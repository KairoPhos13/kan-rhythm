/**
 * The editor's submission routes (server/docs/lazer-protocol.md §4.8).
 *
 * They sit under `/beatmap-submission` because that is what
 * `EndpointConfiguration.BeatmapSubmissionServiceUrl` is set to; the client
 * appends `/beatmapsets` and `/beatmapsets/{id}` itself
 * (`PutBeatmapSetRequest.cs`, `ReplaceBeatmapPackageRequest.cs`,
 * `PatchBeatmapPackageRequest.cs`). All three carry the signed-in creator's
 * bearer token like any API request.
 */
import { Body, Controller, Inject, Param, Patch, Put, Req, UseGuards } from '@nestjs/common';
import { Request } from 'express';
import { readFileSync } from 'node:fs';
import { APP_CONFIG, AppConfig } from '../config';
import { apiError } from '../common/http';
import { requestClientIp } from '../common/client-ip';
import { RouteThrottle } from '../common/throttle';
import { UploadRequest, UploadedFile } from '../common/multipart';
import { AuthenticatedUser } from '../auth/auth.service';
import { AuthGuard, CurrentUser } from '../auth/auth.guard';
import { PutBeatmapSetResult, SubmissionService } from './submission.service';
import { parsePutBeatmapSetBody } from './submission-rules';

@Controller('beatmap-submission')
export class SubmissionController {
  /**
   * Reserving ids also reads and hashes the stored package of an existing set
   * (up to the package limit per call), hence the limit (audit S7).
   */
  private readonly reserveThrottle = new RouteThrottle(30, 60);

  constructor(
    private readonly submission: SubmissionService,
    @Inject(APP_CONFIG) private readonly config: AppConfig,
  ) {}

  /** `PUT /beatmap-submission/beatmapsets`: reserve the ids of a submission. */
  @Put('beatmapsets')
  @UseGuards(AuthGuard)
  async reserve(
    @CurrentUser() user: AuthenticatedUser,
    @Body() body: unknown,
    @Req() req: Request,
  ): Promise<PutBeatmapSetResult> {
    this.reserveThrottle.check(user.id, requestClientIp(req, this.config.trustProxy));
    const parsed = parsePutBeatmapSetBody(body);
    if (!parsed.ok) throw apiError(400, parsed.error);
    return this.submission.reserve(user, parsed.value);
  }

  /**
   * `PUT /beatmap-submission/beatmapsets/{id}`: upload the full package. The
   * multipart middleware streams it to a temporary file and refuses a second
   * file part; the file is read from there, never from memory (audit S1).
   */
  @Put('beatmapsets/:id')
  @UseGuards(AuthGuard)
  async replace(
    @Param('id') id: string,
    @CurrentUser() user: AuthenticatedUser,
    @Req() request: UploadRequest,
  ): Promise<void> {
    const files = request.files ?? [];
    const archive = files.find((file) => file.fieldname === 'beatmapArchive');
    if (!archive || files.length !== 1) {
      throw apiError(400, 'A level upload carries exactly one beatmapArchive file');
    }
    await this.submission.replacePackage(user, beatmapsetId(id), archive.path);
  }

  /** `PATCH /beatmap-submission/beatmapsets/{id}`: changed files and deletions. */
  @Patch('beatmapsets/:id')
  @UseGuards(AuthGuard)
  async patch(
    @Param('id') id: string,
    @CurrentUser() user: AuthenticatedUser,
    @Req() request: UploadRequest,
  ): Promise<void> {
    // The client sends one part per changed file (PatchBeatmapPackageRequest);
    // each was streamed to a temporary file and is read back one at a time.
    const changed = filesOf(request, 'filesChanged').map((file) => ({
      entryName: file.originalname,
      data: readFileSync(file.path),
    }));
    const deleted = textFields(request, 'filesDeleted');
    await this.submission.patchPackage(user, beatmapsetId(id), changed, deleted);
  }
}

function beatmapsetId(raw: string): number {
  const setId = Number.parseInt(raw, 10);
  if (!Number.isFinite(setId) || setId <= 0) throw apiError(404, 'Beatmapset not found');
  return setId;
}

function filesOf(request: UploadRequest, fieldname: string): UploadedFile[] {
  return (request.files ?? []).filter((file) => file.fieldname === fieldname);
}

/** Repeated form fields arrive as an array, single ones as a string. */
function textFields(request: UploadRequest, name: string): string[] {
  const value = (request.body as Record<string, unknown> | undefined)?.[name];
  if (value == null) return [];
  return (Array.isArray(value) ? value : [value]).map((item) => String(item));
}
