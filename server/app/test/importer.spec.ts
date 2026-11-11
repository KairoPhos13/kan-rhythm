/**
 * Importer tests (review fixes): difficulty identity is
 * (beatmapset_id, version, mode) since archives can carry two difficulties with
 * the same `Version` in different modes, and only mode-0 difficulties are
 * playable in kan! (it claims legacy ruleset id 0), so the others are skipped
 * and stripped from the stored archive; an archive without any playable
 * difficulty is rejected with nothing written and CLI exit code 1.
 */
import { spawn } from 'node:child_process';
import { mkdtempSync, readdirSync, rmSync, writeFileSync, statSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import AdmZip from 'adm-zip';
import { Pool } from 'pg';
import { importOsz, importPackage, resolveBeatmapId } from '../src/beatmaps/importer';
import { md5Of } from '../src/beatmaps/osu-file';
import { loadConfig } from '../src/config';
import { createPool } from '../src/db/db.service';
import { runMigrations } from '../src/db/migrate';
import { createStorage } from '../src/storage/create-storage';
import { minimalOsu, silentWav, tinyPng } from './fixture';

const TEST_DATABASE_URL =
  process.env.IMPORT_TEST_DATABASE_URL ?? 'postgres://kan:kan@localhost:55432/kan_import_test';

jest.setTimeout(120_000);

function listFiles(dir: string, prefix = ''): string[] {
  const out: string[] = [];
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    const rel = path.join(prefix, entry.name);
    if (entry.isDirectory()) out.push(...listFiles(path.join(dir, entry.name), rel));
    else out.push(rel);
  }
  return out.sort();
}

