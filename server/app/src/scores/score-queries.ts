/**
 * Leaderboard and user-score queries (server/docs/lazer-protocol.md §5.4,
 * §3.4).
 *
 * MVP semantics: the best score per (user, beatmap, ruleset) competes on the
 * leaderboard, ordered by `total_score` desc, then by the server's own clock
 * (`created_at` asc, `id` asc). `ended_at` is client-supplied and therefore
 * never decides ties (audit S6).
 *
 * The ranking runs in SQL (audit S5): `DISTINCT ON (user_id)` picks every
 * user's best score, one page of rows comes back with the total count as a
 * window function, and the requesting user's rank is one extra query. Nothing
 * loads all scores of a level into memory any more.
 */
import { DbService } from '../db/db.service';
import { ScoreRow } from '../api/serializers';

/** Anything that can run a query: `DbService`, a `pg` pool, a test double. */
export type Queryable = Pick<DbService, 'query'>;

export const SCORE_COLUMNS = `id, user_id, beatmap_id, ruleset_id, ruleset_short_name,
       total_score, total_score_without_mods, accuracy, max_combo, rank, passed,
       mods, statistics, maximum_statistics, pauses, build_id, started_at, ended_at`;

/** Shared ordering: more score wins; earlier server receipt breaks ties. */
export const SCORE_ORDER = 'total_score DESC, created_at ASC, id ASC';

/** One competing score and its position on the level's leaderboard. */
export interface LeaderboardEntry {
  position: number;
  score: ScoreRow;
}

export interface Leaderboard {
  /** Top `limit` entries, already positioned 1..n. */
  entries: LeaderboardEntry[];
  /** Total number of competing (best-per-user) scores. */
  scoreCount: number;
  /** The requesting user's own best and its position, if they have one. */
  userScore: LeaderboardEntry | null;
}

export type UserScoreType = 'best' | 'firsts' | 'recent' | 'pinned';

/**
 * `GET /api/v2/users/{id}/scores/{type}` SQL. `firsts`/`pinned` are out of
 * MVP scope and return empty lists from the caller.
 */
export function buildUserScoresQuery(
  type: UserScoreType,
): { text: string; orderBy: string } | null {
  switch (type) {
    case 'best':
      return {
        text: `SELECT ${SCORE_COLUMNS}
                 FROM scores
                WHERE user_id = $1 AND ruleset_short_name = $2`,
        orderBy: SCORE_ORDER,
      };
    case 'recent':
      return {
        text: `SELECT ${SCORE_COLUMNS}
                 FROM scores
                WHERE user_id = $1 AND ruleset_short_name = $2`,
        orderBy: 'created_at DESC, id DESC',
      };
    default:
      return null;
  }
}

export async function fetchUserScores(
  db: Queryable,
  userId: number,
  type: UserScoreType,
  rulesetShortName: string,
  limit: number,
  offset: number,
): Promise<ScoreRow[]> {
  const query = buildUserScoresQuery(type);
  if (!query) return [];
  const result = await db.query<ScoreRow>(
    `${query.text} ORDER BY ${query.orderBy} LIMIT $3 OFFSET $4`,
    [userId, rulesetShortName, limit, offset],
  );
  return result.rows;
}

/** Every user's best score on a beatmap: the competing scores. */
const BEST_PER_USER = `
  SELECT DISTINCT ON (user_id) ${SCORE_COLUMNS}, created_at
    FROM scores
   WHERE beatmap_id = $1 AND ruleset_short_name = $2
   ORDER BY user_id, ${SCORE_ORDER}`;

/** True where `b` ranks before `m` (same order as {@link SCORE_ORDER}). */
const BEATS = `b.total_score > m.total_score
    OR (b.total_score = m.total_score
        AND (b.created_at < m.created_at
             OR (b.created_at = m.created_at AND b.id < m.id)))`;

/**
 * One user's best competing score and its position (1 = top), or null when
 * the user has no score on the level. Position is the rank of the user's best
 * score, whatever score of theirs the client just submitted.
 */
export async function fetchUserBest(
  db: Queryable,
  beatmapId: number,
  rulesetShortName: string,
  userId: number,
): Promise<LeaderboardEntry | null> {
  const result = await db.query<ScoreRow & { position: string | number }>(
    `WITH best AS (${BEST_PER_USER})
     SELECT m.*, 1 + (SELECT count(*) FROM best b WHERE ${BEATS}) AS position
       FROM best m
      WHERE m.user_id = $3`,
    [beatmapId, rulesetShortName, userId],
  );
  const row = result.rows[0];
  if (!row) return null;
  return { position: Number(row.position), score: row };
}

/**
 * The page of a level's leaderboard (`entries` positioned 1..n), the number of
 * competing scores (`scoreCount`) and the requesting user's own best.
 */
export async function fetchLeaderboard(
  db: Queryable,
  beatmapId: number,
  rulesetShortName: string,
  limit: number,
  userId: number | null,
): Promise<Leaderboard> {
  const result = await db.query<ScoreRow & { score_count: string | number }>(
    `WITH best AS (${BEST_PER_USER})
     SELECT m.*, count(*) OVER () AS score_count
       FROM best m
      ORDER BY m.total_score DESC, m.created_at ASC, m.id ASC
      LIMIT $3`,
    [beatmapId, rulesetShortName, Math.max(1, limit)],
  );

  const rows = result.rows;
  return {
    entries: rows.map((row, index) => ({ position: index + 1, score: row })),
    scoreCount: rows.length > 0 ? Number(rows[0].score_count) : 0,
    userScore: userId == null ? null : await fetchUserBest(db, beatmapId, rulesetShortName, userId),
  };
}
