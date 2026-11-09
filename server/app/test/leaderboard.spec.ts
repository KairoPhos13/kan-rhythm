/** Unit tests for the leaderboard logic (Phase 3). */
import { ScoreRow } from '../src/api/serializers';
import {
  buildBeatmapScoresQuery,
  buildUserScoresQuery,
  compareScores,
  computeLeaderboard,
} from '../src/scores/score-queries';

function score(overrides: Partial<ScoreRow>): ScoreRow {
  return {
    id: 1,
    user_id: 1,
    beatmap_id: 1,
    ruleset_id: 0,
    ruleset_short_name: 'kan',
    total_score: 1000,
    total_score_without_mods: 1000,
    accuracy: 1,
    max_combo: 10,
    rank: 'S',
    passed: true,
    mods: [],
    statistics: {},
    maximum_statistics: {},
    pauses: [],
    build_id: null,
    started_at: null,
    ended_at: new Date('2026-01-01T10:00:00Z'),
    ...overrides,
  };
}

describe('computeLeaderboard', () => {
  it('returns an empty board for no scores', () => {
    const board = computeLeaderboard([], 50, 1);
    expect(board.entries).toEqual([]);
    expect(board.scoreCount).toBe(0);
    expect(board.userScore).toBeNull();
  });

  it('keeps only the best score per user', () => {
    const rows = [
      score({ id: 1, user_id: 1, total_score: 500 }),
      score({ id: 2, user_id: 1, total_score: 900 }),
      score({ id: 3, user_id: 2, total_score: 700 }),
    ];
    const board = computeLeaderboard(rows, 50, null);
    expect(board.entries.map((e) => Number(e.score.id))).toEqual([2, 3]);
    expect(board.scoreCount).toBe(2);
  });

  it('orders by total score desc, then by date asc, and positions from 1', () => {
    const rows = [
      score({ id: 1, user_id: 1, total_score: 700, ended_at: new Date('2026-01-02T10:00:00Z') }),
      score({ id: 2, user_id: 2, total_score: 700, ended_at: new Date('2026-01-01T10:00:00Z') }),
      score({ id: 3, user_id: 3, total_score: 800, ended_at: new Date('2026-01-03T10:00:00Z') }),
    ];
    const board = computeLeaderboard(rows, 50, null);
    expect(board.entries.map((e) => Number(e.score.id))).toEqual([3, 2, 1]);
    expect(board.entries.map((e) => e.position)).toEqual([1, 2, 3]);
  });

  it('reports the requesting user position even beyond the page limit', () => {
    const rows = [
      score({ id: 1, user_id: 1, total_score: 300 }),
      score({ id: 2, user_id: 2, total_score: 200 }),
      score({ id: 3, user_id: 3, total_score: 100 }),
    ];
    const board = computeLeaderboard(rows, 2, 3);
    expect(board.entries).toHaveLength(2);
    expect(board.scoreCount).toBe(3);
    expect(board.userScore?.position).toBe(3);
    expect(Number(board.userScore?.score.id)).toBe(3);
  });

  it('has no user score for guests or users without scores', () => {
    const rows = [score({ id: 1, user_id: 1 })];
    expect(computeLeaderboard(rows, 50, null).userScore).toBeNull();
    expect(computeLeaderboard(rows, 50, 99).userScore).toBeNull();
  });
});

describe('compareScores', () => {
  it('breaks total score ties by earliest date then id', () => {
    const earlier = score({ id: 2, ended_at: new Date('2026-01-01T00:00:00Z') });
    const later = score({ id: 1, ended_at: new Date('2026-01-02T00:00:00Z') });
    expect(compareScores(earlier, later)).toBeLessThan(0);
    const sameDateA = score({ id: 7, ended_at: new Date('2026-01-01T00:00:00Z') });
    const sameDateB = score({ id: 8, ended_at: new Date('2026-01-01T00:00:00Z') });
    expect(compareScores(sameDateA, sameDateB)).toBeLessThan(0);
  });
});

describe('score queries', () => {
  it('orders best scores by total score and recent scores by date', () => {
    expect(buildUserScoresQuery('best')?.orderBy).toBe('total_score DESC, ended_at ASC');
    expect(buildUserScoresQuery('recent')?.orderBy).toBe('ended_at DESC');
    expect(buildUserScoresQuery('firsts')).toBeNull();
    expect(buildUserScoresQuery('pinned')).toBeNull();
  });

  it('fetches a beatmap scores by beatmap id and ruleset short name', () => {
    const text = buildBeatmapScoresQuery();
    expect(text).toContain('WHERE beatmap_id = $1 AND ruleset_short_name = $2');
    expect(text).toContain('ORDER BY ended_at ASC, id ASC');
  });
});
