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
        private static Bindable<bool>? onlyPlayable;

        public static void Bind(KanRulesetConfigManager config)
        {
            instrument ??= config.GetBindable<string>(KanRulesetSetting.PlayerInstrument);
            lowest ??= config.GetBindable<int>(KanRulesetSetting.RangeLowest);
            highest ??= config.GetBindable<int>(KanRulesetSetting.RangeHighest);
            onlyPlayable ??= config.GetBindable<bool>(KanRulesetSetting.OnlyPlayableLevels);

            // The editor greys what the player cannot play (it cannot see these settings).
            RangeFit.PlayerRange = () => Instrument == null ? null : (Lowest, Highest);
        }

        /// <summary>
        /// The instrument profile the player chose, or null: "as the level" —
        /// listen for the level's instrument, play it in its own range (owner
        /// 2026-10-17: the default was "keyboard", so an ocarina player was
        /// heard as a pianist without ever choosing it).
        /// </summary>
        public static string? Instrument => string.IsNullOrEmpty(instrument?.Value) ? null : instrument.Value;

        /// <summary>The choice that means "the level's own instrument".</summary>
        public const string AS_THE_LEVEL = "";

        /// <summary>The choices for "My instrument": as the level, then every profile.</summary>
        public static string[] Choices => new[] { AS_THE_LEVEL }.Concat(Kan.Forge.Pipeline.ForgeJobs.Profiles).ToArray();

        /// <summary>A choice by its name.</summary>
        /// <summary>
        /// A choice by its name, and what it means for listening (owner
        /// 2026-10-17: only ask the kind of instrument — how it attacks, one
        /// note at a time or chords — and its range): "Ocarina · one note at a
        /// time", "Piano / keyboard · chords too".
        /// </summary>
        public static string ChoiceLabel(string choice)
        {
            if (string.IsNullOrEmpty(choice)) return @"As the level";

            string name = Kan.Forge.Pipeline.ForgeJobs.ProfileLabel(choice);
            return $"{char.ToUpperInvariant(name[0])}{name[1..]} · {(Kan.Forge.Score.KanScore.IsMonophonic(choice) ? "one note at a time" : "chords too")}";
        }

        // As the level: every note is in range (nothing moves).
        public static int Lowest => Instrument == null ? 0 : lowest?.Value ?? 21;

        public static int Highest => Instrument == null ? 127 : highest?.Value ?? 108;

        /// <summary>Whether song select lists only the levels the player's instrument can play.</summary>
        public static bool OnlyPlayableLevels => onlyPlayable?.Value ?? false;

        /// <summary>
        /// Whether the player's instrument plays these notes: in its range as
        /// written, or moved by an octave — nothing folded note by note (that
        /// changes the melody). Always, while it is "as the level".
        /// </summary>
        public static bool CanPlay(IReadOnlyCollection<int> midis) => Instrument == null || midis.Count == 0 || FitOf(midis).Folded == 0;

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
