// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System.Collections.Generic;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;

namespace Kan.Ruleset.Scoring
{
    /// <summary>
    /// Accuracy and rank as in osu! (owner 2026-10-23: "more like osu! than
    /// osu!mania"). A meh is the right note within a semitone and the wide
    /// windows, so it weighs as osu!'s ok (100 of 300), not as its meh (50:
    /// a no-miss run of 25 perfects, 19 goods and 12 mehs was a C at 71 %).
    /// Perfect 300 and good 200 stay. As in osu!, an S or SS needs no miss.
    /// </summary>
    public partial class KanScoreProcessor : ScoreProcessor
    {
        public KanScoreProcessor(KanRuleset ruleset)
            : base(ruleset)
        {
        }

        public override int GetBaseScoreForResult(HitResult result)
            => result == HitResult.Meh ? 100 : base.GetBaseScoreForResult(result);

        public override ScoreRank RankFromScore(double accuracy, IReadOnlyDictionary<HitResult, int> results)
        {
            ScoreRank rank = base.RankFromScore(accuracy, results);

            if (rank is ScoreRank.S or ScoreRank.X && results.GetValueOrDefault(HitResult.Miss) > 0)
                rank = ScoreRank.A;

            return rank;
        }
    }
}
