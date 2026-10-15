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

        protected override void CheckForResult(bool userTriggered, double timeOffset)
        {
            // A level without a score: timing-only placeholders resolve by themselves.
            if (!HitObject.HasScore || state == null)
            {
                if (timeOffset >= 0)
                    ApplyMaxResult();
                return;
            }

            var verdict = state.Judge.Judge(HitObject.StartTime, HitObject.Midi, HitObject.PitchUnknown, Time.Current,
                legato: HitObject.Articulation == Kan.Forge.Score.Articulation.Legato);
            if (verdict is not { } v) return;

            ApplyResult(v.Result switch
            {
                LiveResult.Great => HitResult.Great,
                LiveResult.Ok => HitResult.Ok,
                LiveResult.Meh => HitResult.Meh,
                _ => HitResult.Miss,
            });

            state.Report(HitObject, v);
        }
    }
}
