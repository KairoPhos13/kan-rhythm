// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Kan.Core.Score
{
    /// <summary>
    /// The times a level's osu!-format file carries hit-object marks at.
    ///
    /// The score's notes cannot travel in the .osu (the osu! format has no
    /// pitch or note duration), but the game and the server both read the
    /// level's length and object count from the .osu's hit objects. So the
    /// .osu carries a mark per note (a plain circle at the note's start) and
    /// one mark at the level's end (the last note's end), which is exactly
    /// the span the level is played over. The ruleset replaces the marks with
    /// the real notes when the level is played; kan!forge rewrites them from
    /// the score whenever the score is saved into the level.
    ///
    /// Plain circles (time only) are deliberate: they are the one hit-object
    /// shape that survives the editor's decode-and-encode round trip of a
    /// kan! level untouched. See docs/architecture/level-metadata.md.
    /// </summary>
    public static class LevelMarks
    {
        /// <summary>
        /// The mark times for a score: one per note's start, then the level's
        /// end. Empty for a score without notes (a level without a score keeps
        /// whatever its .osu already carries).
        /// </summary>
        public static IReadOnlyList<double> TimesOf(IReadOnlyList<ScoreNote> notes)
        {
            if (notes == null || notes.Count == 0)
                return Array.Empty<double>();

            var times = notes.Select(n => n.StartMs).OrderBy(t => t).ToList();

            // The level ends when its last note ends: a mark there is what
            // makes the level's length reach the end of the music.
            times.Add(notes.Max(n => n.EndMs));
            return times;
        }
    }
}
