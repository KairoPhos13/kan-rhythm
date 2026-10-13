// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System.Collections.Generic;
using osu.Framework.Input;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.UI;
using Kan.Ruleset.Objects;

namespace Kan.Ruleset.UI
{
    /// <summary>
    /// Drawable ruleset for the kan! placeholder mode. Draws the Phase 0
    /// placeholder screen; the note highway and dual-axis judgment visuals
    /// (ADR-0011) replace it in Phase 1.
    /// </summary>
    public partial class KanDrawableRuleset : DrawableRuleset<KanHitObject>
    {
        public KanDrawableRuleset(osu.Game.Rulesets.Ruleset ruleset, IBeatmap beatmap, IReadOnlyList<Mod>? mods = null)
            : base(ruleset, beatmap, mods)
        {
        }

        public override DrawableHitObject<KanHitObject> CreateDrawableRepresentation(KanHitObject h) => new DrawableKanHitObject(h);

        protected override PassThroughInputManager CreateInputManager() => new PassThroughInputManager();

        protected override Playfield CreatePlayfield() => new KanPlayfield();
    }
}
