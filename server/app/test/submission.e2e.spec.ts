/**
 * e2e: the editor's level submission flow against a real PostgreSQL, walking
 * the requests of `BeatmapSubmissionScreen.cs` (osu.Game/Screens/Edit/Submission)
 * exactly as the client sends them:
 *
 *   register → token → PUT /beatmap-submission/beatmapsets (reserve ids) →
 *   export a package with those ids → PUT /beatmap-submission/beatmapsets/{id}
 *   (or a PATCH of the changed files) → the level appears under its search
 *   category and on the creator's profile → the admin CLI ranks it → the
 *   leaderboard and the profile counts follow.
 *
 * The packages are generated here (silent WAV + 1x1 PNG, no copyrighted audio)
 * in the shape `SubmissionBeatmapExporter` produces: one .osu per difficulty
 * carrying `BeatmapID: {id}` / `BeatmapSetID: {id}` with the ids the server
 * reserved, so the stored files can stay byte for byte what the creator's
 * client keeps locally (the checksums must match, lazer-protocol.md §8.2).
 *
 * The suite uses its own database on the server `TEST_DATABASE_URL` points at,
 * so it can run next to the other suites.
 */
import { INestApplication } from '@nestjs/common';
import { NestFactory } from '@nestjs/core';
import AdmZip from 'adm-zip';
import { createHash } from 'node:crypto';
import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import request from 'supertest';
import { Pool } from 'pg';
import { AppModule } from '../src/app.module';
import { configureApp } from '../src/app.setup';
import { loadConfig } from '../src/config';
import { createPool } from '../src/db/db.service';
import { runMigrations } from '../src/db/migrate';
import { archiveKeyFor } from '../src/beatmaps/importer';
import { deleteLevel, listLevels, setLevelStatus } from '../src/admin/level-admin';
import { createStorage } from '../src/storage/create-storage';
import {
  FIXTURE_ARTIST,
  FIXTURE_CREATOR,
  FIXTURE_TITLE,
  minimalOsu,
  silentWav,
  tinyPng,
} from './fixture';

const TEST_DATABASE_URL =
  process.env.TEST_DATABASE_URL ?? 'postgres://kan:kan@localhost:55432/kan_test';
/** Its own database, on the server TEST_DATABASE_URL points at. */
const SUBMISSION_DATABASE_URL = (() => {
  const url = new URL(TEST_DATABASE_URL);
  url.pathname = '/kan_submission_test';
  return url.toString();
})();
const OAUTH_CLIENT_SECRET = 'e2e-client-secret';

jest.setTimeout(120_000);

interface Tokens {
  access_token: string;
  refresh_token: string;
  expires_in: number;
}

let pool: Pool;
let storageDir: string;

async function createTestApp(env: Record<string, string> = {}): Promise<INestApplication> {
  const previous: Record<string, string | undefined> = {};
  for (const [key, value] of Object.entries(env)) {
    previous[key] = process.env[key];
    process.env[key] = value;
  }
  try {
    const app = await NestFactory.create(AppModule, { bodyParser: false, logger: false });
    configureApp(app);
    await app.init();
    return app;
  } finally {
    for (const [key, value] of Object.entries(previous)) {
      if (value == null) delete process.env[key];
      else process.env[key] = value;
    }
  }
}

