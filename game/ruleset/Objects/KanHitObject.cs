// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System.Threading;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Scoring;
using Kan.Core.Score;

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

        /// <summary>The note that sounds last: the level is over when it ends, not when it is judged.</summary>
        public bool EndsTheLevel { get; set; }

        /// <summary>The note's pace (ms, <see cref="Kan.Core.Play.LiveJudge.PaceOf"/>): it sizes the timing windows; 0 = unknown.</summary>
        public double PaceMs { get; set; }

        protected override void CreateNestedHitObjects(CancellationToken cancellationToken)
        {
            base.CreateNestedHitObjects(cancellationToken);
            if (EndsTheLevel) AddNested(new KanLevelEnd { StartTime = EndMs });
        }

        public override Judgement CreateJudgement() => new KanJudgement();

        protected override HitWindows CreateHitWindows() => new KanHitWindows(this);
    }

    /// <summary>
    /// The end of the level's last note. The game shows the results once every
    /// judgement is in, and a note is judged at its attack: without this mark a
    /// long last note would be cut by the results screen. It scores nothing.
    /// </summary>
    public class KanLevelEnd : osu.Game.Rulesets.Objects.HitObject
    {
        public override Judgement CreateJudgement() => new KanLevelEndJudgement();

        protected override HitWindows CreateHitWindows() => HitWindows.Empty;
    }

    public class KanLevelEndJudgement : Judgement
    {
        public override HitResult MaxResult => HitResult.IgnoreHit;
    }

    public class KanJudgement : Judgement
    {
        public override HitResult MaxResult => HitResult.Perfect;
    }

    /// <summary>The judge's windows (<see cref="Kan.Core.Play.LiveJudge"/>), for the results screen.</summary>
    public class KanHitWindows : HitWindows
    {
        private readonly KanHitObject? note;

        public KanHitWindows(KanHitObject? note = null)
        {
            this.note = note;
        }

        private double paceMs => note?.PaceMs ?? 0;

        public override bool IsHitResultAllowed(HitResult result) => result is HitResult.Perfect or HitResult.Good or HitResult.Meh or HitResult.Miss;

        public override void SetDifficulty(double difficulty)
        {
        }

        // The pace's windows, before a neighbour narrows them (the judge does that per note).
        public override double WindowFor(HitResult result) => result switch
        {
            HitResult.Perfect => Kan.Core.Play.LiveJudge.WindowMs(Kan.Core.Play.LiveResult.Perfect, paceMs),
            HitResult.Good => Kan.Core.Play.LiveJudge.WindowMs(Kan.Core.Play.LiveResult.Good, paceMs),
            HitResult.Meh => Kan.Core.Play.LiveJudge.WindowMs(Kan.Core.Play.LiveResult.Meh, paceMs),
            HitResult.Miss => Kan.Core.Play.LiveJudge.WindowMs(Kan.Core.Play.LiveResult.Meh, paceMs) + Kan.Core.Play.LiveJudge.HEARING_MS + Kan.Core.Play.LiveJudge.PITCH_WAIT_MS,
            _ => 0,
        };
    }
}
