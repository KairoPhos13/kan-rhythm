/**
 * Beatmap catalogue routes (server/docs/lazer-protocol.md §4).
 *
 * Route order matters: `beatmapsets/lookup` must precede `beatmapsets/:id`.
 */
import {
  Controller,
  Get,
  Inject,
  Param,
  Query,
  Req,
  Res,
  UseGuards,
} from '@nestjs/common';
import { Request, Response } from 'express';
import { APP_CONFIG, AppConfig } from '../config';
import { apiError } from '../common/http';
import { extractCursor, queryValue, QueryParams } from '../common/query.util';
import { requestClientIp } from '../common/client-ip';
import { RouteThrottle } from '../common/throttle';
import { AuthenticatedUser, AuthService } from '../auth/auth.service';
import { AuthGuard, CurrentUser, optionalUser } from '../auth/auth.guard';
import { STORAGE, Storage } from '../storage/storage';
import { BeatmapsService } from './beatmaps.service';

@Controller('api/v2')
export class BeatmapsController {
  /** Browsing is cheap but not free; downloads are the expensive ones (S7). */
  private readonly searchThrottle = new RouteThrottle(120, 120);
  private readonly downloadThrottle = new RouteThrottle(20, 30);

  constructor(
    private readonly beatmaps: BeatmapsService,
    private readonly auth: AuthService,
    @Inject(STORAGE) private readonly storage: Storage,
    @Inject(APP_CONFIG) private readonly config: AppConfig,
  ) {}

  /** `GET /api/v2/beatmapsets/search`: cursor paging via `cursor[key]`. */
  @Get('beatmapsets/search')
  async search(
    @Query() query: QueryParams,
    @Req() request: Request,
  ): Promise<{ beatmapsets: Record<string, unknown>[]; cursor?: Record<string, unknown> | null }> {
    const user = await optionalUser(this.auth, request);
    this.searchThrottle.check(user?.id ?? null, requestClientIp(request, this.config.trustProxy));
    const cursor = extractCursor(query);
    const offset = Number.parseInt(cursor['_id'] ?? '0', 10);

    const mode = Number.parseInt(queryValue(query, 'm') ?? '', 10);
    const genre = Number.parseInt(queryValue(query, 'g') ?? '', 10);
    const language = Number.parseInt(queryValue(query, 'l') ?? '', 10);

    return this.beatmaps.search({
      query: queryValue(query, 'q'),
      category: queryValue(query, 's') ?? 'any',
      mode: Number.isFinite(mode) ? mode : undefined,
      genre: Number.isFinite(genre) ? genre : undefined,
      language: Number.isFinite(language) ? language : undefined,
      sort: queryValue(query, 'sort') ?? 'ranked_desc',
      offset: Number.isFinite(offset) && offset > 0 ? offset : 0,
      ownerUserId: user?.id ?? null,
    });
  }

  /** `GET /api/v2/beatmapsets/lookup?beatmap_id=`. */
  @Get('beatmapsets/lookup')
  async lookupSet(@Query() query: QueryParams): Promise<Record<string, unknown>> {
    const beatmapId = Number.parseInt(queryValue(query, 'beatmap_id') ?? '', 10);
    if (!Number.isFinite(beatmapId)) throw apiError(404, 'Beatmapset not found');
    const set = await this.beatmaps.getSetByBeatmapId(beatmapId);
    if (!set) throw apiError(404, 'Beatmapset not found');
    return set;
  }

  /** `GET /api/v2/beatmapsets/{id}/download`: the `.osz` archive. */
  @Get('beatmapsets/:id/download')
  @UseGuards(AuthGuard)
  async download(
    @Param('id') id: string,
    @CurrentUser() user: AuthenticatedUser,
    @Req() req: Request,
    @Res() response: Response,
  ): Promise<void> {
    // Each download is a whole package (audit S7): a per-user and per-address
    // limit keeps one client from draining the host.
    this.downloadThrottle.check(user.id, requestClientIp(req, this.config.trustProxy));
    const setId = Number.parseInt(id, 10);
    const set = Number.isFinite(setId) ? await this.beatmaps.getSetRow(setId) : null;
    if (!set) throw apiError(404, 'Beatmapset not found');

    // s3 driver: 302 to a short-lived presigned URL; local driver: stream.
    const url = await this.storage.presign(
      set.archive_key,
      this.config.storage.downloadUrlExpirySeconds,
    );
    if (url) {
      response.redirect(302, url);
      return;
    }

    const file = await this.storage.open(set.archive_key);
    if (!file) throw apiError(404, 'Archive not found');
    response.setHeader('Content-Type', 'application/octet-stream');
    response.setHeader('Content-Length', file.size);
    file.stream.pipe(response);
  }

  /** `GET /api/v2/beatmapsets/{id}`. */
  @Get('beatmapsets/:id')
  async getSet(@Param('id') id: string): Promise<Record<string, unknown>> {
    const setId = Number.parseInt(id, 10);
    const set = Number.isFinite(setId) ? await this.beatmaps.getSet(setId) : null;
    if (!set) throw apiError(404, 'Beatmapset not found');
    return set;
  }

  /** `GET /api/v2/beatmaps/lookup?id=|checksum=|filename=`. */
  @Get('beatmaps/lookup')
  async lookup(@Query() query: QueryParams): Promise<Record<string, unknown>> {
    const id = Number.parseInt(queryValue(query, 'id') ?? '', 10);
    const beatmap = await this.beatmaps.lookupBeatmap({
      id: Number.isFinite(id) ? id : undefined,
      checksum: queryValue(query, 'checksum'),
      filename: queryValue(query, 'filename'),
    });
    if (!beatmap) throw apiError(404, 'Beatmap not found');
    return beatmap;
  }

  /** `GET /api/v2/beatmaps/?ids[]=`: batch lookup. */
  @Get('beatmaps')
  async batchLookup(@Query() query: QueryParams): Promise<{ beatmaps: Record<string, unknown>[] }> {
    const raw = query['ids'] ?? query['ids[]'];
    const values = Array.isArray(raw) ? raw : raw == null ? [] : [raw];
    const ids = values
      .map((value) => Number.parseInt(String(value), 10))
      .filter((value) => Number.isFinite(value));
    return { beatmaps: await this.beatmaps.getBeatmaps(ids) };
  }
}
