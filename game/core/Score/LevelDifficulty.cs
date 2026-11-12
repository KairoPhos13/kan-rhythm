// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Kan.Core.Score
{
    /// <summary>
    /// How hard a level is to play, as a star rating: a simple, honest model
    /// of two things the player actually meets, how fast the notes come and
    /// how far the melody reaches. No instrument is special-cased (the pitch
    /// span counts semitones, whatever plays them), and nothing here is
    /// hidden: the formula is the constants below, documented in
    /// docs/architecture/level-metadata.md. The server computes the same
    /// numbers for the beatmap listing
    /// (server/app/src/beatmaps/level-difficulty.ts), and the game ruleset
    /// feeds them to its difficulty calculator (game/ruleset).
    ///
    /// This is the model of the moment, not the final one: the `kp`-era
    /// difficulty work (chart features, instrument family, per-mod
    /// attributes) is a later milestone (docs/adr/0012). When the formula
    /// changes, bump <c>KanDifficultyCalculator.Version</c> so levels players
    /// already have are recomputed.
    /// </summary>
    public readonly struct LevelDifficulty
    {
        /// <summary>The stars of a level that has notes but no pace and no reach.</summary>
        public const double BASE_STARS = 0.2;

        /// <summary>Stars added per note per second.</summary>
        public const double PACE_STARS = 1.2;

        /// <summary>Stars added per octave of pitch span.</summary>
        public const double REACH_STARS = 0.8;

        /// <summary>
        /// The shortest span of music a level can be measured over (ms): one
        /// short note must not read as frantic because it is alone.
        /// </summary>
        public const double MIN_SPAN_MS = 1000;

        /// <summary>How many notes the level plays.</summary>
        public int NoteCount { get; }

        /// <summary>Notes per second of music (the level's pace).</summary>
        public double NotesPerSecond { get; }

        /// <summary>From the lowest to the highest note, in semitones (0 when no pitch is known).</summary>
        public double PitchSpanSemitones { get; }

        /// <summary>The pitch span in octaves.</summary>
        public double Octaves => PitchSpanSemitones / 12;

        /// <summary>The star rating.</summary>
        public double Stars { get; }

        private LevelDifficulty(int noteCount, double notesPerSecond, double pitchSpanSemitones)
        {
            NoteCount = noteCount;
            NotesPerSecond = notesPerSecond;
            PitchSpanSemitones = pitchSpanSemitones;
            Stars = noteCount > 0 ? BASE_STARS + PACE_STARS * notesPerSecond + REACH_STARS * Octaves : 0;
        }

        /// <summary>The difficulty of a score's notes. A score without notes has none.</summary>
        public static LevelDifficulty Of(IReadOnlyList<ScoreNote> notes)
        {
            if (notes == null || notes.Count == 0)
                return default;

            double spanMs = notes.Max(n => n.EndMs) - notes.Min(n => n.StartMs);

            // The pitched notes decide the reach; a note whose pitch is only a
            // guess says nothing about how far the melody travels.
            var pitched = notes.Where(n => !n.PitchUnknown).ToList();
            double spanSemitones = pitched.Count == 0 ? 0 : pitched.Max(pitchOf) - pitched.Min(pitchOf);

            return From(notes.Count, spanMs, spanSemitones);
        }

        /// <summary>
        /// The same difficulty from its three inputs, so every side of kan!
        /// (game, server, tools) can compute it from whatever it holds.
        /// </summary>
        /// <param name="noteCount">How many notes the level plays.</param>
        /// <param name="spanMs">From the first note's start to the last note's end.</param>
        /// <param name="pitchSpanSemitones">From the lowest to the highest known pitch.</param>
        public static LevelDifficulty From(int noteCount, double spanMs, double pitchSpanSemitones)
        {
            if (noteCount <= 0)
                return default;

            double measuredMs = Math.Max(spanMs, MIN_SPAN_MS);
            return new LevelDifficulty(noteCount, noteCount * 1000.0 / measuredMs, Math.Max(0, pitchSpanSemitones));
        }

        private static double pitchOf(ScoreNote note) => note.Midi + note.Cents / 100.0;
    }
}
