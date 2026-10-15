// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Scoring;

namespace Kan.Ruleset.Scoring
{
    /// <summary>
    /// No game over by default (owner 2026-10-14: memorable and repeatable,
    /// not frustrating): a miss is felt in the music, it never ends the level.
    /// </summary>
    public partial class KanHealthProcessor : DrainingHealthProcessor
    {
        public KanHealthProcessor(double drainStartTime)
            : base(drainStartTime)
        {
        }

        protected override bool CheckDefaultFailCondition(JudgementResult result) => false;
    }
}
