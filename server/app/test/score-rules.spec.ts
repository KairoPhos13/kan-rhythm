/**
 * Unit tests for score submission validation (audit S6): the client's claims
 * are refused or clamped before anything is stored, and out-of-range values
 * answer 400 instead of a database error.
 */
import {
  CLOCK_SKEW_MS,
  MAX_PLAY_AGE_MS,
  parseScoreSubmission,
  ScoreRejectedError,
  SCORE_RANKS,
  SubmitScoreInput,
} from '../src/scores/score-rules';

const NOW = new Date('2026-11-12T12:00:00Z');

function parse(input: SubmitScoreInput) {
  return parseScoreSubmission(input, NOW);
}

describe('parseScoreSubmission', () => {
  it('accepts what the client sends and defaults what it leaves out', () => {
    const score = parse({ rank: 'S', total_score: 100000, ruleset_id: 0, passed: true });
    expect(score).toMatchObject({
      rank: 'S',
      totalScore: 100000,
      totalScoreWithoutMods: 0,
      accuracy: 0,
      maxCombo: 0,
      rulesetId: 0,
      passed: true,
    });
    expect(score.endedAt).toEqual(NOW);
    expect(score.startedAt).toBeNull();
  });

  it('refuses totals, combos and ids outside their range', () => {
    expect(() => parse({ total_score: -1 })).toThrow(ScoreRejectedError);
    expect(() => parse({ total_score: '1e400' })).toThrow(/total_score/);
    expect(() => parse({ total_score: Number.POSITIVE_INFINITY })).toThrow(/total_score/);
    expect(() => parse({ total_score: 'abc' })).toThrow(/total_score/);
    expect(() => parse({ total_score_without_mods: -5 })).toThrow(/total_score_without_mods/);
    expect(() => parse({ max_combo: -1 })).toThrow(/max_combo/);
    expect(() => parse({ max_combo: 1e9 })).toThrow(/max_combo/);
    expect(() => parse({ ruleset_id: -1 })).toThrow(/ruleset_id/);
    expect(() => parse({ build_id: 1e12 })).toThrow(/build_id/);
  });

  it('refuses a rank that is not one of the eight grades', () => {
    for (const rank of SCORE_RANKS) expect(parse({ rank }).rank).toBe(rank);
    expect(() => parse({ rank: 'Z' })).toThrow(/rank/);
    expect(() => parse({ rank: '<script>' })).toThrow(/rank/);
  });

  it('clamps accuracy into 0..1 instead of storing 200%', () => {
    expect(parse({ accuracy: 2.5 }).accuracy).toBe(1);
    expect(parse({ accuracy: -1 }).accuracy).toBe(0);
    expect(parse({ accuracy: 0.987654321 }).accuracy).toBeCloseTo(0.987654321);
    expect(() => parse({ accuracy: '1e400' })).toThrow(/accuracy/);
  });

  it('clamps client-stamped dates into a sane window', () => {
    const score = parse({
      started_at: '2020-01-01T00:00:00Z',
      ended_at: '2031-01-01T00:00:00Z',
    });
    expect(score.startedAt?.getTime()).toBe(NOW.getTime() - MAX_PLAY_AGE_MS);
    expect(score.endedAt.getTime()).toBe(NOW.getTime() + CLOCK_SKEW_MS);

    // ended_at never before started_at (a claim that cannot be true)
    const swapped = parse({
      started_at: '2026-11-12T11:00:00Z',
      ended_at: '2026-11-12T10:00:00Z',
    });
    expect(swapped.endedAt.getTime()).toBe(swapped.startedAt?.getTime());
  });

  it('keeps only well-shaped collections', () => {
    const score = parse({ mods: 'none', statistics: [1, 2], maximum_statistics: 'x', pauses: 'y' });
    expect(score.mods).toEqual([]);
    expect(score.statistics).toEqual({});
    expect(score.maximumStatistics).toEqual({});
    expect(score.pauses).toEqual([]);
  });
});
