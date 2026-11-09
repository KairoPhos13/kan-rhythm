/**
 * Periodic cleanup of expired rows: `oauth_tokens` and `score_tokens` would
 * otherwise grow forever (review note). Runs once at start-up and then every
 * hour on a plain `setInterval` — the timer is `unref()`ed so it never keeps
 * the process alive, and is cleared on module destroy.
 */
import { Injectable, Logger, OnModuleDestroy, OnModuleInit } from '@nestjs/common';
import { DbService } from './db.service';

/** Cleanup cadence: hourly. */
export const PURGE_INTERVAL_MS = 60 * 60 * 1000;

/**
 * Purge predicates:
 *  - `oauth_tokens`: a row is dead once its refresh token has expired (access
 *    tokens live far shorter and the client refreshes proactively);
 *  - `score_tokens`: rows that were used (a play was submitted) or that expired
 *    more than a day ago (the play was abandoned) — a day of slack keeps a
 *    token around for late submissions within its own lifetime.
 */
export const PURGE_STATEMENTS = {
  oauthTokens: `DELETE FROM oauth_tokens WHERE refresh_expires_at < now()`,
  scoreTokens: `DELETE FROM score_tokens
                WHERE used_at IS NOT NULL
                   OR expires_at < now() - interval '1 day'`,
};

export interface PurgeResult {
  oauthTokens: number;
  scoreTokens: number;
}

@Injectable()
export class PurgeService implements OnModuleInit, OnModuleDestroy {
  private readonly logger = new Logger('purge');
  private timer: ReturnType<typeof setInterval> | null = null;

  constructor(private readonly db: DbService) {}

  onModuleInit(): void {
    void this.purge();
    this.timer = setInterval(() => void this.purge(), PURGE_INTERVAL_MS);
    // Never hold the process open for housekeeping.
    this.timer.unref();
  }

  onModuleDestroy(): void {
    if (this.timer) clearInterval(this.timer);
    this.timer = null;
  }

  /** Deletes expired rows; a failure is logged and never breaks the process. */
  async purge(): Promise<PurgeResult> {
    return {
      oauthTokens: await this.run('oauth_tokens', PURGE_STATEMENTS.oauthTokens),
      scoreTokens: await this.run('score_tokens', PURGE_STATEMENTS.scoreTokens),
    };
  }

  private async run(name: string, statement: string): Promise<number> {
    try {
      const result = await this.db.query(statement);
      return result.rowCount ?? 0;
    } catch (error) {
      this.logger.error(
        `purge of ${name} failed: ${error instanceof Error ? error.message : String(error)}`,
      );
      return 0;
    }
  }
}