describe('level import', () => {
  let pool: Pool;
  let storageDir: string;
  let archiveCounter = 0;

  /** Writes a zip with exactly these .osu files plus audio and a background. */
  function writeOsz(osuFiles: { name: string; text: string }[]): string {
    const zip = new AdmZip();
    for (const file of osuFiles) {
      zip.addFile(file.name, Buffer.from(file.text, 'utf8'));
    }
    zip.addFile('audio.wav', silentWav());
    zip.addFile('bg.png', tinyPng());
    const filePath = path.join(storageDir, `archive-${(archiveCounter += 1)}.osz`);
    writeFileSync(filePath, zip.toBuffer());
    return filePath;
  }

  beforeAll(async () => {
    storageDir = mkdtempSync(path.join(tmpdir(), 'kan-import-test-'));

    const adminUrl = new URL(TEST_DATABASE_URL);
    const dbName = adminUrl.pathname.replace(/^\//, '');
    adminUrl.pathname = '/postgres';
    const admin = createPool(adminUrl.toString());
    await admin.query(`DROP DATABASE IF EXISTS ${dbName} WITH (FORCE)`);
    await admin.query(`CREATE DATABASE ${dbName}`);
    await admin.end();

    pool = createPool(TEST_DATABASE_URL);
    await runMigrations(pool, path.join(__dirname, '..', 'migrations'));
    await pool.query(
      `INSERT INTO users (username, email, password_hash) VALUES ('Importer', 'importer@example.org', 'x')`,
    );

    process.env.DATABASE_URL = TEST_DATABASE_URL;
    process.env.STORAGE_DRIVER = 'local';
    process.env.STORAGE_LOCAL_DIR = storageDir;
  });

  afterAll(async () => {
    await pool?.end();
    if (storageDir) rmSync(storageDir, { recursive: true, force: true });
  });

  it('gives distinct ids to two same-name difficulties in different modes', async () => {
    const client = await pool.connect();
    try {
      const set = await pool.query<{ id: number }>(
        `INSERT INTO beatmapsets (archive_key) VALUES ('') RETURNING id`,
      );
      const setId = set.rows[0].id;

      // "Normal" in mode 0 and "Normal" in mode 3 (as in "241526 Soleily -
      // Renatus.osz"): two difficulties, two identities, no overwrite.
      const mode0Id = await resolveBeatmapId(client, setId, 'Normal', 0);
      await pool.query(
        `INSERT INTO beatmaps (id, beatmapset_id, version, filename, checksum, mode_int)
         VALUES ($1, $2, 'Normal', 'normal.osu', $3, 0)`,
        [mode0Id, setId, 'a'.repeat(32)],
      );
      const mode3Id = await resolveBeatmapId(client, setId, 'Normal', 3);
      expect(mode3Id).not.toBe(mode0Id);
      await pool.query(
        `INSERT INTO beatmaps (id, beatmapset_id, version, filename, checksum, mode_int)
         VALUES ($1, $2, 'Normal', 'normal-mania.osu', $3, 3)`,
        [mode3Id, setId, 'b'.repeat(32)],
      );

      // stable across calls
      expect(await resolveBeatmapId(client, setId, 'Normal', 0)).toBe(mode0Id);
      expect(await resolveBeatmapId(client, setId, 'Normal', 3)).toBe(mode3Id);

      // the identity is pinned down at the database level as well
      const index = await pool.query(
        `SELECT indexname FROM pg_indexes
          WHERE tablename = 'beatmaps' AND indexname = 'beatmaps_set_version_mode_key'`,
      );
      expect(index.rowCount).toBe(1);
      await expect(
        pool.query(
          `INSERT INTO beatmaps (id, beatmapset_id, version, filename, checksum, mode_int)
           VALUES ($1, $2, 'Normal', 'other.osu', $3, 0)`,
          [mode0Id + 1000, setId, 'c'.repeat(32)],
        ),
      ).rejects.toThrow(/duplicate key/);
    } finally {
      client.release();
    }
  });

  it('skips difficulties that are not playable in kan! and strips them from the archive', async () => {
    const filePath = writeOsz([
      {
        name: 'Fixture Artist - Fixture Song (KairoPhos) [Normal].osu',
        text: minimalOsu({ version: 'Normal', mode: 0 }),
      },
      {
        name: 'Fixture Artist - Fixture Song (KairoPhos) [Mania].osu',
        text: minimalOsu({ version: 'Normal', mode: 3 }),
      },
    ]);

    const messages: string[] = [];
    const storage = createStorage(loadConfig());
    const result = await importOsz(
      { db: pool, storage, log: (message) => messages.push(message) },
      filePath,
      { ownerUsername: 'Importer' },
    );

    expect(messages).toContain('skipping Normal: mode 3 is not playable in kan!');
    expect(result.beatmaps).toHaveLength(1);

    const rows = await pool.query<{ id: number; mode_int: number }>(
      `SELECT id, mode_int FROM beatmaps WHERE beatmapset_id = $1`,
      [result.setId],
    );
    expect(rows.rows).toHaveLength(1);
    expect(rows.rows[0].mode_int).toBe(0);

    // the stored archive keeps only the playable difficulty
    const archivePath = path.join(storageDir, 'beatmapsets', `${result.setId}.osz`);
    expect(statSync(archivePath).size).toBeGreaterThan(0);
    const stored = new AdmZip(archivePath);
    const osuEntries = stored
      .getEntries()
      .filter((entry) => !entry.isDirectory && entry.entryName.endsWith('.osu'));
    expect(osuEntries).toHaveLength(1);
    expect(osuEntries[0].getData().toString('utf8')).toContain('Mode: 0');
    expect(stored.getEntries().map((e) => e.entryName)).toContain('audio.wav');
  });

  it('rejects an archive with no kan!-playable difficulty and writes nothing', async () => {
    const filePath = writeOsz([
      {
        name: 'Fixture Artist - Fixture Song (KairoPhos) [Mania].osu',
        text: minimalOsu({ version: 'Mania', mode: 3 }),
      },
    ]);

    const filesBefore = listFiles(storageDir);
    const setsBefore = await pool.query<{ count: string }>('SELECT count(*) FROM beatmapsets');
    const storage = createStorage(loadConfig());

    await expect(
      importOsz({ db: pool, storage }, filePath, { ownerUsername: 'Importer' }),
    ).rejects.toThrow('no mode-0 (kan!-playable) difficulties in archive');

    expect(listFiles(storageDir)).toEqual(filesBefore);
    const setsAfter = await pool.query<{ count: string }>('SELECT count(*) FROM beatmapsets');
    expect(setsAfter.rows[0].count).toBe(setsBefore.rows[0].count);
  });

  it('exits with code 1 from the import CLI when nothing is playable', async () => {
    const filePath = writeOsz([
      {
        name: 'Fixture Artist - Fixture Song (KairoPhos) [Taiko].osu',
        text: minimalOsu({ version: 'Taiko', mode: 1 }),
      },
    ]);

    const filesBefore = listFiles(storageDir);
    const exitCode = await new Promise<{ code: number | null; stderr: string }>((resolve) => {
      const child = spawn(
        process.execPath,
        ['-r', 'ts-node/register', 'src/import/cli.ts', filePath],
        {
          cwd: path.join(__dirname, '..'),
          env: {
            ...process.env,
            TS_NODE_TRANSPILE_ONLY: '1',
            DATABASE_URL: TEST_DATABASE_URL,
            STORAGE_DRIVER: 'local',
            STORAGE_LOCAL_DIR: storageDir,
          },
        },
      );
      let stderr = '';
      child.stderr.on('data', (chunk: Buffer) => (stderr += chunk.toString()));
      child.on('close', (code) => resolve({ code, stderr }));
    });

    expect(exitCode.code).toBe(1);
    expect(exitCode.stderr).toContain('import: failed:');
    expect(exitCode.stderr).toContain('no mode-0 (kan!-playable) difficulties in archive');
    expect(listFiles(storageDir)).toEqual(filesBefore);
  });

  it('keeps a package byte for byte when its ids are final (the submission path)', async () => {
    // The submission flow hands the ids out before the package exists
    // (PUT /beatmap-submission/beatmapsets), so the files arrive with their
    // final ids and must be stored unchanged: the creator's local copies and
    // the stored ones must keep the same checksums.
    const storage = createStorage(loadConfig());
    const set = await pool.query<{ id: number }>(
      `INSERT INTO beatmapsets (user_id, archive_key) VALUES ((SELECT id FROM users LIMIT 1), '') RETURNING id`,
    );
    const setId = set.rows[0].id;
    const allocated = await pool.query<{ id: number }>(
      `SELECT nextval(pg_get_serial_sequence('beatmaps', 'id'))::int AS id`,
    );
    const beatmapId = allocated.rows[0].id;

    const text = minimalOsu({ version: 'Normal' })
      .replace('BeatmapID:0', `BeatmapID: ${beatmapId}`)
      .replace('BeatmapSetID:0', `BeatmapSetID: ${setId}`);
    const files = [
      { entryName: 'Fixture Artist - Fixture Song (KairoPhos) [Normal].osu', data: Buffer.from(text, 'utf8') },
      { entryName: 'audio.wav', data: silentWav() },
    ];

    const result = await importPackage({ db: pool, storage }, files, 'package', {
      ownerUsername: 'Importer',
      setId,
      status: 0,
      creator: 'Importer',
      beatmapIds: 'package',
      allowedBeatmapIds: new Set([beatmapId]),
    });
    expect(result.beatmaps).toEqual([
      { id: beatmapId, version: 'Normal', checksum: md5Of(files[0].data) },
    ]);

    const stored = new AdmZip(path.join(storageDir, 'beatmapsets', `${setId}.osz`));
    const storedOsu = stored
      .getEntries()
      .find((entry) => entry.entryName.endsWith('.osu'));
    expect(storedOsu?.getData().equals(files[0].data)).toBe(true);

    // a package carrying an id that is not the set's is refused, nothing written
    const foreign = minimalOsu({ version: 'Hard', beatmapId: beatmapId + 1000, beatmapSetId: setId });
    await expect(
      importPackage(
        { db: pool, storage },
        [{ entryName: 'hard.osu', data: Buffer.from(foreign, 'utf8') }],
        'package',
        { ownerUsername: 'Importer', setId, beatmapIds: 'package', allowedBeatmapIds: new Set([beatmapId]) },
      ),
    ).rejects.toThrow('does not belong to beatmapset');
    const rows = await pool.query<{ id: number }>(
      `SELECT id FROM beatmaps WHERE beatmapset_id = $1`,
      [setId],
    );
    expect(rows.rows.map((row) => row.id)).toEqual([beatmapId]);
  });
});
