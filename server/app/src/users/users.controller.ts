/**
 * User-facing API routes (server/docs/lazer-protocol.md §3).
 *
 * Route order matters: `me/beatmapset-favourites` must be declared before
 * `me/:mode` so the parametric route cannot swallow it.
 */
import { Controller, Get, Param, Query, UseGuards } from '@nestjs/common';
import { apiError } from '../common/http';
import { queryInt } from '../common/query.util';
import { toApiUser, UserRow } from '../api/serializers';
import { hydrateScores } from '../api/hydrate';
import { AuthenticatedUser, AuthService } from '../auth/auth.service';
import { AuthGuard, CurrentUser } from '../auth/auth.guard';
import { DbService } from '../db/db.service';
import { fetchUserScores, UserScoreType } from '../scores/score-queries';
import { UsersService } from './users.service';

@Controller('api/v2')
export class UsersController {
  constructor(
    private readonly users: UsersService,
    private readonly auth: AuthService,
    private readonly db: DbService,
  ) {}

  /** `GET /api/v2/me`, `GET /api/v2/me/`, `GET /api/v2/me/{mode}`. */
  @Get('me')
  @UseGuards(AuthGuard)
  async me(@CurrentUser() user: AuthenticatedUser): Promise<Record<string, unknown>> {
    return toApiUser(user, await this.users.statisticsFor(user.id));
  }

  /** Must precede `me/:mode`, which would otherwise swallow this path. */
  @Get('me/beatmapset-favourites')
  @UseGuards(AuthGuard)
  favourites(): Record<string, unknown> {
    return { beatmapset_ids: [] };
  }

  @Get('me/:mode')
  @UseGuards(AuthGuard)
  async meForMode(@CurrentUser() user: AuthenticatedUser): Promise<Record<string, unknown>> {
    // Per-mode statistics are not separated in the MVP: same payload.
    return toApiUser(user, await this.users.statisticsFor(user.id));
  }

  /** No friends in the MVP: empty list, but the route exists to avoid retry noise. */
  @Get('friends')
  @UseGuards(AuthGuard)
  friends(): unknown[] {
    return [];
  }

  @Get('blocks')
  @UseGuards(AuthGuard)
  blocks(): unknown[] {
    return [];
  }

  /** `GET /api/v2/users/{lookup}/{mode}?key=id|username` (and without a mode). */
  @Get('users/:lookup')
  async user(@Param('lookup') lookup: string, @Query('key') key?: string): Promise<Record<string, unknown>> {
    return this.lookupUser(lookup, key);
  }

  @Get('users/:lookup/:mode')
  async userForMode(
    @Param('lookup') lookup: string,
    @Param('mode') _mode: string,
    @Query('key') key?: string,
  ): Promise<Record<string, unknown>> {
    return this.lookupUser(lookup, key);
  }

  /** `GET /api/v2/users/{id}/scores/{type}`: profile best/recent sections. */
  @Get('users/:lookup/scores/:type')
  async userScores(
    @Param('lookup') lookup: string,
    @Param('type') type: string,
    @Query('mode') mode?: string,
    @Query('offset') offset?: string,
    @Query('limit') limit?: string,
  ): Promise<unknown[]> {
    const userId = Number.parseInt(lookup, 10);
    if (!Number.isFinite(userId)) throw apiError(404, 'User not found');
    const scoreType = type as UserScoreType;
    if (!['best', 'firsts', 'recent', 'pinned'].includes(scoreType)) {
      throw apiError(404, 'Unknown score type');
    }
    const rows = await fetchUserScores(
      this.db,
      userId,
      scoreType,
      mode || 'kan',
      queryInt({ limit }, 'limit', 50, 1, 100),
      queryInt({ offset }, 'offset', 0, 0, 1_000_000),
    );
    return hydrateScores(this.db, rows);
  }

  private async lookupUser(lookup: string, key: string | undefined): Promise<Record<string, unknown>> {
    const byId = key === 'id' || (key == null && /^\d+$/.test(lookup));
    const user: UserRow | null = byId
      ? await this.users.findById(Number.parseInt(lookup, 10))
      : await this.users.findByUsername(lookup);
    if (!user) throw apiError(404, 'User not found');
    return toApiUser(user, await this.users.statisticsFor(user.id), { isOnline: false });
  }
}
