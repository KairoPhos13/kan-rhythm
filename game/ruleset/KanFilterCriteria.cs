// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Bindables;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Filter;
using osu.Game.Rulesets.Mods;
using osu.Game.Screens.Select;
using osu.Game.Screens.Select.Filter;
using Kan.Ruleset.Beatmaps;
using Kan.Ruleset.Configuration;

namespace Kan.Ruleset
{
    /// <summary>
    /// Song select lists the levels the player's instrument can play (owner
    /// 2026-10-17: "ocarinas come in many kinds… ideally it filters the levels
    /// shown"): a level's notes must sit in the instrument's range, as written
    /// or an octave away (<see cref="KanPlayer.CanPlay"/>). Off while the
    /// instrument is "as the level", or when the player turns it off
    /// (Settings → kan! → Only levels my instrument plays). Search "fits=no"
    /// lists the others.
    /// </summary>
    public class KanFilterCriteria : IRulesetFilterCriteria
    {
        /// <summary>A level's pitched notes, by its set's hash (a saved level gets a new one).</summary>
        private static readonly ConcurrentDictionary<string, int[]> notes_of = new ConcurrentDictionary<string, int[]>();

        private bool? fits;

        public bool Matches(BeatmapInfo beatmapInfo, FilterCriteria criteria)
        {
            if (beatmapInfo.Ruleset.ShortName != @"kan") return true;

            bool want = fits ?? true;
            if (fits == null && !KanPlayer.OnlyPlayableLevels) return true;

            bool shown = canPlay(beatmapInfo) == want;

            if (Dev.KanDevPlay.Enabled)
                osu.Framework.Logging.Logger.Log($"kan! dev filter: {beatmapInfo.DifficultyName} {(shown ? "shown" : "hidden")} (range {KanPlayer.Lowest}-{KanPlayer.Highest})");

            return shown;
        }

        private static bool canPlay(BeatmapInfo beatmapInfo)
        {
            if (beatmapInfo.BeatmapSet is not BeatmapSetInfo set) return true;

            int[] midis = notes_of.GetOrAdd(set.Hash ?? set.ID.ToString(), _ =>
                KanLevelFiles.LoadScore(set)?.Notes.Where(n => !n.PitchUnknown).Select(n => n.Midi).ToArray() ?? System.Array.Empty<int>());

            return KanPlayer.CanPlay(midis);
        }

        /// <summary>"fits=yes" / "fits=no": the levels the instrument plays, or the others.</summary>
        public bool TryParseCustomKeywordCriteria(string key, Operator op, string value)
        {
            if (key != @"fits" || op != Operator.Equal) return false;

            fits = value is @"yes" or @"y" or @"true" or @"1";
            return true;
        }

        public bool FilterMayChangeFromMods(FilterCriteria criteria, ValueChangedEvent<IReadOnlyList<Mod>> mods) => false;
    }
}
