// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System.Collections.Generic;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Scoring.Legacy;

namespace Kan.Ruleset.Legacy
{
    /// <summary>
    /// Legacy Score V1 simulation for imported osu! content. Placeholder
    /// values for the kan! skeleton: every converted object counts for one
    /// combo. Real Score V1 semantics are not a kan! product goal (our scores
    /// use dual-axis semantics); this exists because
    /// <see cref="ILegacyRuleset"/> requires it for legacy beatmap import.
    /// </summary>
    public class KanLegacyScoreSimulator : ILegacyScoreSimulator
    {
        public LegacyScoreAttributes Simulate(IWorkingBeatmap workingBeatmap, IBeatmap playableBeatmap)
            => new LegacyScoreAttributes
            {
                AccuracyScore = 0,
                ComboScore = 0,
                BonusScoreRatio = 1,
                BonusScore = 0,
                MaxCombo = playableBeatmap.HitObjects.Count,
            };

        public double GetLegacyScoreMultiplier(IReadOnlyList<Mod> mods, LegacyBeatmapConversionDifficultyInfo difficulty)
            => 1.0;
    }
}
