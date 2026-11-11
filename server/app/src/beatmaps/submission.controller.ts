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
import { Body, Controller, Param, Patch, Put, Req, UseGuards } from '@nestjs/common';
import { apiError } from '../common/http';
import { UploadRequest, UploadedFile } from '../common/multipart';
import { AuthenticatedUser } from '../auth/auth.service';
import { AuthGuard, CurrentUser } from '../auth/auth.guard';
import { PutBeatmapSetResult, SubmissionService } from './submission.service';
import { parsePutBeatmapSetBody } from './submission-rules';

@Controller('beatmap-submission')
export class SubmissionController {
  constructor(private readonly submission: SubmissionService) {}

  /** `PUT /beatmap-submission/beatmapsets`: reserve the ids of a submission. */
  @Put('beatmapsets')
  @UseGuards(AuthGuard)
  async reserve(
    @CurrentUser() user: AuthenticatedUser,
    @Body() body: unknown,
  ): Promise<PutBeatmapSetResult> {
    const parsed = parsePutBeatmapSetBody(body);
    if (!parsed.ok) throw apiError(400, parsed.error);
    return this.submission.reserve(user, parsed.value);
  }

  /** `PUT /beatmap-submission/beatmapsets/{id}`: upload the full package. */
  @Put('beatmapsets/:id')
  @UseGuards(AuthGuard)
  async replace(
    @Param('id') id: string,
    @CurrentUser() user: AuthenticatedUser,
    @Req() request: UploadRequest,
  ): Promise<void> {
    const [archive] = filesOf(request, 'beatmapArchive');
    if (!archive) throw apiError(400, 'No beatmapArchive file in the upload');
    await this.submission.replacePackage(user, beatmapsetId(id), archive.buffer);
  }

  /** `PATCH /beatmap-submission/beatmapsets/{id}`: changed files and deletions. */
  @Patch('beatmapsets/:id')
  @UseGuards(AuthGuard)
  async patch(
    @Param('id') id: string,
    @CurrentUser() user: AuthenticatedUser,
    @Req() request: UploadRequest,
  ): Promise<void> {
    const changed = filesOf(request, 'filesChanged').map((file) => ({
      entryName: file.originalname,
      data: file.buffer,
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
