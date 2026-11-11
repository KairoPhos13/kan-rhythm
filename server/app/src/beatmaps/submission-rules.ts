/**
 * Pure rules for the editor's submission flow
 * (server/docs/lazer-protocol.md §4.8): request validation, the status a
 * submission leaves a set in, osu!'s cap on unranked sets, and the file hash
 * the client diffs packages by. Nothing here touches the database or storage,
 * so it is unit-tested offline (test/submission-rules.spec.ts).
 */
import { createHash } from 'node:crypto';

/** Client wire values of `BeatmapSubmissionTarget` (StringEnumConverter). */
export const SUBMISSION_TARGETS = ['WIP', 'Pending'] as const;
export type SubmissionTarget = (typeof SUBMISSION_TARGETS)[number];

/** Statuses a submission can put a set in (`BeatmapOnlineStatus`). */
export const TARGET_STATUS: Record<SubmissionTarget, number> = { WIP: -1, Pending: 0 };

/** A sensible ceiling on difficulties per set (also bounds id reservations). */
export const MAX_DIFFICULTIES_PER_SET = 64;

export interface PutBeatmapSetInput {
  /** The set to update, or null to create one. */
  beatmapsetId: number | null;
  beatmapsToCreate: number;
  beatmapsToKeep: number[];
  target: SubmissionTarget;
}

export type ParseResult<T> = { ok: true; value: T } | { ok: false; error: string };

/**
 * Validates the JSON body of `PUT /beatmap-submission/beatmapsets`
 * (`PutBeatmapSetRequest.cs`: `beatmapset_id`, `beatmaps_to_create`,
 * `beatmaps_to_keep`, `target`; `notify_on_discussion_replies` is accepted and
 * ignored, kan! has no discussions).
 */
export function parsePutBeatmapSetBody(body: unknown): ParseResult<PutBeatmapSetInput> {
  const record = (body ?? {}) as Record<string, unknown>;

  let beatmapsetId: number | null = null;
  if (record['beatmapset_id'] != null) {
    const parsed = toInt(record['beatmapset_id']);
    if (parsed == null || parsed <= 0) {
      return { ok: false, error: 'beatmapset_id must be a positive integer' };
    }
    beatmapsetId = parsed;
  }

  const beatmapsToCreate = toInt(record['beatmaps_to_create']);
  if (beatmapsToCreate == null || beatmapsToCreate < 0) {
    return { ok: false, error: 'beatmaps_to_create must be a non-negative integer' };
  }
  if (beatmapsToCreate > MAX_DIFFICULTIES_PER_SET) {
    return { ok: false, error: `beatmaps_to_create may not exceed ${MAX_DIFFICULTIES_PER_SET}` };
  }

  const rawKeep = record['beatmaps_to_keep'] ?? [];
  if (!Array.isArray(rawKeep)) {
    return { ok: false, error: 'beatmaps_to_keep must be an array of beatmap ids' };
  }
  const beatmapsToKeep: number[] = [];
  for (const value of rawKeep) {
    const id = toInt(value);
    if (id == null || id <= 0) {
      return { ok: false, error: 'beatmaps_to_keep must contain positive integer ids' };
    }
    beatmapsToKeep.push(id);
  }
  if (beatmapsToKeep.length > MAX_DIFFICULTIES_PER_SET) {
    return { ok: false, error: `beatmaps_to_keep may not exceed ${MAX_DIFFICULTIES_PER_SET} ids` };
  }
  if (beatmapsetId == null && beatmapsToKeep.length > 0) {
    return { ok: false, error: 'beatmaps_to_keep needs the beatmapset_id of an existing set' };
  }

  const target = String(record['target'] ?? '');
  if (!(SUBMISSION_TARGETS as readonly string[]).includes(target)) {
    return {
      ok: false,
      error: `target must be one of ${SUBMISSION_TARGETS.join(', ')}`,
    };
  }

  return {
    ok: true,
    value: { beatmapsetId, beatmapsToCreate, beatmapsToKeep, target: target as SubmissionTarget },
  };
}

function toInt(value: unknown): number | null {
  const parsed = typeof value === 'number' ? value : Number.parseInt(String(value ?? ''), 10);
  return Number.isInteger(parsed) ? parsed : null;
}

/**
 * The status a set has after a submission: the client's WIP/pending choice
 * (BeatmapSubmissionSettings) while the level is not ranked yet, and the
 * existing status once it is, so a submission can never rank or unrank a level
 * (that is the server owner's call, via the admin CLI). A graveyarded level
 * comes back to the chosen status when its creator resubmits it, as in osu!.
 */
export function statusAfterSubmission(currentStatus: number | null, target: SubmissionTarget): number {
  return currentStatus == null || currentStatus <= 0 ? TARGET_STATUS[target] : currentStatus;
}

/**
 * osu!'s cap on unranked (pending/WIP) sets per creator: a base allowance plus
 * one slot per ranked set, never more than the maximum.
 */
export function unrankedSetLimit(base: number, max: number, rankedSets: number): number {
  return Math.min(Math.max(base, 0) + Math.max(rankedSets, 0), Math.max(max, 0));
}

/**
 * `sha2_hash` of a package file (`PutBeatmapSetResponse.Files`): SHA-256 as
 * lowercase hex, the same value the client computes with
 * `StreamExtensions.ComputeSHA2Hash` to diff its export against the server's
 * stored package (it compares the two case-insensitively).
 */
export function sha2Hash(content: Buffer): string {
  return createHash('sha256').update(content).digest('hex');
}

/**
 * Archive entry names the server accepts: relative paths inside the package
 * with no traversal, no backslashes and no drive letters.
 */
export function isSafeEntryName(entryName: string): boolean {
  if (entryName.length === 0 || entryName.length > 255) return false;
  if (entryName.includes('\\') || entryName.startsWith('/')) return false;
  return !entryName.split('/').some((segment) => segment === '' || segment === '.' || segment === '..');
}
