// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Scoring;
using Kan.Forge.Score;

namespace Kan.Ruleset.Objects
{
    /// <summary>
    /// One note of the level's score (kan.score.json, written in the compose
    /// tab): what the player plays. Judged at its start (timing + the right
    /// note); its duration and playing style are shown, not judged yet.
    /// Levels without a score keep timing-only placeholder objects
    /// (<see cref="HasScore"/> false) that resolve by themselves.
    /// </summary>
    public class KanHitObject : osu.Game.Rulesets.Objects.HitObject
    {
        /// <summary>False for the placeholder objects of a level without a score.</summary>
        public bool HasScore { get; set; }

        public int Midi { get; set; } = 60;

        public float Cents { get; set; }

        public double DurationMs { get; set; } = 250;

        public float Intensity { get; set; } = 0.6f;

        public Articulation Articulation { get; set; }

        public bool Vibrato { get; set; }

        /// <summary>The score does not know this note's pitch: any note on time counts.</summary>
        public bool PitchUnknown { get; set; }

        /// <summary>Position in the score (0-based), shared by the judge and the staff.</summary>
        public int Index { get; set; }

        public double EndMs => StartTime + DurationMs;

        public override Judgement CreateJudgement() => new KanJudgement();

        protected override HitWindows CreateHitWindows() => new KanHitWindows();
    }

    public class KanJudgement : Judgement
    {
        public override HitResult MaxResult => HitResult.Perfect;
    }

    /// <summary>The judge's windows (<see cref="Kan.Forge.Play.LiveJudge"/>), for the results screen.</summary>
    public class KanHitWindows : HitWindows
    {
        public override bool IsHitResultAllowed(HitResult result) => result is HitResult.Perfect or HitResult.Great or HitResult.Good or HitResult.Ok or HitResult.Meh or HitResult.Miss;

        public override void SetDifficulty(double difficulty)
        {
        }

        public override double WindowFor(HitResult result) => result switch
        {
            HitResult.Perfect => Kan.Forge.Play.LiveJudge.PERFECT_MS,
            HitResult.Great => Kan.Forge.Play.LiveJudge.GREAT_MS,
            HitResult.Good => Kan.Forge.Play.LiveJudge.GOOD_MS,
            HitResult.Ok => Kan.Forge.Play.LiveJudge.OK_MS,
            HitResult.Meh => Kan.Forge.Play.LiveJudge.MEH_MS,
            HitResult.Miss => Kan.Forge.Play.LiveJudge.MEH_MS + Kan.Forge.Play.LiveJudge.PITCH_WAIT_MS,
            _ => 0,
        };
    }
}
