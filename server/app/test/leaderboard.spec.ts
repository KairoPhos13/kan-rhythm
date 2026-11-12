/**
 * Leaderboard ranking against real SQL (audit S5, S6): the best score per user
 * competes, `total_score` decides, ties break on the server's own clock (never
 * on the client's `ended_at`), one page comes back with the total count, and
 * the requesting user's rank comes back whatever page was requested.
 */
import path from 'node:path';
import { Pool } from 'pg';
import { createPool } from '../src/db/db.service';
import { runMigrations } from '../src/db/migrate';
import { fetchLeaderboard, fetchUserBest } from '../src/scores/score-queries';

const TEST_DATABASE_URL =
  process.env.TEST_DATABASE_URL ?? 'postgres://kan:kan@localhost:55432/kan_test';
/** Its own database, on the server TEST_DATABASE_URL points at. */
const LEADERBOARD_DATABASE_URL = (() => {
  const url = new URL(TEST_DATABASE_URL);
  url.pathname = '/kan_leaderboard_test';
  return url.toString();
})();

jest.setTimeout(120_000);

describe('leaderboard ranking', () => {
  let pool: Pool;
  let beatmapId: number;

  /** Inserts one score and returns its id; `created` is the server clock. */
  async function addScore(
    userId: number,
    totalScore: number,
    created: string,
    ended = '2026-11-12T10:00:00Z',
  ): Promise<number> {
    const inserted = await pool.query<{ id: number }>(
      `INSERT INTO scores (user_id, beatmap_id, total_score, total_score_without_mods,
                           accuracy, max_combo, rank, passed, ended_at, created_at)
       VALUES ($1, $2, $3, $3, 1, 10, 'S', true, $4, $5)
       RETURNING id::int AS id`,
      [userId, beatmapId, totalScore, ended, created],
    );
    return inserted.rows[0].id;
  }

  beforeAll(async () => {
    const adminUrl = new URL(LEADERBOARD_DATABASE_URL);
    const dbName = adminUrl.pathname.replace(/^\//, '');
    adminUrl.pathname = '/postgres';
    const admin = createPool(adminUrl.toString());
    await admin.query(`DROP DATABASE IF EXISTS ${dbName} WITH (FORCE)`);
    await admin.query(`CREATE DATABASE ${dbName}`);
    await admin.end();

    pool = createPool(LEADERBOARD_DATABASE_URL);
    await runMigrations(pool, path.join(__dirname, '..', 'migrations'));

    // Real accounts start at id 2 (migration 0005).
    for (let id = 2; id <= 5; id += 1) {
      await pool.query(
        `INSERT INTO users (id, username, email, password_hash) VALUES ($1, $2, $3, 'x')`,
        [id, `Board${id}`, `board${id}@example.org`],
      );
    }
    const set = await pool.query<{ id: number }>(
      `INSERT INTO beatmapsets (user_id, archive_key) VALUES (2, '') RETURNING id`,
    );
    const beatmap = await pool.query<{ id: number }>(
      `INSERT INTO beatmaps (beatmapset_id, checksum) VALUES ($1, 'sum') RETURNING id`,
      [set.rows[0].id],
    );
    beatmapId = beatmap.rows[0].id;
  });

  afterAll(async () => {
    await pool?.end();
  });

  it('keeps only the best score per user', async () => {
    await addScore(2, 500, '2026-11-12T08:00:00Z');
    await addScore(2, 900, '2026-11-12T08:10:00Z');
    await addScore(3, 700, '2026-11-12T08:20:00Z');

    const board = await fetchLeaderboard(pool, beatmapId, 'kan', 50, null);
    expect(board.entries.map((entry) => Number(entry.score.total_score))).toEqual([900, 700]);
    expect(board.entries.map((entry) => entry.position)).toEqual([1, 2]);
    expect(board.scoreCount).toBe(2);
    expect(board.userScore).toBeNull();
  });

  it('breaks total score ties on the server clock, not the claimed ended_at', async () => {
    await addScore(4, 900, '2026-11-12T09:00:00Z', '2026-11-12T23:00:00Z');
    await addScore(5, 900, '2026-11-12T09:30:00Z', '2026-11-12T01:00:00Z');

    // Four competing scores: 900 (user 2, submitted 08:10), 900 (user 4,
    // 09:00), 900 (user 5, 09:30), 700 (user 3). The claimed `ended_at`
    // values (23:00 and 01:00) decide nothing (audit S6).
    const board = await fetchLeaderboard(pool, beatmapId, 'kan', 50, null);
    expect(board.entries.map((entry) => entry.score.user_id)).toEqual([2, 4, 5, 3]);
  });

  it('reports the requesting user position even beyond the page limit', async () => {
    const board = await fetchLeaderboard(pool, beatmapId, 'kan', 2, 3);
    expect(board.entries).toHaveLength(2);
    expect(board.scoreCount).toBe(4);
    expect(board.userScore?.position).toBe(4);
    expect(Number(board.userScore?.score.total_score)).toBe(700);

    expect((await fetchUserBest(pool, beatmapId, 'kan', 2))?.position).toBe(1);
  });

  it('has no user score for users without scores on the level', async () => {
    expect(await fetchUserBest(pool, beatmapId, 'kan', 42)).toBeNull();
    const board = await fetchLeaderboard(pool, beatmapId, 'kan', 50, 42);
    expect(board.userScore).toBeNull();
  });
});
