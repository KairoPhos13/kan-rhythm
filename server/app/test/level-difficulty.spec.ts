/**
 * The star rating the listing shows: the server's mirror of the game's
 * `LevelDifficulty` (game/core/Score/LevelDifficulty.cs). The expected
 * numbers here mirror `LevelDifficultyTest` in game/core/tests, so the two
 * implementations cannot drift apart unnoticed; the formula is documented in
 * docs/architecture/level-metadata.md.
 */
import {
  difficultyFromInputs,
  difficultyOfNotes,
  scoreNotesFromJson,
  ScoreNote,
} from '../src/beatmaps/level-difficulty';

function note(startMs: number, durationMs: number, midi: number, options: { cents?: number; pitchUnknown?: boolean } = {}): ScoreNote {
  return {
    startMs,
    durationMs,
    midi,
    cents: options.cents ?? 0,
    pitchUnknown: options.pitchUnknown ?? false,
  };
}

describe('difficultyOfNotes', () => {
  it('gives a level without notes no difficulty', () => {
    const none = difficultyOfNotes([]);
    expect(none.stars).toBe(0);
    expect(none.noteCount).toBe(0);
    expect(none.notesPerSecond).toBe(0);
  });

  it('reads one short note as calm, not frantic', () => {
    // The span of a lone note is measured as at least one second of music
    // (MIN_SPAN_MS), so its pace is 1 note per second whatever its duration.
    const one = difficultyOfNotes([note(0, 250, 60)]);
    expect(one.notesPerSecond).toBeCloseTo(1, 9);
    expect(one.pitchSpanSemitones).toBe(0);
    expect(one.stars).toBeCloseTo(0.2 + 1.2, 9);
  });

  it('adds pace and reach each their share', () => {
    // Two notes two seconds apart, an octave apart in pitch.
    const notes = [note(0, 750, 60), note(2000, 750, 72)];
    const difficulty = difficultyOfNotes(notes);

    expect(difficulty.noteCount).toBe(2);
    expect(difficulty.notesPerSecond).toBeCloseTo(2000 / 2750, 9);
    expect(difficulty.pitchSpanSemitones).toBeCloseTo(12, 9);
    expect(difficulty.stars).toBeCloseTo(0.2 + 1.2 * (2000 / 2750) + 0.8, 9);
  });

  it('measures the reach by known pitches only', () => {
    const notes = [note(0, 100, 60), note(1000, 100, 84, { pitchUnknown: true }), note(2000, 100, 65)];
    expect(difficultyOfNotes(notes).pitchSpanSemitones).toBeCloseTo(5, 9);
  });

  it('counts cents towards the reach', () => {
    const notes = [note(0, 100, 60, { cents: -50 }), note(1000, 100, 60, { cents: 50 })];
    expect(difficultyOfNotes(notes).pitchSpanSemitones).toBeCloseTo(1, 9);
  });

  it('reads a level of many fast notes as hard', () => {
    const notes: ScoreNote[] = [];
    for (let i = 0; i < 30; i += 1) notes.push(note(i * 333, 200, 60 + (i % 2) * 24));

    const difficulty = difficultyOfNotes(notes);
    expect(difficulty.notesPerSecond).toBeCloseTo(3, 1);
    expect(difficulty.stars).toBeGreaterThan(4);
    expect(difficulty.stars).toBeLessThan(6);
  });
});

describe('difficultyFromInputs', () => {
  it('computes the same stars from the three inputs', () => {
    const fromInputs = difficultyFromInputs(76, 76960, 12);
    expect(fromInputs.noteCount).toBe(76);
    expect(fromInputs.notesPerSecond).toBeCloseTo((76 * 1000) / 76960, 9);
    expect(fromInputs.stars).toBeCloseTo(0.2 + 1.2 * ((76 * 1000) / 76960) + 0.8, 9);
    expect(difficultyFromInputs(0, 1000, 12).stars).toBe(0);
  });
});

describe('scoreNotesFromJson', () => {
  it('reads the notes of a kan! score', () => {
    const notes = scoreNotesFromJson(
      JSON.stringify({
        Version: 1,
        Instrument: 'ocarina',
        Notes: [
          { StartMs: 100.5, DurationMs: 250, Midi: 72, Cents: -12.5, PitchUnknown: false },
          { StartMs: 400, DurationMs: 250, Midi: 74, Cents: 0, PitchUnknown: true },
        ],
      }),
    );

    expect(notes).not.toBeNull();
    expect(notes).toHaveLength(2);
    expect(notes![0]).toEqual({ startMs: 100.5, durationMs: 250, midi: 72, cents: -12.5, pitchUnknown: false });
    expect(notes![1].pitchUnknown).toBe(true);
  });

  it('returns null for anything that is not a score', () => {
    expect(scoreNotesFromJson('not json')).toBeNull();
    expect(scoreNotesFromJson('{"Version":1}')).toBeNull();
    expect(scoreNotesFromJson('{"Notes":42}')).toBeNull();
    expect(scoreNotesFromJson('[]')).toBeNull();
  });
});
