// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System.Collections.Generic;
using System.Linq;
using osu.Framework.Bindables;
using Kan.Forge.Play;

namespace Kan.Ruleset.Configuration
{
    /// <summary>
    /// The player's own instrument (owner 2026-10-15: "levels should be
    /// playable on any instrument; the selector tells whether the instrument
    /// has the song's range"): what the game listens for, and the notes it
    /// can play. Bound from the settings when the game starts (the ruleset
    /// icon), so it is known where no dependency injection reaches (the
    /// drawable ruleset's constructor fits the level before autoplay reads it).
    /// </summary>
    public static class KanPlayer
    {
        private static Bindable<string>? instrument;
        private static Bindable<int>? lowest, highest;

        public static void Bind(KanRulesetConfigManager config)
        {
            instrument ??= config.GetBindable<string>(KanRulesetSetting.PlayerInstrument);
            lowest ??= config.GetBindable<int>(KanRulesetSetting.RangeLowest);
            highest ??= config.GetBindable<int>(KanRulesetSetting.RangeHighest);
        }

        /// <summary>The instrument profile the game listens with (null before the settings are known).</summary>
        public static string? Instrument => instrument?.Value;

        public static int Lowest => lowest?.Value ?? 21;

        public static int Highest => highest?.Value ?? 108;

        /// <summary>The octave fit of a level's notes into the player's range.</summary>
        public static RangeFit.Fit FitOf(IReadOnlyCollection<int> midis) => RangeFit.Plan(midis, Lowest, Highest);

        public static int Apply(int midi, RangeFit.Fit fit) => RangeFit.Apply(midi, fit, Lowest, Highest);

        /// <summary>The fit in plain words ("plays an octave lower", "3 notes folded"), or null when the level fits as it is.</summary>
        public static string? Describe(RangeFit.Fit fit)
        {
            if (!fit.Changes) return null;

            var parts = new List<string>();

            if (fit.OctaveShift != 0)
            {
                int n = System.Math.Abs(fit.OctaveShift);
                string octaves = n == 1 ? "an octave" : $"{n} octaves";
                parts.Add($"plays {octaves} {(fit.OctaveShift > 0 ? "higher" : "lower")}");
            }

            if (fit.Folded > 0)
                parts.Add($"{fit.Folded} {(fit.Folded == 1 ? "note" : "notes")} moved by octaves into your range");

            return string.Join(@", ", parts.Select((p, i) => i == 0 ? char.ToUpperInvariant(p[0]) + p[1..] : p));
        }
    }
}
