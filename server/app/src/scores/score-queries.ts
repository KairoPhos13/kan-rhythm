/**
 * Leaderboard and user-score queries (server/docs/lazer-protocol.md §5.4,
 * §3.4).
 *
 * MVP semantics: the best score per (user, beatmap, ruleset) competes on the
 * leaderboard, ordered by `total_score` desc then date asc. The ranking itself
 * is computed in {@link computeLeaderboard} so it can be unit-tested; the SQL
 * stays a plain fetch of the beatmap's scores.
 */
import { DbService } from '../db/db.service';
import { ScoreRow } from '../api/serializers';

export const SCORE_COLUMNS = `id, user_id, beatmap_id, ruleset_id, ruleset_short_name,
       total_score, total_score_without_mods, accuracy, max_combo, rank, passed,
       mods, statistics, maximum_statistics, pauses, build_id, started_at, ended_at`;

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
        orderBy: 'total_score DESC, ended_at ASC',
      };
    case 'recent':
      return {
        text: `SELECT ${SCORE_COLUMNS}
                 FROM scores
                WHERE user_id = $1 AND ruleset_short_name = $2`,
        orderBy: 'ended_at DESC',
      };
    default:
      return null;
  }
}

/** All scores on a beatmap, oldest first (stable input for the ranking). */
export function buildBeatmapScoresQuery(): string {
  return `SELECT ${SCORE_COLUMNS}
            FROM scores
           WHERE beatmap_id = $1 AND ruleset_short_name = $2
           ORDER BY ended_at ASC, id ASC`;
}

export async function fetchBeatmapScores(
  db: DbService,
  beatmapId: number,
  rulesetShortName: string,
): Promise<ScoreRow[]> {
  const result = await db.query<ScoreRow>(buildBeatmapScoresQuery(), [beatmapId, rulesetShortName]);
  return result.rows;
}

export async function fetchUserScores(
  db: DbService,
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

/**
 * Best score per user, ranked by `total_score` desc then date asc (the client
 * re-sorts by total score itself and assigns list positions — §5.4 — but the
 * `user_score` position comes from here).
 */
export function computeLeaderboard(
  rows: ScoreRow[],
  limit: number,
  userId: number | null,
): Leaderboard {
  const bestByUser = new Map<number, ScoreRow>();
  for (const row of rows) {
    const current = bestByUser.get(row.user_id);
    if (!current || isBetter(row, current)) bestByUser.set(row.user_id, row);
  }

  const ranked = [...bestByUser.values()].sort(compareScores);
  const entries: LeaderboardEntry[] = ranked.map((score, index) => ({
    position: index + 1,
    score,
  }));

  const userScore = userId == null ? null : entries.find((e) => e.score.user_id === userId) ?? null;
  return {
    entries: entries.slice(0, Math.max(0, limit)),
    scoreCount: entries.length,
    userScore,
  };
}

/** Ordering shared with the SQL semantics: more score wins, earlier date wins ties. */
export function compareScores(a: ScoreRow, b: ScoreRow): number {
  const scoreDiff = Number(b.total_score) - Number(a.total_score);
  if (scoreDiff !== 0) return scoreDiff;
  const dateDiff = new Date(a.ended_at).getTime() - new Date(b.ended_at).getTime();
  if (dateDiff !== 0) return dateDiff;
  return Number(a.id) - Number(b.id);
}

function isBetter(candidate: ScoreRow, current: ScoreRow): boolean {
  return compareScores(candidate, current) < 0;
}
