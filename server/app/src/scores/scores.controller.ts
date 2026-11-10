/**
 * Score routes (server/docs/lazer-protocol.md §5): token creation, submission
 * and the per-beatmap leaderboard.
 */
import { Body, Controller, Get, Param, Post, Put, Query, Req, UseGuards } from '@nestjs/common';
import { Request } from 'express';
import { apiError } from '../common/http';
import { queryInt, QueryParams } from '../common/query.util';
import { AuthenticatedUser, AuthService } from '../auth/auth.service';
import { AuthGuard, CurrentUser, optionalUser } from '../auth/auth.guard';
import { ScoresService, SubmitScoreInput } from './scores.service';

@Controller('api/v2')
export class ScoresController {
  constructor(
    private readonly scores: ScoresService,
    private readonly auth: AuthService,
  ) {}

  /** `POST /api/v2/beatmaps/{id}/solo/scores`: create a score token. */
  @Post('beatmaps/:id/solo/scores')
  @UseGuards(AuthGuard)
  async createToken(
    @Param('id') id: string,
    @CurrentUser() user: AuthenticatedUser,
    @Body() body: Record<string, unknown>,
  ): Promise<{ id: number }> {
    const beatmapId = this.beatmapId(id);
    return this.scores.createToken(user, beatmapId, {
      beatmap_hash: typeof body['beatmap_hash'] === 'string' ? body['beatmap_hash'] : undefined,
      ruleset_id: Number.parseInt(String(body['ruleset_id'] ?? '0'), 10) || 0,
      version_hash: typeof body['version_hash'] === 'string' ? body['version_hash'] : undefined,
    });
  }

  /** `PUT /api/v2/beatmaps/{id}/solo/scores/{token}`: submit the score. */
  @Put('beatmaps/:id/solo/scores/:token')
  @UseGuards(AuthGuard)
  async submit(
    @Param('id') id: string,
    @Param('token') token: string,
    @CurrentUser() user: AuthenticatedUser,
    @Body() body: SubmitScoreInput,
  ): Promise<Record<string, unknown>> {
    const beatmapId = this.beatmapId(id);
    const tokenId = Number.parseInt(token, 10);
    if (!Number.isFinite(tokenId)) throw apiError(400, 'invalid token');
    return this.scores.submit(user, beatmapId, tokenId, body ?? {});
  }

  /** `GET /api/v2/beatmaps/{id}/scores`: leaderboard (`mode=kan`). */
  @Get('beatmaps/:id/scores')
  async leaderboard(
    @Param('id') id: string,
    @Query() query: QueryParams,
    @Req() request: Request,
  ): Promise<Record<string, unknown>> {
    const beatmapId = this.beatmapId(id);
    const user = await optionalUser(this.auth, request);
    // `type=country|friend|team` falls back to global for the MVP (§5.4).
    return this.scores.leaderboard(
      beatmapId,
      query['mode'] ? String(query['mode']) : 'kan',
      queryInt(query, 'limit', 50, 1, 100),
      user?.id ?? null,
    );
  }

  private beatmapId(raw: string): number {
    const beatmapId = Number.parseInt(raw, 10);
    if (!Number.isFinite(beatmapId)) throw apiError(404, 'Beatmap not found');
    return beatmapId;
  }
}
