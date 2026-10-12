// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Difficulty.Skills;
using osu.Game.Rulesets.Mods;

namespace Kan.Ruleset.Difficulty
{
    /// <summary>
    /// Phase 0 placeholder difficulty calculation: zero skills, zero star
    /// rating. The `kp`-era difficulty model (chart features, instrument
    /// family) is a later milestone; see docs/adr/0012.
    /// </summary>
    public class KanDifficultyCalculator : DifficultyCalculator
    {
        public KanDifficultyCalculator(osu.Game.Rulesets.IRulesetInfo ruleset, IWorkingBeatmap beatmap)
            : base(ruleset, beatmap)
        {
        }

        protected override DifficultyAttributes CreateDifficultyAttributes(IBeatmap beatmap, Mod[] mods, Skill[] skills)
            => new DifficultyAttributes(mods, 0);

        protected override IEnumerable<DifficultyHitObject> CreateDifficultyHitObjects(IBeatmap beatmap, Mod[] mods)
            => Enumerable.Empty<DifficultyHitObject>();

        protected override Skill[] CreateSkills(IBeatmap beatmap, Mod[] mods)
            => Array.Empty<Skill>();
    }
}
