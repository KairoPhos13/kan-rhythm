/**
 * Pure rules for score submission (server/docs/lazer-protocol.md §5.2).
 *
 * A score is the client's own claim and nothing else (mvp-scope.md §6): no
 * replay is re-simulated here. What this module does guarantee is that only
 * well-formed data reaches the database (audit S6: out-of-range values used to
 * be stored as sent and answered 500 on the way in). Values are validated or
 * clamped as documented below; everything here is offline-testable.
 */

/** The request body of `PUT /api/v2/beatmaps/{id}/solo/scores/{token}`. */
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

/** A validated score, ready to be stored. */
export interface ScoreSubmission {
  rank: string;
  totalScore: number;
  totalScoreWithoutMods: number;
  accuracy: number;
  maxCombo: number;
  rulesetId: number;
  passed: boolean;
  mods: unknown[];
  statistics: Record<string, unknown>;
  maximumStatistics: Record<string, unknown>;
  pauses: unknown[];
  buildId: number | null;
  startedAt: Date | null;
  endedAt: Date;
}

/** Grade letters the client sends (`SoloScoreInfo.rank`). */
export const SCORE_RANKS = ['XH', 'X', 'SH', 'S', 'A', 'B', 'C', 'D'] as const;

/** Upper bound on a total score: no real score is anywhere near it. */
export const MAX_TOTAL_SCORE = 1_000_000_000_000;
/** Upper bound on a combo count (the longest levels are in the thousands). */
export const MAX_COMBO = 10_000_000;
/** A play older than this cannot be genuine; older claims are clamped away. */
export const MAX_PLAY_AGE_MS = 24 * 60 * 60 * 1000;
/** Clock skew forgiven on `ended_at` (the client stamps it locally). */
export const CLOCK_SKEW_MS = 2 * 60 * 1000;

/** A score the server refuses: mapped to HTTP 400 by the score routes. */
export class ScoreRejectedError extends Error {}

/**
 * A numeric field: absent means "the client did not send it" (the score's own
 * zero), anything present must parse to a finite number.
 */
function numberField(value: unknown, field: string, fallback = 0): number {
  if (value == null || value === '') return fallback;
  const parsed = typeof value === 'number' ? value : Number.parseFloat(String(value));
  if (!Number.isFinite(parsed)) {
    throw new ScoreRejectedError(`${field} must be a finite number`);
  }
  return parsed;
}

function intField(value: unknown, field: string, min: number, max: number): number {
  const parsed = Math.trunc(numberField(value, field));
  if (parsed < min || parsed > max) {
    throw new ScoreRejectedError(`${field} must be between ${min} and ${max}`);
  }
  return parsed;
}

function boolField(value: unknown, fallback: boolean): boolean {
  if (typeof value === 'boolean') return value;
  if (value === 'true') return true;
  if (value === 'false') return false;
  return fallback;
}

function clamp(value: number, min: number, max: number): number {
  return Math.min(Math.max(value, min), max);
}

/** A client-stamped date, clamped into `[now - MAX_PLAY_AGE, now + skew]`. */
function dateField(value: unknown, now: Date): Date | null {
  if (value == null || value === '') return null;
  const date = new Date(String(value));
  if (Number.isNaN(date.getTime())) return null;
  return new Date(
    clamp(date.getTime(), now.getTime() - MAX_PLAY_AGE_MS, now.getTime() + CLOCK_SKEW_MS),
  );
}

function objectField(value: unknown): Record<string, unknown> {
  return value != null && typeof value === 'object' && !Array.isArray(value)
    ? (value as Record<string, unknown>)
    : {};
}

function arrayField(value: unknown): unknown[] {
  return Array.isArray(value) ? value : [];
}

/**
 * Validates and clamps a submitted score (audit S6):
 *  - `total_score`, `total_score_without_mods`, `max_combo` and `ruleset_id`
 *    must be finite and inside their ranges (refused otherwise: 400);
 *  - `accuracy` is clamped into 0..1 (float fuzz is forgiven, claims of 200%
 *    are not stored);
 *  - `rank` must be one of the eight grades;
 *  - `started_at` / `ended_at` are clamped into a sane window around `now`,
 *    and ordering never looks at them (the server clock does, audit S6).
 */
export function parseScoreSubmission(
  input: SubmitScoreInput,
  now: Date = new Date(),
): ScoreSubmission {
  const rank = String(input.rank ?? 'D').toUpperCase();
  if (!(SCORE_RANKS as readonly string[]).includes(rank)) {
    throw new ScoreRejectedError(`rank must be one of ${SCORE_RANKS.join(', ')}`);
  }

  const startedAt = dateField(input.started_at, now);
  let endedAt = dateField(input.ended_at, now) ?? now;
  if (startedAt && endedAt.getTime() < startedAt.getTime()) endedAt = startedAt;

  return {
    rank,
    totalScore: intField(input.total_score, 'total_score', 0, MAX_TOTAL_SCORE),
    totalScoreWithoutMods: intField(
      input.total_score_without_mods,
      'total_score_without_mods',
      0,
      MAX_TOTAL_SCORE,
    ),
    accuracy: clamp(numberField(input.accuracy, 'accuracy'), 0, 1),
    maxCombo: intField(input.max_combo, 'max_combo', 0, MAX_COMBO),
    rulesetId: intField(input.ruleset_id, 'ruleset_id', 0, 2_147_483_647),
    passed: boolField(input.passed, true),
    mods: arrayField(input.mods),
    statistics: objectField(input.statistics),
    maximumStatistics: objectField(input.maximum_statistics),
    pauses: arrayField(input.pauses),
    buildId:
      input.build_id == null
        ? null
        : intField(input.build_id, 'build_id', 0, 2_147_483_647),
    startedAt,
    endedAt,
  };
}
