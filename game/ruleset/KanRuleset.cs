// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Threading;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.UI;
using Kan.Ruleset.Beatmaps;
using Kan.Ruleset.Difficulty;
using Kan.Ruleset.UI;

namespace Kan.Ruleset
{
    /// <summary>
    /// The kan! game mode: real-instrument play with dual-axis judgment
    /// (attack timing + intonation). Phase 0 skeleton — this class only proves
    /// the osu!lazer ruleset plugin path and carries product metadata.
    /// Gameplay arrives with the "Input & judgment core" work items in
    /// game/TODO.md (Phase 1).
    /// </summary>
    /// <remarks>
    /// The upstream base type is spelled fully qualified throughout this
    /// project: inside the <c>Kan.Ruleset</c> namespaces the simple name
    /// <c>Ruleset</c> resolves to our own namespace and shadows the upstream
    /// type. Same for <c>osu.Game.Rulesets.RulesetInfo</c> where ambiguous.
    /// </remarks>
    public class KanRuleset : osu.Game.Rulesets.Ruleset
    {
        /// <summary>Product tagline (ADR-0011), shown by the placeholder screen and the about screen.</summary>
        public const string TAGLINE = @"You kan!";

        private static int loadLogged;

        public KanRuleset()
        {
            // One-shot breadcrumb so logs prove the plugin path worked (and so
            // user bug reports show whether their ruleset was picked up).
            if (Interlocked.Exchange(ref loadLogged, 1) == 0)
                Logger.Log(@"kan! ruleset loaded (Phase 0 skeleton — judgment core pending)", level: LogLevel.Important);
        }

        public override string Description => @"kan!";

        public override string ShortName => @"kan";

        public override string PlayingVerb => @"Playing";

        /// <summary>
        /// Ruleset plugins are validated against the fork's API version on load
        /// (see RealmRulesetStore); always point at the current constant.
        /// </summary>
        public override string RulesetAPIVersionSupported => CURRENT_RULESET_API_VERSION;

        public override IEnumerable<Mod> GetModsFor(ModType type) => Array.Empty<Mod>();

        public override IBeatmapConverter CreateBeatmapConverter(IBeatmap beatmap) => new KanBeatmapConverter(beatmap, this);

        public override DifficultyCalculator CreateDifficultyCalculator(IWorkingBeatmap beatmap) => new KanDifficultyCalculator(RulesetInfo, beatmap);

        public override DrawableRuleset CreateDrawableRulesetWith(IBeatmap beatmap, IReadOnlyList<Mod>? mods = null) => new KanDrawableRuleset(this, beatmap, mods);
    }
}
