/**
 * Score tokens, score submission and per-level leaderboards
 * (server/docs/lazer-protocol.md §5).
 */
import { Inject, Injectable } from '@nestjs/common';
import { APP_CONFIG, AppConfig } from '../config';
import { apiError } from '../common/http';
import { DbService } from '../db/db.service';
import { toMultiplayerScore, UserRow } from '../api/serializers';
import { hydrateScores } from '../api/hydrate';
import { grantsLeaderboard } from '../beatmaps/status';
import {
  computeLeaderboard,
  fetchBeatmapScores,
} from './score-queries';

export interface SubmitScoreInput {
  rank?: unknown;
  total_score?: unknown;
  total_score_without_mods?: unknown;
  accuracy?: unknown;
  max_combo?: unknown;
  ruleset_id?: unknown;
  passed?: unknown;
  mods?: unknown;
  statistics?: unknown;
  maximum_statistics?: unknown;
  pauses?: unknown;
  started_at?: unknown;
  ended_at?: unknown;
  build_id?: unknown;
}

function num(value: unknown, fallback = 0): number {
  const parsed = typeof value === 'number' ? value : Number.parseFloat(String(value ?? ''));
  return Number.isFinite(parsed) ? parsed : fallback;
}

function int(value: unknown, fallback = 0): number {
  return Math.trunc(num(value, fallback));
}

function bool(value: unknown, fallback = false): boolean {
  if (typeof value === 'boolean') return value;
  if (value === 'true') return true;
  if (value === 'false') return false;
  return fallback;
}

function dateOrNull(value: unknown): Date | null {
  if (value == null || value === '') return null;
  const date = new Date(String(value));
  return Number.isNaN(date.getTime()) ? null : date;
}

@Injectable()
export class ScoresService {
  constructor(
    private readonly db: DbService,
    @Inject(APP_CONFIG) private readonly config: AppConfig,
  ) {}

  /**
   * `POST /api/v2/beatmaps/{id}/solo/scores`: creates the score token for a
   * play. `beatmap_hash` must be the MD5 of the player's local `.osu`.
   */
  async createToken(
    user: UserRow,
    beatmapId: number,
    input: { beatmap_hash?: string; ruleset_id?: number; version_hash?: string },
  ): Promise<{ id: number }> {
    const beatmap = await this.db.query<{ id: number; checksum: string }>(
      `SELECT id, checksum FROM beatmaps WHERE id = $1`,
      [beatmapId],
    );
    if (beatmap.rows.length === 0) throw apiError(404, 'Beatmap not found');
    if (!input.beatmap_hash || input.beatmap_hash !== beatmap.rows[0].checksum) {
      throw apiError(400, 'invalid or missing beatmap_hash');
    }

    const result = await this.db.query<{ id: number }>(
      `INSERT INTO score_tokens
         (user_id, beatmap_id, beatmap_hash, ruleset_id, ruleset_short_name, version_hash, expires_at)
       VALUES ($1, $2, $3, $4, 'kan', $5, now() + ($6 || ' seconds')::interval)
       RETURNING id::int AS id`,
      [
        user.id,
        beatmapId,
        input.beatmap_hash,
        int(input.ruleset_id, 0),
        input.version_hash ?? '',
        String(this.config.oauth.scoreTokenTtlSeconds),
      ],
    );
    return { id: result.rows[0].id };
  }

