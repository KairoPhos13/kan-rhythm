// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using osu.Game.Rulesets.Objects.Drawables;

namespace Kan.Ruleset.Objects
{
    /// <summary>
    /// Drawable representation of <see cref="KanHitObject"/> — intentionally
    /// empty in Phase 0 (the placeholder screen is drawn by
    /// <see cref="UI.KanPlayfield"/>).
    /// </summary>
    public partial class DrawableKanHitObject : DrawableHitObject<KanHitObject>
    {
        public DrawableKanHitObject(KanHitObject hitObject)
            : base(hitObject)
        {
        }

        /// <summary>
        /// PHASE 0 PLACEHOLDER — NOT JUDGMENT. The real dual-axis judgment
        /// (instant attack + delayed intonation validation, see game/TODO.md
        /// "Input & judgment core") replaces this in Phase 1. Until then every
        /// object auto-resolves at its end time so the placeholder mode
        /// completes cleanly without player input.
        /// </summary>
        protected override void CheckForResult(bool userTriggered, double timeOffset)
        {
            if (timeOffset >= 0)
                ApplyMaxResult();
        }
    }
}
