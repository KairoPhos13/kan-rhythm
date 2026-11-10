// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using osu.Framework.Allocation;
using osu.Framework.Graphics.Containers;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Scoring;
using Kan.Forge.Play;
using Kan.Ruleset.UI;
using Kan.Core.Play;

namespace Kan.Ruleset.Objects
{
    /// <summary>
    /// The judging side of one score note. It draws nothing; the staff
    /// (<see cref="KanStaff"/>) draws every note. It asks the live judge for
    /// a verdict each frame and applies it as the game's result.
    /// </summary>
    public partial class DrawableKanHitObject : DrawableHitObject<KanHitObject>
    {
        [Resolved(CanBeNull = true)]
        private KanPlayState? state { get; set; }

        public DrawableKanHitObject(KanHitObject hitObject)
            : base(hitObject)
        {
            // A replay rewound past the decision: the note is undecided again.
            OnRevertResult += (judged, _) =>
            {
                if (judged != this) return;

                resultTime = null;
                state?.Unreport(HitObject);
            };
        }

        private readonly Container<DrawableKanLevelEnd> ends = new Container<DrawableKanLevelEnd>();

        [BackgroundDependencyLoader]
        private void load()
        {
            AddInternal(ends);
        }

        protected override DrawableHitObject CreateNestedHitObject(HitObject hitObject) => hitObject switch
        {
            KanLevelEnd end => new DrawableKanLevelEnd(end),
            _ => base.CreateNestedHitObject(hitObject),
        };

        protected override void AddNestedHitObject(DrawableHitObject hitObject)
        {
            base.AddNestedHitObject(hitObject);
            ends.Add((DrawableKanLevelEnd)hitObject);
        }

        protected override void ClearNestedHitObjects()
        {
            base.ClearNestedHitObjects();
            ends.Clear(false);
        }

        protected override void UpdateHitStateTransforms(ArmedState state)
        {
            base.UpdateHitStateTransforms(state);

            // Judged at its attack, the last note stays alive until its end mark is.
            if (HitObject.EndsTheLevel) LifetimeEnd = Math.Max(HitStateUpdateTime, HitObject.EndMs) + 1;
        }

        /// <summary>A hit's moment: the note's start plus the judge's offset (the attack, not the decision).</summary>
        private double? resultTime;

        protected override double ResultTime => resultTime ?? Time.Current;

        protected override void CheckForResult(bool userTriggered, double timeOffset)
        {
            // A level without a score: timing-only placeholders resolve by themselves.
            if (!HitObject.HasScore || state == null)
            {
                if (timeOffset >= 0)
                    ApplyMaxResult();
                return;
            }

            // "Wait for me": a wrong try is a miss the moment it is confirmed
            //, even while the level waits for the right note.
            if (state.WrongTry(HitObject) is not { } v)
            {
                var verdict = state.Judge.Judge(HitObject.StartTime, HitObject.Midi, HitObject.PitchUnknown, Time.Current,
                    legato: HitObject.Articulation == Kan.Core.Score.Articulation.Legato, expected: state.ExpectedAround(HitObject), paceMs: HitObject.PaceMs,
                    prevStartMs: state.PreviousStart(HitObject), nextStartMs: state.NextStart(HitObject));
                if (verdict is not { } judged) return;

                // The level waits for a note the player has not played yet, so
                // the judge never closes one as missed: it is played, waited for,
                // or tried wrong (the wait's own count of tries, above).
                if (judged.Result == LiveResult.Miss && state.WaitPending(HitObject)) return;

                v = judged;
            }

            // Stamped when the player played it, not when the pitch came in (fork F-17).
            resultTime = v.Result != LiveResult.Miss ? HitObject.StartTime + v.OffsetMs : null;

            ApplyResult(v.Result switch
            {
                LiveResult.Perfect => HitResult.Perfect,
                LiveResult.Good => HitResult.Good,
                LiveResult.Meh => HitResult.Meh,
                _ => HitResult.Miss,
            });

            state.Report(HitObject, v);
        }
    }

    /// <summary>Resolves when the last note's end passes (<see cref="KanLevelEnd"/>).</summary>
    public partial class DrawableKanLevelEnd : DrawableHitObject<KanLevelEnd>
    {
        public DrawableKanLevelEnd(KanLevelEnd end)
            : base(end)
        {
        }

        protected override void CheckForResult(bool userTriggered, double timeOffset)
        {
            if (timeOffset >= 0) ApplyMaxResult();
        }
    }
}
