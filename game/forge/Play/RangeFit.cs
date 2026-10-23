// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Kan.Forge.Play
{
    /// <summary>
    /// Any level on any instrument (owner 2026-10-15: "the instrument
    /// selector should mostly tell whether the instrument has the range the
    /// song needs"). The whole level moves by the octave that keeps the most
    /// notes inside the player's range (no move when that is a tie with
    /// staying put); a note still outside is folded in by octaves, as a
    /// player would. The game then shows and expects the fitted notes.
    /// </summary>
    public static class RangeFit
    {
        public readonly record struct Fit(int OctaveShift, int Folded, int Total)
        {
            public bool Changes => OctaveShift != 0 || Folded > 0;
        }

        /// <summary>Default ranges (MIDI, lowest–highest) of the instrument profiles; the player can change them.</summary>
        public static (int Lowest, int Highest) DefaultRange(string profile) => profile switch
        {
            @"ocarina" => (69, 89), // 12-hole alto C: A4–F6
            @"flute" => (60, 96), // C4–C7
            @"recorder" => (72, 98), // soprano: C5–D7
            @"guitar" => (40, 88), // E2–E6
            @"wind_bowed" => (55, 100), // violin: G3–E7
            @"plucked" => (40, 96),
            _ => (21, 108), // keyboard: the piano's 88 keys
        };

        /// <summary>
        /// The player's own range (lowest–highest), or null while their
        /// instrument is "as the level". The ruleset sets it: its settings
        /// hold the player's instrument.
        /// </summary>
        public static Func<(int Lowest, int Highest)?>? PlayerRange { get; set; }

        /// <summary>The notes the player can play: their own range, else the level instrument's.</summary>
        public static (int Lowest, int Highest) Playable(string instrument) => PlayerRange?.Invoke() ?? DefaultRange(instrument);

        public static Fit Plan(IReadOnlyCollection<int> midis, int lowest, int highest)
        {
            if (midis.Count == 0 || highest < lowest) return new Fit(0, 0, midis.Count);

            int best = 0, bestInside = inside(midis, 0, lowest, highest);

            foreach (int octaves in new[] { -1, 1, -2, 2, -3, 3 })
            {
                int count = inside(midis, octaves * 12, lowest, highest);
                if (count > bestInside)
                {
                    best = octaves;
                    bestInside = count;
                }
            }

            return new Fit(best, midis.Count - bestInside, midis.Count);
        }

        /// <summary>A note as the player plays it: moved by the fit's octaves, then folded into the range.</summary>
        public static int Apply(int midi, Fit fit, int lowest, int highest)
        {
            int m = midi + fit.OctaveShift * 12;

            // Folding needs an octave of room; a narrower range keeps the note as it is.
            if (highest - lowest < 11) return m;

            while (m < lowest) m += 12;
            while (m > highest) m -= 12;
            return m;
        }

        private static int inside(IEnumerable<int> midis, int shift, int lowest, int highest)
            => midis.Count(m => m + shift >= lowest && m + shift <= highest);
    }
}
