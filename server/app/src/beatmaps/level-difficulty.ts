/**
 * Level difficulty: the pace of the notes (notes per second) and how far the
 * melody reaches (pitch span), as a star rating.
 *
 * This is the server's mirror of the game's `LevelDifficulty`
 * (game/core/Score/LevelDifficulty.cs), so the beatmap listing shows the same
 * stars the game computes; the formula is documented in
 * docs/architecture/level-metadata.md. Change both implementations together
 * (and bump the ruleset's `KanDifficultyCalculator.Version`, so levels
 * players already have are recomputed). No instrument is special-cased: the
 * pitch span counts semitones, whatever plays the notes.
 */

/** One note of a kan! score (`kan.score.json`, written by kan!forge). */
export interface ScoreNote {
  startMs: number;
  durationMs: number;
  midi: number;
  cents: number;
  pitchUnknown: boolean;
}

export interface LevelDifficulty {
  /** How many notes the level plays. */
  noteCount: number;
  /** Notes per second of music (the level's pace). */
  notesPerSecond: number;
  /** From the lowest to the highest note, in semitones (0 when no pitch is known). */
  pitchSpanSemitones: number;
  /** The star rating. */
  stars: number;
}

/** The stars of a level that has notes but no pace and no reach. */
export const BASE_STARS = 0.2;

/** Stars added per note per second. */
export const PACE_STARS = 1.2;

/** Stars added per octave of pitch span. */
export const REACH_STARS = 0.8;

/** The shortest span of music a level can be measured over (ms). */
export const MIN_SPAN_MS = 1000;

/**
 * The difficulty of a score's notes. A score without notes has none.
 */
export function difficultyOfNotes(notes: ScoreNote[]): LevelDifficulty {
  if (notes.length === 0) return difficultyFromInputs(0, 0, 0);

  const starts = notes.map((note) => note.startMs);
  const ends = notes.map((note) => note.startMs + note.durationMs);
  const spanMs = Math.max(...ends) - Math.min(...starts);

  // Only known pitches measure the reach: a note whose pitch is just a guess
  // says nothing about how far the melody travels.
  const pitched = notes.filter((note) => !note.pitchUnknown);
  const spanSemitones =
    pitched.length === 0
      ? 0
      : Math.max(...pitched.map(pitchOf)) - Math.min(...pitched.map(pitchOf));

  return difficultyFromInputs(notes.length, spanMs, spanSemitones);
}

/**
 * The same difficulty from its three inputs, so the number can be computed
 * from whatever a caller holds (the server falls back to the .osu's hit
 * objects, which carry no pitch, when a level has no score).
 */
export function difficultyFromInputs(
  noteCount: number,
  spanMs: number,
  pitchSpanSemitones: number,
): LevelDifficulty {
  if (noteCount <= 0) {
    return { noteCount: 0, notesPerSecond: 0, pitchSpanSemitones: 0, stars: 0 };
  }

  const measuredMs = Math.max(spanMs, MIN_SPAN_MS);
  const notesPerSecond = (noteCount * 1000) / measuredMs;
  const reach = Math.max(0, pitchSpanSemitones);

  return {
    noteCount,
    notesPerSecond,
    pitchSpanSemitones: reach,
    stars: BASE_STARS + PACE_STARS * notesPerSecond + REACH_STARS * (reach / 12),
  };
}

/**
 * The notes of a `kan.score.json` (the score that travels inside a level), or
 * null when the text is not a score. Tolerant on purpose: an archive without
 * a readable score falls back to its hit objects.
 */
export function scoreNotesFromJson(text: string): ScoreNote[] | null {
  try {
    const parsed: unknown = JSON.parse(text);
    if (typeof parsed !== 'object' || parsed === null) return null;
    const notes = (parsed as { Notes?: unknown }).Notes;
    if (!Array.isArray(notes)) return null;

    const out: ScoreNote[] = [];
    for (const note of notes) {
      if (typeof note !== 'object' || note === null) return null;
      const raw = note as Record<string, unknown>;
      out.push({
        startMs: numberOf(raw.StartMs),
        durationMs: numberOf(raw.DurationMs),
        midi: numberOf(raw.Midi),
        cents: numberOf(raw.Cents),
        pitchUnknown: raw.PitchUnknown === true,
      });
    }
    return out;
  } catch {
    return null;
  }
}

function pitchOf(note: ScoreNote): number {
  return note.midi + note.cents / 100;
}

function numberOf(value: unknown): number {
  return typeof value === 'number' && Number.isFinite(value) ? value : 0;
}
