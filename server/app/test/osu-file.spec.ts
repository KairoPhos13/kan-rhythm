/** Unit tests for the `.osu` parser and id rewriter (Phase 3). */
import { md5Of, parseOsu, rewriteOsu } from '../src/beatmaps/osu-file';
import { minimalOsu } from './fixture';

describe('parseOsu', () => {
  const info = parseOsu(minimalOsu());

  it('reads metadata, general and difficulty values', () => {
    expect(info.metadata.title).toBe('Fixture Song');
    expect(info.metadata.artist).toBe('Fixture Artist');
    expect(info.metadata.creator).toBe('KairoPhos');
    expect(info.metadata.version).toBe('Normal');
    expect(info.metadata.tags).toContain('kan');
    expect(info.metadata.beatmapId).toBe(0);
    expect(info.metadata.beatmapSetId).toBe(0);
    expect(info.general.audioFilename).toBe('audio.wav');
    expect(info.general.mode).toBe(0);
    expect(info.difficulty.circleSize).toBe(4);
    expect(info.difficulty.overallDifficulty).toBe(6);
    expect(info.difficulty.approachRate).toBe(7);
    expect(info.difficulty.hpDrainRate).toBe(5);
  });

  it('computes BPM from the first uninherited timing point', () => {
    expect(info.bpm).toBe(120);
  });

  it('counts hit objects and estimates combo and length', () => {
    expect(info.hitObjectCount).toBe(4);
    expect(info.countCircles).toBe(2);
    expect(info.countSliders).toBe(1);
    expect(info.countSpinners).toBe(1);
    // two circles (1 each) + slider (slides + 1) + spinner (1)
    expect(info.maxCombo).toBe(5);
    // last hit object ends at 2000 ms
    expect(info.totalLengthSeconds).toBe(2);
    expect(info.hitLengthSeconds).toBe(2);
  });

  it('reads the background filename from [Events]', () => {
    expect(info.backgroundFile).toBe('bg.png');
  });
});

describe('crafted values are clamped (audit S8)', () => {
  const hostile = [
    'osu file format v14',
    '',
    '[General]',
    'AudioFilename: audio.wav',
    'Mode: 1e300',
    '',
    '[Metadata]',
    'Title:Hostile',
    'Artist:Hostile',
    'Creator:nobody',
    'Version:Hostile',
    'BeatmapID:1e300',
    'BeatmapSetID:99999999999999',
    '',
    '[Difficulty]',
    'HPDrainRate:1e40',
    'CircleSize:-5',
    'OverallDifficulty:12',
    'ApproachRate:1e400',
    '',
    '[TimingPoints]',
    '0,1e-300,4,2,0,60,1,0',
    '',
    '[HitObjects]',
    '256,192,1e15,2,0,L|440:100,1e300',
    '256,192,0,8,0,1e15,0:0:0:0:',
    '',
  ].join('\n');

  const info = parseOsu(hostile);

  it('keeps difficulty settings inside 0..10 and mode inside 0..3', () => {
    expect(info.difficulty.hpDrainRate).toBe(10);
    expect(info.difficulty.circleSize).toBe(0);
    expect(info.difficulty.overallDifficulty).toBe(10);
    expect(info.difficulty.approachRate).toBe(10);
    // A crafted mode lands inside 0..3 (the importer then skips any mode
    // that is not kan!'s mode 0).
    expect(info.general.mode).toBe(3);
  });

  it('keeps ids integral and inside the integer columns', () => {
    expect(Number.isInteger(info.metadata.beatmapId)).toBe(true);
    expect(info.metadata.beatmapId).toBeLessThanOrEqual(2_147_483_647);
    expect(info.metadata.beatmapSetId).toBe(2_147_483_647);
  });

  it('keeps BPM, lengths and combo finite and bounded', () => {
    expect(info.bpm).toBe(1000);
    expect(info.totalLengthSeconds).toBeLessThanOrEqual(24 * 60 * 60);
    expect(info.hitLengthSeconds).toBeLessThanOrEqual(24 * 60 * 60);
    expect(Number.isFinite(info.maxCombo)).toBe(true);
    expect(info.maxCombo).toBeLessThanOrEqual(10_000_000);
  });
});

describe('rewriteOsu', () => {
  it('replaces existing BeatmapID / BeatmapSetID values', () => {
    const rewritten = rewriteOsu(minimalOsu(), { beatmapId: 42, beatmapSetId: 7 });
    const parsed = parseOsu(rewritten);
    expect(parsed.metadata.beatmapId).toBe(42);
    expect(parsed.metadata.beatmapSetId).toBe(7);
    expect(rewritten).toContain('BeatmapID: 42');
    expect(rewritten).toContain('BeatmapSetID: 7');
    expect(rewritten).not.toContain('BeatmapID: 0');
  });

  it('appends the id keys when they are missing from [Metadata]', () => {
    const stripped = minimalOsu()
      .split('\n')
      .filter((line) => !line.startsWith('BeatmapID:') && !line.startsWith('BeatmapSetID:'))
      .join('\n');
    const rewritten = rewriteOsu(stripped, { beatmapId: 5, beatmapSetId: 6 });
    expect(rewritten).toContain('BeatmapID: 5');
    expect(rewritten).toContain('BeatmapSetID: 6');
    // the keys must land inside [Metadata], i.e. before the [Difficulty] header
    expect(rewritten.indexOf('BeatmapID: 5')).toBeLessThan(rewritten.indexOf('[Difficulty]'));
  });

  it('keeps every other line untouched', () => {
    const original = minimalOsu();
    const rewritten = rewriteOsu(original, { beatmapId: 1, beatmapSetId: 2 });
    for (const line of original.split('\n')) {
      if (line.startsWith('BeatmapID:') || line.startsWith('BeatmapSetID:')) continue;
      expect(rewritten).toContain(line);
    }
  });

  it('preserves CRLF line endings', () => {
    const crlf = minimalOsu().replace(/\n/g, '\r\n');
    const rewritten = rewriteOsu(crlf, { beatmapId: 3, beatmapSetId: 4 });
    expect(rewritten).toContain('BeatmapID: 3\r\n');
    expect(rewritten.split('\r\n').length).toBeGreaterThan(10);
    expect(parseOsu(rewritten).metadata.beatmapId).toBe(3);
  });

  it('produces a stable MD5 over the rewritten bytes (the client checksum)', () => {
    const a = rewriteOsu(minimalOsu(), { beatmapId: 8, beatmapSetId: 9 });
    const b = rewriteOsu(minimalOsu(), { beatmapId: 8, beatmapSetId: 9 });
    expect(md5Of(a)).toBe(md5Of(b));
    expect(md5Of(a)).toMatch(/^[0-9a-f]{32}$/);
    // a different id rewrites to different bytes and a different checksum
    expect(md5Of(rewriteOsu(minimalOsu(), { beatmapId: 9, beatmapSetId: 9 }))).not.toBe(md5Of(a));
  });

  it('rejects files without a [Metadata] section', () => {
    expect(() => rewriteOsu('osu file format v14\n\n[General]\nMode: 0\n', { beatmapId: 1, beatmapSetId: 1 })).toThrow();
  });
});