beforeAll(async () => {
  storageDir = mkdtempSync(path.join(tmpdir(), 'kan-submission-e2e-'));

  const adminUrl = new URL(SUBMISSION_DATABASE_URL);
  const dbName = adminUrl.pathname.replace(/^\//, '');
  adminUrl.pathname = '/postgres';
  const admin = createPool(adminUrl.toString());
  await admin.query(`DROP DATABASE IF EXISTS ${dbName} WITH (FORCE)`);
  await admin.query(`CREATE DATABASE ${dbName}`);
  await admin.end();

  pool = createPool(SUBMISSION_DATABASE_URL);
  await runMigrations(pool, path.join(__dirname, '..', 'migrations'));

  process.env.DATABASE_URL = SUBMISSION_DATABASE_URL;
  process.env.STORAGE_DRIVER = 'local';
  process.env.STORAGE_LOCAL_DIR = storageDir;
  process.env.OAUTH_CLIENT_ID = 'kan-client';
  process.env.OAUTH_CLIENT_SECRET = OAUTH_CLIENT_SECRET;
});

afterAll(async () => {
  await pool?.end();
  if (storageDir) rmSync(storageDir, { recursive: true, force: true });
});

/** Registration exactly as the game sends it (RegistrationRequest.cs). */
async function register(app: INestApplication, username: string, email: string, password: string) {
  return request(app.getHttpServer())
    .post('/users')
    .field('user[username]', username)
    .field('user[user_email]', email)
    .field('user[password]', password)
    .set('Accept', 'application/json');
}

/** The token request exactly as the game sends it (OAuth.cs). */
async function login(app: INestApplication, username: string, password: string): Promise<Tokens> {
  const response = await request(app.getHttpServer())
    .post('/oauth/token')
    .field('grant_type', 'password')
    .field('client_id', 'kan-client')
    .field('client_secret', OAUTH_CLIENT_SECRET)
    .field('scope', '*')
    .field('username', username)
    .field('password', password);
  expect(response.status).toBe(200);
  return response.body as Tokens;
}

/** One `.osu` as the client's exporter writes it (ids with the space). */
function difficultyOsu(options: {
  version: string;
  beatmapId: number;
  setId: number;
  mode?: number;
}): Buffer {
  const text = minimalOsu({ version: options.version, mode: options.mode })
    .replace('BeatmapID:0', `BeatmapID: ${options.beatmapId}`)
    .replace('BeatmapSetID:0', `BeatmapSetID: ${options.setId}`);
  return Buffer.from(text, 'utf8');
}

/**
 * Builds a package the way `SubmissionBeatmapExporter` exports one: one `.osu`
 * per difficulty carrying the ids the server reserved (the `BeatmapID: 5` form
 * `LegacyBeatmapEncoder` writes, with the space), plus the set's files.
 */
function submissionPackage(options: {
  setId: number;
  difficulties: { version: string; beatmapId: number; mode?: number }[];
  files?: Record<string, Buffer>;
}): Buffer {
  const zip = new AdmZip();
  for (const difficulty of options.difficulties) {
    zip.addFile(
      osuName(difficulty.version),
      difficultyOsu({ ...difficulty, setId: options.setId }),
    );
  }
  for (const [name, data] of Object.entries(options.files ?? {})) zip.addFile(name, data);
  return zip.toBuffer();
}

/** The files of a zip, by entry name. */
function zipFiles(archive: Buffer): Map<string, Buffer> {
  const files = new Map<string, Buffer>();
  for (const entry of new AdmZip(archive).getEntries()) {
    if (!entry.isDirectory) files.set(entry.entryName, entry.getData());
  }
  return files;
}

const md5 = (data: Buffer): string => createHash('md5').update(data).digest('hex');
const sha2 = (data: Buffer): string => createHash('sha256').update(data).digest('hex');

/** The archive entry name `SubmissionBeatmapExporter` gives a difficulty. */
const osuName = (version: string): string =>
  `${FIXTURE_ARTIST} - ${FIXTURE_TITLE} (${FIXTURE_CREATOR}) [${version}].osu`;

describe('level submission (the editor flow)', () => {
  let app: INestApplication;
  let creatorTokens: Tokens;
  let visitorTokens: Tokens;
  let creatorId: number;
  let setId: number;
  let beatmapIds: number[] = [];
  let firstPackage: Buffer;

  const auth = (): string => `Bearer ${creatorTokens.access_token}`;

  /** `PUT /beatmap-submission/beatmapsets` with the body of PutBeatmapSetRequest. */
  function reserve(body: Record<string, unknown>) {
    return request(app.getHttpServer())
      .put('/beatmap-submission/beatmapsets')
      .set('Authorization', auth())
      .set('Content-Type', 'application/json')
      .send(body);
  }

  /** A search, as the listing overlay sends it. */
  function search(query: string) {
    return request(app.getHttpServer()).get(`/api/v2/beatmapsets/search?${query}`);
  }

  beforeAll(async () => {
    app = await createTestApp();
  });

  afterAll(async () => {
    await app?.close();
  });

  it('reserves the ids of a new set exactly as PutBeatmapSetRequest asks', async () => {
    const created = await register(app, 'KairoPhos', 'kairophos@proton.me', 'a line from a song');
    expect(created.status).toBe(201);
    creatorId = created.body.id;
    creatorTokens = await login(app, 'KairoPhos', 'a line from a song');

    const guest = await request(app.getHttpServer())
      .put('/beatmap-submission/beatmapsets')
      .set('Content-Type', 'application/json')
      .send({ beatmaps_to_create: 1, beatmaps_to_keep: [], target: 'Pending' });
    expect(guest.status).toBe(401);

    const response = await reserve({
      beatmapset_id: null,
      beatmaps_to_create: 2,
      beatmaps_to_keep: [],
      target: 'Pending',
      notify_on_discussion_replies: true,
    });
    expect(response.status).toBe(200);
    setId = response.body.beatmapset_id;
    expect(typeof setId).toBe('number');
    expect(setId).toBeGreaterThan(0);

    // PutBeatmapSetResponse: the ids the exporter writes into the package.
    beatmapIds = response.body.beatmap_ids;
    expect(beatmapIds).toHaveLength(2);
    expect(new Set(beatmapIds).size).toBe(2);
    for (const id of beatmapIds) expect(id).toBeGreaterThan(0);
    // nothing stored yet: the client uploads the full package
    expect(response.body.files).toEqual([]);

    const set = await request(app.getHttpServer()).get(`/api/v2/beatmapsets/${setId}`);
    expect(set.status).toBe(200);
    expect(set.body.status).toBe(0);
    expect(set.body.creator).toBe('KairoPhos');
    expect(set.body.user_id).toBe(creatorId);
    expect(set.body.ranked_date).toBeNull();
    expect(set.body.submitted_date).toBeTruthy();
  });

  it('accepts the package exactly as ReplaceBeatmapPackageRequest sends it', async () => {
    firstPackage = submissionPackage({
      setId,
      difficulties: [
        { version: 'Normal', beatmapId: beatmapIds[0] },
        { version: 'Hard', beatmapId: beatmapIds[1] },
      ],
      files: {
        'audio.wav': silentWav(),
        'bg.png': tinyPng(),
        'hitsound.wav': Buffer.from('kan! hitsound', 'utf8'),
      },
    });

    const response = await request(app.getHttpServer())
      .put(`/beatmap-submission/beatmapsets/${setId}`)
      .set('Authorization', auth())
      .attach('beatmapArchive', firstPackage, 'export.osz');
    expect(response.status).toBe(200);

    // the set is credited to the creator and keeps the reserved ids
    const set = await request(app.getHttpServer()).get(`/api/v2/beatmapsets/${setId}`);
    expect(set.body.beatmaps.map((b: { id: number }) => b.id).sort()).toEqual(
      [...beatmapIds].sort(),
    );
    for (const beatmap of set.body.beatmaps) {
      const uploaded = zipFiles(firstPackage).get(osuName(beatmap.version));
      expect(uploaded).toBeDefined();
      // the checksum is the md5 of the file as the client exported it: the
      // creator's local copy must resolve through the lookup after import
      expect(beatmap.checksum).toBe(md5(uploaded as Buffer));
    }

    // the lookup the client runs after re-importing its own export
    for (const [name, data] of zipFiles(firstPackage)) {
      if (!name.endsWith('.osu')) continue;
      const lookup = await request(app.getHttpServer()).get(
        `/api/v2/beatmaps/lookup?checksum=${md5(data)}&filename=${encodeURIComponent(name)}`,
      );
      expect(lookup.status).toBe(200);
      expect(lookup.body.beatmapset_id).toBe(setId);
      expect(beatmapIds).toContain(lookup.body.id);
    }

    // the download carries the very same files
    const download = await request(app.getHttpServer())
      .get(`/api/v2/beatmapsets/${setId}/download`)
      .set('Authorization', auth())
      .buffer(true)
      .parse((res, callback) => {
        const chunks: Buffer[] = [];
        res.on('data', (chunk: Buffer) => chunks.push(chunk));
        res.on('end', () => callback(null, Buffer.concat(chunks)));
      });
    expect(download.status).toBe(200);
    expect([...zipFiles(download.body as Buffer).keys()].sort()).toEqual(
      [...zipFiles(firstPackage).keys()].sort(),
    );
    for (const [name, data] of zipFiles(firstPackage)) {
      expect(md5(zipFiles(download.body as Buffer).get(name) as Buffer)).toBe(md5(data));
    }
  });

  it('lists the level under its search categories only', async () => {
    const pending = await search('s=pending&sort=updated_desc&nsfw=false');
    expect(pending.body.beatmapsets.map((s: { id: number }) => s.id)).toContain(setId);
    // the client labels this category "Pending & WIP" (SearchCategory.cs)
    const wip = await search('s=wip&sort=updated_desc&nsfw=false');
    expect(wip.body.beatmapsets.map((s: { id: number }) => s.id)).not.toContain(setId);

    // ... and never in the categories a player opens by default
    for (const category of ['any', 'leaderboard', 'ranked', 'loved', 'graveyard']) {
      const response = await search(`s=${category}&sort=updated_desc&nsfw=false`);
      expect(response.body.beatmapsets.map((s: { id: number }) => s.id)).not.toContain(setId);
    }

    // the creator finds their own level under "mine"
    const mine = await request(app.getHttpServer())
      .get('/api/v2/beatmapsets/search?s=mine&sort=updated_desc&nsfw=false')
      .set('Authorization', auth());
    expect(mine.body.beatmapsets.map((s: { id: number }) => s.id)).toContain(setId);
  });

  it('shows the level and the beatmapset counts on the creator profile', async () => {
    const pending = await request(app.getHttpServer()).get(
      `/api/v2/users/${creatorId}/beatmapsets/pending?offset=0&limit=50`,
    );
    expect(pending.status).toBe(200);
    expect(pending.body).toHaveLength(1);
    expect(pending.body[0].id).toBe(setId);
    expect(pending.body[0].beatmaps).toHaveLength(2);

    for (const type of ['ranked', 'loved', 'graveyard', 'favourite', 'guest', 'nominated', 'most_played']) {
      const response = await request(app.getHttpServer()).get(
        `/api/v2/users/${creatorId}/beatmapsets/${type}?offset=0&limit=50`,
      );
      expect(response.status).toBe(200);
      expect(response.body).toEqual([]);
    }

    const unknown = await request(app.getHttpServer()).get(
      `/api/v2/users/${creatorId}/beatmapsets/nonsense`,
    );
    expect(unknown.status).toBe(404);

    const profile = await request(app.getHttpServer()).get(`/api/v2/users/${creatorId}/?key=id`);
    expect(profile.status).toBe(200);
    expect(profile.body.pending_beatmapset_count).toBe(1);
    expect(profile.body.ranked_beatmapset_count).toBe(0);
    expect(profile.body.loved_beatmapset_count).toBe(0);
    expect(profile.body.graveyard_beatmapset_count).toBe(0);
    expect(profile.body.favourite_beatmapset_count).toBe(0);
    expect(profile.body.guest_beatmapset_count).toBe(0);
    expect(profile.body.nominated_beatmapset_count).toBe(0);
  });

  it('keeps unranked levels off the leaderboard', async () => {
    const beatmapId = beatmapIds[0];
    const checksum = md5(zipFiles(firstPackage).get(osuName('Normal')) as Buffer);

    // the client plays and submits whatever the status is (SoloPlayer.cs)
    const token = await request(app.getHttpServer())
      .post(`/api/v2/beatmaps/${beatmapId}/solo/scores`)
      .set('Authorization', auth())
      .field('version_hash', 'build')
      .field('beatmap_hash', checksum)
      .field('ruleset_id', '0');
    expect(token.status).toBe(201);

    const submitted = await request(app.getHttpServer())
      .put(`/api/v2/beatmaps/${beatmapId}/solo/scores/${token.body.id}`)
      .set('Authorization', auth())
      .send({ rank: 'S', total_score: 100000, ruleset_id: 0, passed: true });
    expect(submitted.status).toBe(200);
    expect(submitted.body.position).toBeNull();

    // but a pending level has no public leaderboard, as in osu!
    const leaderboard = await request(app.getHttpServer()).get(
      `/api/v2/beatmaps/${beatmapId}/scores?type=global&mode=kan&limit=50`,
    );
    expect(leaderboard.status).toBe(200);
    expect(leaderboard.body.scores).toEqual([]);
    expect(leaderboard.body.score_count).toBe(0);
    expect(leaderboard.body.user_score).toBeNull();
  });

  it('updates the level with a patch of the changed files', async () => {
    // what the client sends on a second submission of a set it already has
    // online (PutBeatmapSetRequest.UpdateExisting): keep what is there, add
    // one new difficulty
    const response = await reserve({
      beatmapset_id: setId,
      beatmaps_to_keep: beatmapIds,
      beatmaps_to_create: 1,
      target: 'Pending',
      notify_on_discussion_replies: false,
    });
    expect(response.status).toBe(200);
    expect(response.body.beatmapset_id).toBe(setId);

    // the files already stored, with the sha2_hash the client diffs by
    const files: { filename: string; sha2_hash: string }[] = response.body.files;
    expect(files.map((file) => file.filename).sort()).toEqual(
      [...zipFiles(firstPackage).keys()].sort(),
    );
    for (const file of files) {
      const stored = zipFiles(firstPackage).get(file.filename) as Buffer;
      expect(file.sha2_hash).toBe(sha2(stored));
    }

    // new ids come first: the exporter hands them to difficulties that have
    // none yet, without consuming a kept id
    const newIds: number[] = response.body.beatmap_ids;
    expect(newIds).toHaveLength(3);
    expect(newIds.slice(1)).toEqual(beatmapIds);
    const newId = newIds[0];
    expect(beatmapIds).not.toContain(newId);

    // the patch itself, exactly as PatchBeatmapPackageRequest sends it: the
    // changed audio, one new difficulty, and one file the creator removed
    const changedAudio = Buffer.concat([silentWav(), Buffer.from('kan!', 'utf8')]);
    const insaneName = osuName('Insane');
    const patch = await request(app.getHttpServer())
      .patch(`/beatmap-submission/beatmapsets/${setId}`)
      .set('Authorization', auth())
      .attach('filesChanged', changedAudio, 'audio.wav')
      .attach(
        'filesChanged',
        difficultyOsu({ version: 'Insane', beatmapId: newId, setId }),
        insaneName,
      )
      .field('filesDeleted', 'hitsound.wav');
    expect(patch.status).toBe(200);

    // the stored package is the merged one: changed files updated, the deleted
    // one gone, everything else byte for byte as uploaded
    const download = await request(app.getHttpServer())
      .get(`/api/v2/beatmapsets/${setId}/download`)
      .set('Authorization', auth())
      .buffer(true)
      .parse((res, callback) => {
        const chunks: Buffer[] = [];
        res.on('data', (chunk: Buffer) => chunks.push(chunk));
        res.on('end', () => callback(null, Buffer.concat(chunks)));
      });
    const stored = zipFiles(download.body as Buffer);
    expect([...stored.keys()].sort()).toEqual(
      [osuName('Hard'), osuName('Insane'), osuName('Normal'), 'audio.wav', 'bg.png'].sort(),
    );
    expect(md5(stored.get('audio.wav') as Buffer)).toBe(md5(changedAudio));
    const original = zipFiles(firstPackage);
    for (const name of [osuName('Normal'), osuName('Hard'), 'bg.png']) {
      expect(md5(stored.get(name) as Buffer)).toBe(md5(original.get(name) as Buffer));
    }

    // the third difficulty joined the set, with the id it was given
    const set = await request(app.getHttpServer()).get(`/api/v2/beatmapsets/${setId}`);
    expect(set.body.beatmaps.map((b: { id: number }) => b.id).sort()).toEqual(
      [...beatmapIds, newId].sort(),
    );
    beatmapIds = [...beatmapIds, newId];

    // and its checksum resolves, like the client's local copy after import
    const lookup = await request(app.getHttpServer()).get(
      `/api/v2/beatmaps/lookup?checksum=${md5(stored.get(insaneName) as Buffer)}`,
    );
    expect(lookup.status).toBe(200);
    expect(lookup.body.id).toBe(newId);
  });

  it('refuses a replace or a patch from anyone but the creator', async () => {
    const visitor = await register(app, 'LevelGuest', 'levelguest@example.org', 'another good password');
    expect(visitor.status).toBe(201);
    visitorTokens = await login(app, 'LevelGuest', 'another good password');

    const setBefore = await request(app.getHttpServer()).get(`/api/v2/beatmapsets/${setId}`);
    const beatmapsBefore = setBefore.body.beatmaps.length;

    const replace = await request(app.getHttpServer())
      .put(`/beatmap-submission/beatmapsets/${setId}`)
      .set('Authorization', `Bearer ${visitorTokens.access_token}`)
      .attach('beatmapArchive', firstPackage, 'export.osz');
    expect(replace.status).toBe(403);
    expect(replace.body.error).toBeTruthy();

    const patch = await request(app.getHttpServer())
      .patch(`/beatmap-submission/beatmapsets/${setId}`)
      .set('Authorization', `Bearer ${visitorTokens.access_token}`)
      .attach('filesChanged', silentWav(), 'audio.wav');
    expect(patch.status).toBe(403);

    const reserveUpdate = await request(app.getHttpServer())
      .put('/beatmap-submission/beatmapsets')
      .set('Authorization', `Bearer ${visitorTokens.access_token}`)
      .set('Content-Type', 'application/json')
      .send({ beatmapset_id: setId, beatmaps_to_keep: [], beatmaps_to_create: 0, target: 'WIP' });
    expect(reserveUpdate.status).toBe(403);

    // nothing changed for the creator's level
    const setAfter = await request(app.getHttpServer()).get(`/api/v2/beatmapsets/${setId}`);
    expect(setAfter.body.beatmaps).toHaveLength(beatmapsBefore);

    // and an unknown set is simply not there
    const missing = await request(app.getHttpServer())
      .put('/beatmap-submission/beatmapsets/424242')
      .set('Authorization', `Bearer ${visitorTokens.access_token}`)
      .attach('beatmapArchive', firstPackage, 'export.osz');
    expect(missing.status).toBe(404);
  });

  it('moves the level between statuses from the admin CLI', async () => {
    const storage = createStorage(loadConfig());

    // `set-status <id> rank`: only the server owner ranks levels
    const ranked = await setLevelStatus(pool, setId, 'rank');
    expect(ranked).toMatchObject({ id: setId, creator: 'KairoPhos', status: 1 });

    const set = await request(app.getHttpServer()).get(`/api/v2/beatmapsets/${setId}`);
    expect(set.body.status).toBe(1);
    expect(set.body.ranked_date).toBeTruthy();

    // it moved out of the pending category and into the default ones
    for (const category of ['any', 'leaderboard', 'ranked']) {
      const response = await search(`s=${category}&sort=updated_desc&nsfw=false`);
      expect(response.body.beatmapsets.map((s: { id: number }) => s.id)).toContain(setId);
    }
    const pending = await search('s=pending&sort=updated_desc&nsfw=false');
    expect(pending.body.beatmapsets.map((s: { id: number }) => s.id)).not.toContain(setId);

    // the profile counts follow
    const profile = await request(app.getHttpServer()).get(`/api/v2/users/${creatorId}/?key=id`);
    expect(profile.body.ranked_beatmapset_count).toBe(1);
    expect(profile.body.pending_beatmapset_count).toBe(0);
    const rankedSets = await request(app.getHttpServer()).get(
      `/api/v2/users/${creatorId}/beatmapsets/ranked?offset=0&limit=50`,
    );
    expect(rankedSets.body).toHaveLength(1);
    expect(rankedSets.body[0].id).toBe(setId);

    // the score set while the level was pending shows on the leaderboard now
    const leaderboard = await request(app.getHttpServer())
      .get(`/api/v2/beatmaps/${beatmapIds[0]}/scores?type=global&mode=kan&limit=50`)
      .set('Authorization', auth());
    expect(leaderboard.body.score_count).toBe(1);
    expect(leaderboard.body.user_score.position).toBe(1);

    // back to a working status: the ranked date goes away again
    const wip = await setLevelStatus(pool, setId, 'wip');
    expect(wip?.status).toBe(-1);
    const afterWip = await request(app.getHttpServer()).get(`/api/v2/beatmapsets/${setId}`);
    expect(afterWip.body.status).toBe(-1);
    expect(afterWip.body.ranked_date).toBeNull();

    // and `levels` lists it with whatever status it has
    const levels = await listLevels(pool);
    expect(levels).toContainEqual(
      expect.objectContaining({ id: setId, creator: 'KairoPhos', status: -1 }),
    );

    // `set-status <id> loved` for the last status the task lists
    const loved = await setLevelStatus(pool, setId, 'loved');
    expect(loved?.status).toBe(4);
    const lovedSearch = await search('s=loved&sort=updated_desc&nsfw=false');
    expect(lovedSearch.body.beatmapsets.map((s: { id: number }) => s.id)).toContain(setId);

    // `delete-level <id> --yes`: the level goes with its package and cover
    const coverKey = (await pool.query<{ cover_key: string | null }>(
      'SELECT cover_key FROM beatmapsets WHERE id = $1',
      [setId],
    )).rows[0]?.cover_key;
    expect(coverKey).toBeTruthy();
    expect(await storage.exists(archiveKeyFor(setId))).toBe(true);

    const deleted = await deleteLevel(pool, storage, setId);
    expect(deleted).toMatchObject({ id: setId, status: 4 });
    expect(await storage.exists(archiveKeyFor(setId))).toBe(false);
    expect(await storage.exists(coverKey as string)).toBe(false);
    const rows = await pool.query('SELECT 1 FROM beatmapsets WHERE id = $1', [setId]);
    expect(rows.rowCount).toBe(0);
    const beatmaps = await pool.query('SELECT 1 FROM beatmaps WHERE beatmapset_id = $1', [setId]);
    expect(beatmaps.rowCount).toBe(0);

    const gone = await request(app.getHttpServer()).get(`/api/v2/beatmapsets/${setId}`);
    expect(gone.status).toBe(404);
  });
});

describe('submission package size limit', () => {
  let app: INestApplication;
  let tokens: Tokens;

  beforeAll(async () => {
    // A small limit keeps the test light; the default is 100 MB per package.
    app = await createTestApp({ SUBMISSION_MAX_PACKAGE_BYTES: '2048' });
    const created = await register(app, 'LimitTester', 'limits@example.org', 'a line from a song');
    expect(created.status).toBe(201);
    tokens = await login(app, 'LimitTester', 'a line from a song');
  });

  afterAll(async () => {
    await app?.close();
  });

  it('refuses a package over the size limit and accepts one under it', async () => {
    const created = await request(app.getHttpServer())
      .put('/beatmap-submission/beatmapsets')
      .set('Authorization', `Bearer ${tokens.access_token}`)
      .set('Content-Type', 'application/json')
      .send({ beatmaps_to_create: 1, beatmaps_to_keep: [], target: 'WIP' });
    expect(created.status).toBe(200);
    const setId = created.body.beatmapset_id as number;
    const [beatmapId] = created.body.beatmap_ids as number[];

    const oversized = submissionPackage({
      setId,
      difficulties: [{ version: 'Normal', beatmapId }],
      files: { 'audio.wav': silentWav(), 'big.bin': Buffer.alloc(8192) },
    });
    const refused = await request(app.getHttpServer())
      .put(`/beatmap-submission/beatmapsets/${setId}`)
      .set('Authorization', `Bearer ${tokens.access_token}`)
      .attach('beatmapArchive', oversized, 'export.osz');
    expect(refused.status).toBe(413);
    expect(refused.body.error).toContain('2048');

    const small = submissionPackage({
      setId,
      difficulties: [{ version: 'Normal', beatmapId }],
      files: { 'audio.wav': silentWav() },
    });
    const accepted = await request(app.getHttpServer())
      .put(`/beatmap-submission/beatmapsets/${setId}`)
      .set('Authorization', `Bearer ${tokens.access_token}`)
      .attach('beatmapArchive', small, 'export.osz');
    expect(accepted.status).toBe(200);
  });
});

describe('submission upload rate limit', () => {
  let app: INestApplication;
  let tokens: Tokens;

  beforeAll(async () => {
    app = await createTestApp({ SUBMISSION_UPLOADS_PER_HOUR: '2' });
    const created = await register(app, 'RateTester', 'rates@example.org', 'a line from a song');
    expect(created.status).toBe(201);
    tokens = await login(app, 'RateTester', 'a line from a song');
  });

  afterAll(async () => {
    await app?.close();
  });

  it('refuses uploads over the rate limit, per creator', async () => {
    const auth = `Bearer ${tokens.access_token}`;
    const created = await request(app.getHttpServer())
      .put('/beatmap-submission/beatmapsets')
      .set('Authorization', auth)
      .set('Content-Type', 'application/json')
      .send({ beatmaps_to_create: 1, beatmaps_to_keep: [], target: 'Pending' });
    expect(created.status).toBe(200);
    const setId = created.body.beatmapset_id as number;
    const [beatmapId] = created.body.beatmap_ids as number[];
    const package1 = submissionPackage({
      setId,
      difficulties: [{ version: 'Normal', beatmapId }],
      files: { 'audio.wav': silentWav() },
    });

    const upload = () =>
      request(app.getHttpServer())
        .put(`/beatmap-submission/beatmapsets/${setId}`)
        .set('Authorization', auth)
        .attach('beatmapArchive', package1, 'export.osz');

    expect((await upload()).status).toBe(200);
    expect((await upload()).status).toBe(200);
    const refused = await upload();
    expect(refused.status).toBe(429);
    expect(refused.body.error).toBeTruthy();

    // reserving ids is not an upload and stays available
    const reserved = await request(app.getHttpServer())
      .put('/beatmap-submission/beatmapsets')
      .set('Authorization', auth)
      .set('Content-Type', 'application/json')
      .send({ beatmapset_id: setId, beatmaps_to_keep: [beatmapId], beatmaps_to_create: 0, target: 'Pending' });
    expect(reserved.status).toBe(200);
  });
});

describe('unranked set cap', () => {
  let app: INestApplication;
  let tokens: Tokens;

  beforeAll(async () => {
    // osu!'s rule with a small allowance: two unranked levels, plus one per
    // ranked level, up to three.
    app = await createTestApp({
      SUBMISSION_UNRANKED_SETS_BASE: '2',
      SUBMISSION_UNRANKED_SETS_MAX: '3',
    });
    const created = await register(app, 'CapTester', 'caps@example.org', 'a line from a song');
    expect(created.status).toBe(201);
    tokens = await login(app, 'CapTester', 'a line from a song');
  });

  afterAll(async () => {
    await app?.close();
  });

  function reserve(target: 'WIP' | 'Pending') {
    return request(app.getHttpServer())
      .put('/beatmap-submission/beatmapsets')
      .set('Authorization', `Bearer ${tokens.access_token}`)
      .set('Content-Type', 'application/json')
      .send({ beatmaps_to_create: 1, beatmaps_to_keep: [], target });
  }

  it('caps how many pending and WIP levels a creator may hold', async () => {
    // a set starts in the status the client's settings ask for
    const first = await reserve('WIP');
    expect(first.status).toBe(200);
    const firstSet = await request(app.getHttpServer()).get(
      `/api/v2/beatmapsets/${first.body.beatmapset_id}`,
    );
    expect(firstSet.body.status).toBe(-1);

    const second = await reserve('Pending');
    expect(second.status).toBe(200);
    const secondSet = await request(app.getHttpServer()).get(
      `/api/v2/beatmapsets/${second.body.beatmapset_id}`,
    );
    expect(secondSet.body.status).toBe(0);

    // the base allowance of two unranked levels is used up
    const refused = await reserve('Pending');
    expect(refused.status).toBe(422);
    expect(refused.body.error).toBeTruthy();

    // every ranked level adds a slot, up to the maximum of three
    await setLevelStatus(pool, first.body.beatmapset_id as number, 'rank');
    const third = await reserve('Pending');
    expect(third.status).toBe(200);
    const fourth = await reserve('Pending');
    expect(fourth.status).toBe(200);
    const refusedAgain = await reserve('Pending');
    expect(refusedAgain.status).toBe(422);
  });
});