  /**
   * `PUT /api/v2/beatmaps/{id}/solo/scores/{token}`: consumes the token and
   * stores the score. Returns the score and its leaderboard position (§5.2).
   */
  async submit(
    user: UserRow,
    beatmapId: number,
    tokenId: number,
    input: SubmitScoreInput,
  ): Promise<Record<string, unknown>> {
    const client = await this.db.pool.connect();
    const rank = String(input.rank ?? 'D').toUpperCase();
    let scoreId: number;
    try {
      await client.query('BEGIN');
      const tokenResult = await client.query<{
        id: number;
        user_id: number;
        beatmap_id: number;
        used_at: Date | null;
        expires_at: Date;
      }>(
        `SELECT id, user_id, beatmap_id, used_at, expires_at
           FROM score_tokens
          WHERE id = $1
          FOR UPDATE`,
        [tokenId],
      );
      const token = tokenResult.rows[0];
      if (!token || token.user_id !== user.id || token.beatmap_id !== beatmapId) {
        throw apiError(400, 'invalid token');
      }
      if (token.used_at != null) throw apiError(400, 'invalid token');
      if (token.expires_at.getTime() <= Date.now()) throw apiError(400, 'expired token');

      const inserted = await client.query<{ id: number }>(
        `INSERT INTO scores
           (user_id, beatmap_id, ruleset_id, ruleset_short_name, score_token_id,
            total_score, total_score_without_mods, accuracy, max_combo, rank, passed,
            mods, statistics, maximum_statistics, pauses, build_id, started_at, ended_at)
         VALUES ($1, $2, $3, 'kan', $4, $5, $6, $7, $8, $9, $10, $11::jsonb, $12::jsonb,
                 $13::jsonb, $14::jsonb, $15, $16, $17)
         RETURNING id::int AS id`,
        [
          user.id,
          beatmapId,
          int(input.ruleset_id, 0),
          tokenId,
          Math.trunc(num(input.total_score)),
          Math.trunc(num(input.total_score_without_mods)),
          num(input.accuracy),
          int(input.max_combo),
          rank,
          bool(input.passed, true),
          JSON.stringify(input.mods ?? []),
          JSON.stringify(input.statistics ?? {}),
          JSON.stringify(input.maximum_statistics ?? {}),
          JSON.stringify(input.pauses ?? []),
          input.build_id == null ? null : int(input.build_id),
          dateOrNull(input.started_at),
          dateOrNull(input.ended_at) ?? new Date(),
        ],
      );
      scoreId = inserted.rows[0].id;

      await client.query(`UPDATE score_tokens SET used_at = now() WHERE id = $1`, [tokenId]);
      await client.query('COMMIT');
    } catch (error) {
      await client.query('ROLLBACK');
      throw error;
    } finally {
      client.release();
    }

    const rows = await fetchBeatmapScores(this.db, beatmapId, 'kan');
    const leaderboard = computeLeaderboard(rows, Number.MAX_SAFE_INTEGER, user.id);
    const competing = leaderboard.entries.find((entry) => Number(entry.score.id) === scoreId);
    // As in osu!, only statuses above pending have a leaderboard to place on
    // (the client only fetches those either way, §5.4).
    const status = await this.statusOf(beatmapId);
    const position =
      status != null && grantsLeaderboard(status)
        ? competing?.position ?? leaderboard.userScore?.position ?? null
        : null;

    const stored = rows.find((row) => Number(row.id) === scoreId);
    if (!stored) throw apiError(500, 'score could not be read back');
    // `MultiplayerScore`: the client reads `id` and `position` (§5.2).
    return toMultiplayerScore(stored, user, position);
  }

  /**
   * `GET /api/v2/beatmaps/{id}/scores`: best score per user, top 50, plus the
   * requesting user's own best (`user_score`).
   *
   * As in osu!, only statuses above pending (ranked/approved/qualified/loved)
   * have a public leaderboard; scores set while a level is pending or WIP stay
   * stored and show up once the level is ranked.
   */
  async leaderboard(
    beatmapId: number,
    mode: string,
    limit: number,
    userId: number | null,
  ): Promise<Record<string, unknown>> {
    const status = await this.statusOf(beatmapId);
    if (status == null || !grantsLeaderboard(status)) {
      return { scores: [], score_count: 0, user_score: null };
    }

    const rows = await fetchBeatmapScores(this.db, beatmapId, mode);
    const board = computeLeaderboard(rows, Math.min(Math.max(limit, 1), 100), userId);

    const topRows = board.entries.map((entry) => entry.score);
    const scores = await hydrateScores(this.db, topRows);

    let userScore: Record<string, unknown> | null = null;
    if (board.userScore) {
      const [hydrated] = await hydrateScores(this.db, [board.userScore.score]);
      userScore = { position: board.userScore.position, score: hydrated };
    }

    return {
      scores,
      score_count: board.scoreCount,
      user_score: userScore,
    };
  }

  /** The status of the beatmapset a beatmap belongs to; null when unknown. */
  private async statusOf(beatmapId: number): Promise<number | null> {
    const result = await this.db.query<{ status: number }>(
      `SELECT s.status
         FROM beatmapsets s
         JOIN beatmaps b ON b.beatmapset_id = s.id
        WHERE b.id = $1`,
      [beatmapId],
    );
    return result.rows[0]?.status ?? null;
  }
}
