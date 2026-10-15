// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using osu.Game.Rulesets.Scoring;

namespace Kan.Ruleset.Scoring
{
    /// <summary>
    /// Health as in osu!: misses drain it and an empty bar ends the level
    /// (owner 2026-10-15: "the player doesn't lose by playing wrong" — it was
    /// no-fail before, 2026-10-14). The "no fail" and "wait for me" mods keep
    /// the level going.
    /// </summary>
    public partial class KanHealthProcessor : DrainingHealthProcessor
    {
        public KanHealthProcessor(double drainStartTime)
            : base(drainStartTime)
        {
        }
    }
}
