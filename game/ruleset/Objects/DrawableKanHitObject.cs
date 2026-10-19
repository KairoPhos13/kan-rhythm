// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using osu.Framework.Allocation;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Scoring;
using Kan.Forge.Play;
using Kan.Ruleset.UI;

namespace Kan.Ruleset.Objects
{
    /// <summary>
    /// The judging side of one score note. It draws nothing — the staff
    /// (<see cref="KanStaff"/>) draws every note — it asks the live judge for
    /// a verdict each frame and applies it as the game's result.
    /// </summary>
    public partial class DrawableKanHitObject : DrawableHitObject<KanHitObject>
    {
        [Resolved(CanBeNull = true)]
        private KanPlayState? state { get; set; }

        public DrawableKanHitObject(KanHitObject hitObject)
            : base(hitObject)
        {
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
            // (owner 2026-10-17), even while the level waits for the right note.
            if (state.WrongTry(HitObject) is not { } v)
            {
                var verdict = state.Judge.Judge(HitObject.StartTime, HitObject.Midi, HitObject.PitchUnknown, Time.Current,
                    legato: HitObject.Articulation == Kan.Forge.Score.Articulation.Legato, expected: state.ExpectedAround(HitObject));
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
                LiveResult.Great => HitResult.Great,
                LiveResult.Good => HitResult.Good,
                LiveResult.Ok => HitResult.Ok,
                LiveResult.Meh => HitResult.Meh,
                _ => HitResult.Miss,
            });

            state.Report(HitObject, v);
        }
    }
}
