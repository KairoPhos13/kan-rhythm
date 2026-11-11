/**
 * e2e: walks the osu!lazer client's flow against a real PostgreSQL, using the
 * exact JSON shapes from server/docs/lazer-protocol.md:
 *
 *   register → token → me → import a generated .osz → search → download →
 *   lookup by checksum → score token → submit → leaderboard → second user
 *   ranks first → profile + profile scores.
 *
 * The fixture archive is generated in-test (silent WAV + 1x1 PNG, no
 * copyrighted audio) and the `local` storage driver is used; the s3 redirect
 * path is unit-tested separately in storage-redirect.spec.ts.
 *
 * The forms are sent exactly as the game sends them: multipart/form-data with
 * the field names of RegistrationRequest.cs and OAuth.cs (osu!framework posts
 * every AddParameter body that way, see src/common/multipart.ts). One
 * urlencoded case stays so both body encodings keep working.
 *
 * The second describe covers the newer registration rules (common passwords,
 * the website CORS header) with its own app instance: the registration rate
 * limiter is in-memory and allows 5 attempts per hour per client IP
 * (src/users/registration.controller.ts), which the flow above already spends
 * 4 of.
 */
import { INestApplication } from '@nestjs/common';
import { NestFactory } from '@nestjs/core';
import AdmZip from 'adm-zip';
import { createHash } from 'node:crypto';
import { mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import request from 'supertest';
import { Pool } from 'pg';
import { AppModule } from '../src/app.module';
import { configureApp } from '../src/app.setup';
import { loadConfig } from '../src/config';
import { createPool } from '../src/db/db.service';
import { runMigrations } from '../src/db/migrate';
import { importOsz, ImportResult } from '../src/beatmaps/importer';
import { createStorage } from '../src/storage/create-storage';
import { buildOsz, FIXTURE_ARTIST, FIXTURE_TITLE } from './fixture';

const TEST_DATABASE_URL =
  process.env.TEST_DATABASE_URL ?? 'postgres://kan:kan@localhost:55432/kan_test';
const OAUTH_CLIENT_SECRET = 'e2e-client-secret';
/** The website whose sign-up form is the only origin the server CORS-allows. */
const WEBSITE_URL = 'https://kan.example.org';

jest.setTimeout(120_000);

interface Tokens {
  access_token: string;
  refresh_token: string;
  expires_in: number;
}

// The suite's app instances share one throwaway database (created and
// migrated here) and one environment. WEBSITE_URL is set before any app is
// created: the website CORS middleware (src/common/website-cors.ts) reads it
// at startup.
let pool: Pool;
let storageDir: string;

async function createTestApp(): Promise<INestApplication> {
  const app = await NestFactory.create(AppModule, { bodyParser: false, logger: false });
  configureApp(app);
  await app.init();
  return app;
}

beforeAll(async () => {
  storageDir = mkdtempSync(path.join(tmpdir(), 'kan-server-e2e-'));

  // A throwaway database for this run.
  const adminUrl = new URL(TEST_DATABASE_URL);
  const dbName = adminUrl.pathname.replace(/^\//, '');
  adminUrl.pathname = '/postgres';
  const admin = createPool(adminUrl.toString());
  await admin.query(`DROP DATABASE IF EXISTS ${dbName} WITH (FORCE)`);
  await admin.query(`CREATE DATABASE ${dbName}`);
  await admin.end();

  pool = createPool(TEST_DATABASE_URL);
  const migrationsDir = path.join(__dirname, '..', 'migrations');
  await runMigrations(pool, migrationsDir);

  process.env.DATABASE_URL = TEST_DATABASE_URL;
  process.env.STORAGE_DRIVER = 'local';
  process.env.STORAGE_LOCAL_DIR = storageDir;
  process.env.OAUTH_CLIENT_ID = 'kan-client';
  process.env.OAUTH_CLIENT_SECRET = OAUTH_CLIENT_SECRET;
  process.env.WEBSITE_URL = WEBSITE_URL;
});

afterAll(async () => {
  await pool?.end();
  if (storageDir) rmSync(storageDir, { recursive: true, force: true });
});

describe('client flow (lazer protocol)', () => {
  let app: INestApplication;
  let tokensA: Tokens;
  let tokensB: Tokens;
  let userAId: number;
  let userBId: number;
  let imported: ImportResult;
  let fixturePath: string;
  let downloadPath: string;

  beforeAll(async () => {
    app = await createTestApp();
  });

  afterAll(async () => {
    await app?.close();
    if (fixturePath) rmSync(fixturePath, { force: true });
    if (downloadPath) rmSync(downloadPath, { force: true });
  });

  /**
   * Registration exactly as the game sends it (RegistrationRequest.cs): a
   * multipart form with the `user[...]` field names.
   */
  async function register(username: string, email: string, password: string) {
    return request(app.getHttpServer())
      .post('/users')
      .field('user[username]', username)
      .field('user[user_email]', email)
      .field('user[password]', password)
      .set('Accept', 'application/json');
  }

  /** The token request exactly as the game sends it (OAuth.cs): a multipart form. */
  async function login(username: string, password: string): Promise<Tokens> {
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

  /** The stored `users.last_visit` as epoch milliseconds, 0 when never written. */
  async function storedLastVisit(userId: number): Promise<number> {
    const result = await pool.query<{ last_visit: Date | null }>(
      'SELECT last_visit FROM users WHERE id = $1',
      [userId],
    );
    return result.rows[0]?.last_visit?.getTime() ?? 0;
  }

  it('registers a player with the client registration form fields', async () => {
    const response = await register('AliceOsu', 'alice@example.org', 'correct horse battery');
    expect(response.status).toBe(201);
    expect(response.body.username).toBe('AliceOsu');
    userAId = response.body.id;
    expect(typeof userAId).toBe('number');
    // The client treats ids <= 1 as "no real account" (migration 0005).
    expect(userAId).toBeGreaterThanOrEqual(2);
  });

  it('answers registration validation failures in the form_error shape', async () => {
    const response = await register('x', 'not-an-email', 'short');
    expect(response.status).toBe(422);
    expect(response.body.form_error.user.username[0]).toContain('3 and 25');
    expect(response.body.form_error.user.user_email[0]).toContain('not valid');
    expect(response.body.form_error.user.password[0]).toContain('8 characters');

    // The one urlencoded case stays (the game posts multipart): the website's
    // sign-up form posts x-www-form-urlencoded (web/index.html), so both body
    // encodings stay covered.
    const duplicate = await request(app.getHttpServer())
      .post('/users')
      .type('form')
      .send({
        user: {
          username: 'aliceosu',
          user_email: 'other@example.org',
          password: 'correct horse battery',
        },
      })
      .set('Accept', 'application/json');
    expect(duplicate.status).toBe(422);
    expect(duplicate.body.form_error.user.username[0]).toContain('taken');
  });

  it('issues tokens via the password grant', async () => {
    tokensA = await login('AliceOsu', 'correct horse battery');
    expect(tokensA.access_token).toBeTruthy();
    expect(tokensA.refresh_token).toBeTruthy();
    expect(tokensA.expires_in).toBeGreaterThan(30);

    const wrong = await request(app.getHttpServer())
      .post('/oauth/token')
      .field('grant_type', 'password')
      .field('client_id', 'kan-client')
      .field('client_secret', OAUTH_CLIENT_SECRET)
      .field('scope', '*')
      .field('username', 'AliceOsu')
      .field('password', 'wrong-password');
    expect(wrong.status).toBe(400);
    expect(wrong.body.error).toBe('invalid_grant');
    expect(wrong.body.hint).toBeTruthy();
  });

  it('rotates the refresh token on use', async () => {
    const response = await request(app.getHttpServer())
      .post('/oauth/token')
      .field('grant_type', 'refresh_token')
      .field('client_id', 'kan-client')
      .field('client_secret', OAUTH_CLIENT_SECRET)
      .field('scope', '*')
      .field('refresh_token', tokensA.refresh_token);
    expect(response.status).toBe(200);
    expect(response.body.refresh_token).not.toBe(tokensA.refresh_token);
    tokensA = response.body as Tokens;

    // the rotated-away refresh token is no longer valid
    const replay = await request(app.getHttpServer())
      .post('/oauth/token')
      .field('grant_type', 'refresh_token')
      .field('client_id', 'kan-client')
      .field('client_secret', OAUTH_CLIENT_SECRET)
      .field('scope', '*')
      .field('refresh_token', 'definitely-not-a-token');
    expect(replay.status).toBe(400);
    expect(replay.body.error).toBe('invalid_grant');
  });

  it('returns the current user from /api/v2/me', async () => {
    for (const route of ['/api/v2/me', '/api/v2/me/', '/api/v2/me/kan']) {
      const response = await request(app.getHttpServer())
        .get(route)
        .set('Authorization', `Bearer ${tokensA.access_token}`);
      expect(response.status).toBe(200);
      expect(response.body.id).toBe(userAId);
      expect(response.body.username).toBe('AliceOsu');
      expect(response.body.country_code).toBeTruthy();
      expect(response.body.session_verification_method).toBeUndefined();
      expect(response.body.statistics).toBeDefined();
    }

    const invalid = await request(app.getHttpServer())
      .get('/api/v2/me')
      .set('Authorization', 'Bearer not-a-real-token');
    expect(invalid.status).toBe(401);
    expect(invalid.body.error).toBeTruthy();
  });

  it('records last_visit when the player signs in', async () => {
    // Never written before: the sign-in fills it in.
    await pool.query('UPDATE users SET last_visit = NULL WHERE id = $1', [userAId]);
    await login('AliceOsu', 'correct horse battery');
    expect(await storedLastVisit(userAId)).toBeGreaterThan(0);

    // A sign-in always writes, even a minute after the previous one: the
    // five-minute throttle belongs to the /me poll below, not to signing in.
    await pool.query(`UPDATE users SET last_visit = now() - interval '1 minute' WHERE id = $1`, [
      userAId,
    ]);
    const before = await storedLastVisit(userAId);
    await login('AliceOsu', 'correct horse battery');
    expect((await storedLastVisit(userAId)) - before).toBeGreaterThan(30_000);
  });

  it('writes last_visit from /me at most once per five minutes', async () => {
    // Stale on purpose: outside the throttle window, so the first /me writes.
    await pool.query(`UPDATE users SET last_visit = now() - interval '10 minutes' WHERE id = $1`, [
      userAId,
    ]);
    const stale = await storedLastVisit(userAId);

    for (const route of ['/api/v2/me', '/api/v2/me/kan']) {
      const response = await request(app.getHttpServer())
        .get(route)
        .set('Authorization', `Bearer ${tokensA.access_token}`);
      expect(response.status).toBe(200);
    }
    const written = await storedLastVisit(userAId);
    expect(written - stale).toBeGreaterThan(5 * 60 * 1000);

    // Inside the window: the routes answer but must not write again.
    const again = await request(app.getHttpServer())
      .get('/api/v2/me')
      .set('Authorization', `Bearer ${tokensA.access_token}`);
    expect(again.status).toBe(200);
    expect(await storedLastVisit(userAId)).toBe(written);
  });

  it('keeps the optional login-time endpoints quiet with empty collections', async () => {
    for (const route of ['/api/v2/friends', '/api/v2/blocks']) {
      const response = await request(app.getHttpServer())
        .get(route)
        .set('Authorization', `Bearer ${tokensA.access_token}`);
      expect(response.status).toBe(200);
      expect(response.body).toEqual([]);
    }
    const favourites = await request(app.getHttpServer())
      .get('/api/v2/me/beatmapset-favourites')
      .set('Authorization', `Bearer ${tokensA.access_token}`);
    expect(favourites.status).toBe(200);
    expect(favourites.body).toEqual({ beatmapset_ids: [] });
  });

  it('imports a generated .osz fixture with rewritten ids and checksums', async () => {
    const archive = buildOsz({ versions: ['Normal', 'Hard'] });
    fixturePath = path.join(storageDir, 'fixture.osz');
    writeFileSync(fixturePath, archive);

    const storage = createStorage(loadConfig());
    imported = await importOsz({ db: pool, storage }, fixturePath, { ownerUsername: 'AliceOsu' });

    expect(imported.created).toBe(true);
    expect(imported.beatmaps).toHaveLength(2);
    for (const beatmap of imported.beatmaps) {
      expect(beatmap.checksum).toMatch(/^[0-9a-f]{32}$/);
    }

    // every checksum resolves through the lookup the client uses after import
    for (const beatmap of imported.beatmaps) {
      const response = await request(app.getHttpServer()).get(
        `/api/v2/beatmaps/lookup?checksum=${beatmap.checksum}`,
      );
      expect(response.status).toBe(200);
      expect(response.body.id).toBe(beatmap.id);
      expect(response.body.beatmapset_id).toBe(imported.setId);
      expect(response.body.status).toBe(1);
      expect(response.body.mode_int).toBe(0);
      expect(response.body.beatmapset).toBeDefined();
      expect(response.body.beatmapset.id).toBe(imported.setId);
    }
  });

  it('finds the level in the beatmap listing search with cursor paging', async () => {
    const response = await request(app.getHttpServer()).get(
      '/api/v2/beatmapsets/search?q=Fixture&s=ranked&sort=ranked_desc&nsfw=false',
    );
    expect(response.status).toBe(200);
    expect(Array.isArray(response.body.beatmapsets)).toBe(true);
    const set = response.body.beatmapsets.find((s: { id: number }) => s.id === imported.setId);
    expect(set).toBeDefined();
    expect(set.status).toBe(1);
    expect(set.title).toBe(FIXTURE_TITLE);
    expect(set.artist).toBe(FIXTURE_ARTIST);
    expect(set.creator).toBe('KairoPhos');
    expect(set.beatmaps).toHaveLength(2);
    expect(set.covers).toBeDefined();
    expect(set.availability).toEqual({ download_disabled: false, more_information: null });
    expect(set.genre).toEqual({ id: 1, name: 'Unspecified' });
    // no further pages for a single result set
    expect(response.body.cursor ?? null).toBeNull();

    // the client echoes the cursor back as cursor[key]=value parameters
    const second = buildOsz({ title: 'Second Song', artist: 'Other Artist' });
    const secondPath = path.join(storageDir, 'fixture2.osz');
    writeFileSync(secondPath, second);
    const storage = createStorage(loadConfig());
    const secondImport = await importOsz({ db: pool, storage }, secondPath, {
      ownerUsername: 'AliceOsu',
    });

    const firstPage = await request(app.getHttpServer()).get(
      '/api/v2/beatmapsets/search?s=any&sort=ranked_desc&nsfw=false',
    );
    expect(firstPage.body.beatmapsets[0].id).toBe(secondImport.setId);

    const page = await request(app.getHttpServer()).get(
      '/api/v2/beatmapsets/search?s=any&sort=ranked_desc&nsfw=false&cursor[_id]=1',
    );
    expect(page.status).toBe(200);
    expect(page.body.beatmapsets).toHaveLength(1);
    expect(page.body.beatmapsets[0].id).toBe(imported.setId);
  });

  it('serves a download whose .osu files carry the ids and checksums', async () => {
    const guest = await request(app.getHttpServer()).get(
      `/api/v2/beatmapsets/${imported.setId}/download`,
    );
    expect(guest.status).toBe(401);

    const response = await request(app.getHttpServer())
      .get(`/api/v2/beatmapsets/${imported.setId}/download`)
      .set('Authorization', `Bearer ${tokensA.access_token}`)
      .buffer(true)
      .parse((res, callback) => {
        const chunks: Buffer[] = [];
        res.on('data', (chunk: Buffer) => chunks.push(chunk));
        res.on('end', () => callback(null, Buffer.concat(chunks)));
      });
    expect(response.status).toBe(200);

    downloadPath = path.join(storageDir, 'downloaded.osz');
    writeFileSync(downloadPath, response.body as Buffer);

    const zip = new AdmZip(downloadPath);
    const osuEntries = zip.getEntries().filter((e) => !e.isDirectory && e.entryName.endsWith('.osu'));
    expect(osuEntries).toHaveLength(2);

    for (const entry of osuEntries) {
      const text = entry.getData().toString('utf8');
      expect(text).toContain(`BeatmapSetID: ${imported.setId}`);
      const beatmapId = /BeatmapID: (\d+)/.exec(text)?.[1];
      expect(beatmapId).toBeTruthy();
      expect(text).toContain(`BeatmapID: ${beatmapId}`);

      const md5 = createHash('md5').update(entry.getData()).digest('hex');
      const expected = imported.beatmaps.find((b) => b.id === Number(beatmapId));
      expect(expected).toBeDefined();
      expect(md5).toBe(expected?.checksum);

      const lookup = await request(app.getHttpServer()).get(
        `/api/v2/beatmaps/lookup?checksum=${md5}&filename=${encodeURIComponent(entry.entryName)}`,
      );
      expect(lookup.status).toBe(200);
      expect(lookup.body.checksum).toBe(md5);
    }
  });

  it('creates a score token and submits a score', async () => {
    const beatmap = imported.beatmaps[0];

    // Score token creation is a multipart form too (CreateSoloScoreRequest.cs).
    const badHash = await request(app.getHttpServer())
      .post(`/api/v2/beatmaps/${beatmap.id}/solo/scores`)
      .set('Authorization', `Bearer ${tokensA.access_token}`)
      .field('version_hash', 'build')
      .field('beatmap_hash', 'not-the-md5')
      .field('ruleset_id', '0');
    expect(badHash.status).toBe(400);
    expect(badHash.body.error).toBe('invalid or missing beatmap_hash');

    const token = await request(app.getHttpServer())
      .post(`/api/v2/beatmaps/${beatmap.id}/solo/scores`)
      .set('Authorization', `Bearer ${tokensA.access_token}`)
      .field('version_hash', 'build')
      .field('beatmap_hash', beatmap.checksum)
      .field('ruleset_id', '0');
    expect(token.status).toBe(201);
    expect(typeof token.body.id).toBe('number');

    const submitted = await request(app.getHttpServer())
      .put(`/api/v2/beatmaps/${beatmap.id}/solo/scores/${token.body.id}`)
      .set('Authorization', `Bearer ${tokensA.access_token}`)
      .send({
        rank: 'S',
        total_score: 100000,
        total_score_without_mods: 100000,
        accuracy: 0.95,
        max_combo: 5,
        ruleset_id: 0,
        passed: true,
        mods: [],
        statistics: { great: 4, ok: 1, miss: 0 },
        maximum_statistics: { great: 5 },
        pauses: [],
        started_at: '2026-11-09T12:00:00Z',
        ended_at: '2026-11-09T12:01:00Z',
      });
    expect(submitted.status).toBe(200);
    expect(typeof submitted.body.id).toBe('number');
    expect(submitted.body.position).toBe(1);

    // a token can only be used once
    const replay = await request(app.getHttpServer())
      .put(`/api/v2/beatmaps/${beatmap.id}/solo/scores/${token.body.id}`)
      .set('Authorization', `Bearer ${tokensA.access_token}`)
      .send({ rank: 'S', total_score: 1, ruleset_id: 0, passed: true });
    expect(replay.status).toBe(400);
    expect(replay.body.error).toBe('invalid token');
  });

  it('shows the score on the per-level leaderboard', async () => {
    const beatmap = imported.beatmaps[0];
    const response = await request(app.getHttpServer())
      .get(`/api/v2/beatmaps/${beatmap.id}/scores?type=global&mode=kan&limit=50`)
      .set('Authorization', `Bearer ${tokensA.access_token}`);
    expect(response.status).toBe(200);
    expect(response.body.scores).toHaveLength(1);
    expect(response.body.score_count).toBe(1);
    expect(response.body.user_score.position).toBe(1);

    const score = response.body.scores[0];
    expect(score.total_score).toBe(100000);
    expect(score.rank).toBe('S');
    expect(score.user.username).toBe('AliceOsu');
    expect(score.beatmap.id).toBe(beatmap.id);
    expect(score.beatmapset.id).toBe(imported.setId);
    // SoloScoreInfo deserialisation requires beatmap before beatmapset
    const keys = Object.keys(score);
    expect(keys.indexOf('beatmap')).toBeLessThan(keys.indexOf('beatmapset'));
  });

  it('ranks a second player with a better score first', async () => {
    const registered = await register('BobFlute', 'bob@example.org', 'another good password');
    expect(registered.status).toBe(201);
    userBId = registered.body.id;
    tokensB = await login('BobFlute', 'another good password');

    const beatmap = imported.beatmaps[0];
    const token = await request(app.getHttpServer())
      .post(`/api/v2/beatmaps/${beatmap.id}/solo/scores`)
      .set('Authorization', `Bearer ${tokensB.access_token}`)
      .field('version_hash', 'build')
      .field('beatmap_hash', beatmap.checksum)
      .field('ruleset_id', '0');
    expect(token.status).toBe(201);

    const submitted = await request(app.getHttpServer())
      .put(`/api/v2/beatmaps/${beatmap.id}/solo/scores/${token.body.id}`)
      .set('Authorization', `Bearer ${tokensB.access_token}`)
      .send({
        rank: 'X',
        total_score: 250000,
        total_score_without_mods: 250000,
        accuracy: 1,
        max_combo: 5,
        ruleset_id: 0,
        passed: true,
        mods: [],
        statistics: { great: 5 },
        maximum_statistics: { great: 5 },
        pauses: [],
        ended_at: '2026-11-09T12:05:00Z',
      });
    expect(submitted.status).toBe(200);
    expect(submitted.body.position).toBe(1);

    const response = await request(app.getHttpServer())
      .get(`/api/v2/beatmaps/${beatmap.id}/scores?type=global&mode=kan&limit=50`)
      .set('Authorization', `Bearer ${tokensA.access_token}`);
    expect(response.body.scores).toHaveLength(2);
    expect(response.body.score_count).toBe(2);
    expect(response.body.scores[0].user.username).toBe('BobFlute');
    expect(response.body.scores[1].user.username).toBe('AliceOsu');
    // Alice's own best is second
    expect(response.body.user_score.position).toBe(2);
  });

  it('serves profiles and the profile best-scores section', async () => {
    const profile = await request(app.getHttpServer()).get(`/api/v2/users/${userAId}/?key=id`);
    expect(profile.status).toBe(200);
    expect(profile.body.username).toBe('AliceOsu');
    expect(profile.body.statistics.play_count).toBe(1);

    const byName = await request(app.getHttpServer()).get(
      '/api/v2/users/AliceOsu/kan?key=username',
    );
    expect(byName.status).toBe(200);
    expect(byName.body.id).toBe(userAId);

    const bob = await request(app.getHttpServer()).get(`/api/v2/users/${userBId}/?key=id`);
    expect(bob.status).toBe(200);
    expect(bob.body.username).toBe('BobFlute');

    const unknown = await request(app.getHttpServer()).get('/api/v2/users/nobody-here?key=username');
    expect(unknown.status).toBe(404);
    expect(unknown.body.error).toBeTruthy();

    const best = await request(app.getHttpServer()).get(
      `/api/v2/users/${userAId}/scores/best?mode=kan&offset=0&limit=50`,
    );
    expect(best.status).toBe(200);
    expect(best.body).toHaveLength(1);
    expect(best.body[0].beatmap.id).toBe(imported.beatmaps[0].id);
    expect(best.body[0].beatmapset.id).toBe(imported.setId);
    const keys = Object.keys(best.body[0]);
    expect(keys.indexOf('beatmap')).toBeLessThan(keys.indexOf('beatmapset'));

    const firsts = await request(app.getHttpServer()).get(
      `/api/v2/users/${userAId}/scores/firsts?mode=kan`,
    );
    expect(firsts.status).toBe(200);
    expect(firsts.body).toEqual([]);
  });

  it('updates instead of duplicating when the downloaded archive is re-imported', async () => {
    const storage = createStorage(loadConfig());
    const again = await importOsz({ db: pool, storage }, downloadPath, {
      ownerUsername: 'AliceOsu',
    });
    expect(again.created).toBe(false);
    expect(again.setId).toBe(imported.setId);
    expect(again.beatmaps.map((b) => b.id).sort()).toEqual(
      imported.beatmaps.map((b) => b.id).sort(),
    );
    for (const beatmap of again.beatmaps) {
      const previous = imported.beatmaps.find((b) => b.id === beatmap.id);
      expect(previous?.checksum).toBe(beatmap.checksum);
    }

    const sets = await request(app.getHttpServer()).get('/api/v2/beatmapsets/search?s=any');
    const ids = sets.body.beatmapsets.map((s: { id: number }) => s.id);
    expect(ids.filter((id: number) => id === imported.setId)).toHaveLength(1);
  });

  it('answers batch beatmap lookups like the client sends them', async () => {
    const ids = imported.beatmaps.map((b) => b.id).join('&ids[]=');
    const response = await request(app.getHttpServer()).get(`/api/v2/beatmaps/?ids[]=${ids}`);
    expect(response.status).toBe(200);
    expect(response.body.beatmaps).toHaveLength(2);
  });
});

describe('newer registration rules', () => {
  let app: INestApplication;

  beforeAll(async () => {
    app = await createTestApp();
  });

  afterAll(async () => {
    await app?.close();
  });

  it('refuses a common password in the form_error shape', async () => {
    // The game's multipart registration form with a password straight off the
    // credential-stuffing lists (src/users/password-rules.ts).
    const response = await request(app.getHttpServer())
      .post('/users')
      .field('user[username]', 'MelodyKeys')
      .field('user[user_email]', 'melody@example.org')
      .field('user[password]', 'password123')
      .set('Accept', 'application/json');
    expect(response.status).toBe(422);
    expect(response.body.form_error.user.password[0]).toContain('too common');
    // the password is the only complaint
    expect(response.body.form_error.user.username).toBeUndefined();
    expect(response.body.form_error.user.user_email).toBeUndefined();
  });

  it('puts the website CORS header on POST /users only for the website origin', async () => {
    // The website's sign-up form posts to POST /users from WEBSITE_URL and must
    // be able to read the new account back (src/common/website-cors.ts).
    const fromWebsite = await request(app.getHttpServer())
      .post('/users')
      .field('user[username]', 'WebSigner')
      .field('user[user_email]', 'web@example.org')
      .field('user[password]', 'a line from a song')
      .set('Origin', WEBSITE_URL);
    expect(fromWebsite.status).toBe(201);
    expect(fromWebsite.headers['access-control-allow-origin']).toBe(WEBSITE_URL);

    // every other origin gets no CORS header on the same route
    const fromElsewhere = await request(app.getHttpServer())
      .post('/users')
      .field('user[username]', 'ElseSigner')
      .field('user[user_email]', 'else@example.org')
      .field('user[password]', 'another good password')
      .set('Origin', 'https://evil.example');
    expect(fromElsewhere.status).toBe(201);
    expect(fromElsewhere.headers['access-control-allow-origin']).toBeUndefined();
  });
});
