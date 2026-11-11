/**
 * Unit tests for the submission rules (src/beatmaps/submission-rules.ts): the
 * `PUT /beatmap-submission/beatmapsets` body, the status a submission leaves a
 * set in, osu!'s cap on unranked sets, and the package file hash the client
 * diffs by. No database, no HTTP.
 */
import {
  isSafeEntryName,
  MAX_DIFFICULTIES_PER_SET,
  parsePutBeatmapSetBody,
  sha2Hash,
  statusAfterSubmission,
  unrankedSetLimit,
} from '../src/beatmaps/submission-rules';

describe('parsePutBeatmapSetBody', () => {
  it('accepts the body of a new set exactly as PutBeatmapSetRequest sends it', () => {
    expect(
      parsePutBeatmapSetBody({
        beatmapset_id: null,
        beatmaps_to_create: 2,
        beatmaps_to_keep: [],
        target: 'Pending',
        notify_on_discussion_replies: true,
      }),
    ).toEqual({
      ok: true,
      value: {
        beatmapsetId: null,
        beatmapsToCreate: 2,
        beatmapsToKeep: [],
        target: 'Pending',
      },
    });
  });

  it('accepts the body of an update of an existing set', () => {
    const parsed = parsePutBeatmapSetBody({
      beatmapset_id: 12,
      beatmaps_to_create: 1,
      beatmaps_to_keep: [34, 35],
      target: 'WIP',
    });
    expect(parsed.ok).toBe(true);
    if (parsed.ok) {
      expect(parsed.value.beatmapsetId).toBe(12);
      expect(parsed.value.beatmapsToKeep).toEqual([34, 35]);
      expect(parsed.value.target).toBe('WIP');
    }
  });

  it('rejects anything else', () => {
    expect(parsePutBeatmapSetBody({ beatmaps_to_create: -1, target: 'Pending' }).ok).toBe(false);
    expect(parsePutBeatmapSetBody({ beatmaps_to_create: 1, target: 'Qualified' }).ok).toBe(false);
    expect(parsePutBeatmapSetBody({ beatmaps_to_create: 1 }).ok).toBe(false);
    expect(
      parsePutBeatmapSetBody({ beatmaps_to_create: 1, beatmaps_to_keep: [0], target: 'WIP' }).ok,
    ).toBe(false);
    expect(
      parsePutBeatmapSetBody({ beatmapset_id: 'x', beatmaps_to_create: 1, target: 'WIP' }).ok,
    ).toBe(false);
    // keeping ids needs the set they belong to
    expect(
      parsePutBeatmapSetBody({ beatmaps_to_create: 0, beatmaps_to_keep: [5], target: 'WIP' }).ok,
    ).toBe(false);
    expect(parsePutBeatmapSetBody(null).ok).toBe(false);
  });

  it('bounds the size of a submission', () => {
    expect(
      parsePutBeatmapSetBody({
        beatmaps_to_create: MAX_DIFFICULTIES_PER_SET + 1,
        target: 'Pending',
      }).ok,
    ).toBe(false);
    expect(
      parsePutBeatmapSetBody({
        beatmaps_to_create: 0,
        beatmaps_to_keep: new Array(MAX_DIFFICULTIES_PER_SET + 1).fill(3),
        target: 'Pending',
      }).ok,
    ).toBe(false);
  });
});

describe('statusAfterSubmission', () => {
  it('starts a new set where the creator put the target', () => {
    expect(statusAfterSubmission(null, 'Pending')).toBe(0);
    expect(statusAfterSubmission(null, 'WIP')).toBe(-1);
  });

  it('lets the creator move between WIP and pending', () => {
    expect(statusAfterSubmission(-1, 'Pending')).toBe(0);
    expect(statusAfterSubmission(0, 'WIP')).toBe(-1);
  });

  it('brings a graveyarded level back to the target, as osu! does', () => {
    expect(statusAfterSubmission(-2, 'Pending')).toBe(0);
    expect(statusAfterSubmission(-2, 'WIP')).toBe(-1);
  });

  it('never changes a ranked or loved level (ranking is the owner\'s call)', () => {
    expect(statusAfterSubmission(1, 'Pending')).toBe(1);
    expect(statusAfterSubmission(2, 'WIP')).toBe(2);
    expect(statusAfterSubmission(4, 'WIP')).toBe(4);
  });
});

describe('unrankedSetLimit', () => {
  it('is the base allowance plus one slot per ranked set, up to the maximum', () => {
    expect(unrankedSetLimit(4, 8, 0)).toBe(4);
    expect(unrankedSetLimit(4, 8, 1)).toBe(5);
    expect(unrankedSetLimit(4, 8, 3)).toBe(7);
    expect(unrankedSetLimit(4, 8, 4)).toBe(8);
    expect(unrankedSetLimit(4, 8, 20)).toBe(8);
    expect(unrankedSetLimit(4, 8, -5)).toBe(4);
  });
});

describe('sha2Hash', () => {
  it('is SHA-256 as lowercase hex, the hash the client diffs packages by', () => {
    // "abc" (FIPS 180-2 test vector)
    expect(sha2Hash(Buffer.from('abc', 'utf8'))).toBe(
      'ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad',
    );
    expect(sha2Hash(Buffer.from('abc', 'utf8'))).toMatch(/^[0-9a-f]{64}$/);
  });
});

describe('isSafeEntryName', () => {
  it('accepts package file names and their subdirectories', () => {
    expect(isSafeEntryName('audio.wav')).toBe(true);
    expect(isSafeEntryName('Fixture Artist - Fixture Song (KairoPhos) [Normal].osu')).toBe(true);
    expect(isSafeEntryName('sb/storyboard.osb')).toBe(true);
  });

  it('refuses traversal, absolute names and stray separators', () => {
    expect(isSafeEntryName('')).toBe(false);
    expect(isSafeEntryName('../secrets.txt')).toBe(false);
    expect(isSafeEntryName('a/../b.osu')).toBe(false);
    expect(isSafeEntryName('/etc/passwd')).toBe(false);
    expect(isSafeEntryName('a\\b.osu')).toBe(false);
    expect(isSafeEntryName('a//b.osu')).toBe(false);
    expect(isSafeEntryName('x'.repeat(256))).toBe(false);
  });
});
