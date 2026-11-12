/**
 * Score tokens, score submission and per-level leaderboards
 * (server/docs/lazer-protocol.md §5).
 *
 * A score is the client's own claim (mvp-scope.md §6): what happens here is
 * that only well-formed values are stored (`src/scores/score-rules.ts`) and
 * that the leaderboard ranks in SQL (`src/scores/score-queries.ts`).
 */
import { Inject, Injectable } from '@nestjs/common';
import { APP_CONFIG, AppConfig } from '../config';
import { apiError } from '../common/http';
import { DbService } from '../db/db.service';
import { ScoreRow, toMultiplayerScore, UserRow } from '../api/serializers';
import { hydrateScores } from '../api/hydrate';
import { grantsLeaderboard } from '../beatmaps/status';
import { fetchLeaderboard, fetchUserBest, SCORE_COLUMNS } from './score-queries';
import {
  parseScoreSubmission,
  ScoreRejectedError,
  SubmitScoreInput,
} from './score-rules';

export type { SubmitScoreInput } from './score-rules';

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
        // The column is an integer: clamp what the client sent (audit S8's
        // class of bug, on the token route).
        Math.min(Math.max(Math.trunc(input.ruleset_id ?? 0), 0), 2_147_483_647),
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
    let score;
    try {
      score = parseScoreSubmission(input);
    } catch (error) {
      // The refusals carry their own message for the client (audit S6: out of
      // range values used to reach the database and answer 500).
      if (error instanceof ScoreRejectedError) throw apiError(400, error.message);
      throw error;
    }

    const client = await this.db.pool.connect();
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
          score.rulesetId,
          tokenId,
          score.totalScore,
          score.totalScoreWithoutMods,
          score.accuracy,
          score.maxCombo,
          score.rank,
          score.passed,
          JSON.stringify(score.mods),
          JSON.stringify(score.statistics),
          JSON.stringify(score.maximumStatistics),
          JSON.stringify(score.pauses),
          score.buildId,
          score.startedAt,
          score.endedAt,
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

    const stored = await this.db.query<ScoreRow>(
      `SELECT ${SCORE_COLUMNS} FROM scores WHERE id = $1`,
      [scoreId],
    );
    const row = stored.rows[0];
    if (!row) throw apiError(500, 'score could not be read back');

    // As in osu!, only statuses above pending have a leaderboard to place on
    // (the client only fetches those either way, §5.4). The position is the
    // rank of the user's best score on the level, which is what competes.
    const status = await this.statusOf(beatmapId);
    const best =
      status != null && grantsLeaderboard(status)
        ? await fetchUserBest(this.db, beatmapId, 'kan', user.id)
        : null;

    // `MultiplayerScore`: the client reads `id` and `position` (§5.2).
    return toMultiplayerScore(row, user, best?.position ?? null);
  }

  /**
   * `GET /api/v2/beatmaps/{id}/scores`: best score per user, top `limit`, plus
   * the requesting user's own best (`user_score`).
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

    const board = await fetchLeaderboard(
      this.db,
      beatmapId,
      mode,
      Math.min(Math.max(limit, 1), 100),
      userId,
    );
    const scores = await hydrateScores(this.db, board.entries.map((entry) => entry.score));

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
