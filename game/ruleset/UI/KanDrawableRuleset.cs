// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System.Collections.Generic;
using System.Linq;
using osu.Framework.Input;
using osu.Framework.Input.Bindings;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.UI;
using Kan.Ruleset.Beatmaps;
using Kan.Ruleset.Objects;

namespace Kan.Ruleset.UI
{
    /// <summary>
    /// Drawable ruleset for kan!: the score's notes are judged from the
    /// player's live sound (<see cref="KanPlayState"/>) and drawn on the
    /// living staff (<see cref="KanStaff"/>).
    /// </summary>
    public partial class KanDrawableRuleset : DrawableRuleset<KanHitObject>
    {
        private readonly KanPlayState state;

        public KanDrawableRuleset(osu.Game.Rulesets.Ruleset ruleset, IBeatmap beatmap, IReadOnlyList<Mod>? mods = null)
            : base(ruleset, beatmap, mods)
        {
            var notes = Beatmap.HitObjects.Where(h => h.HasScore).OrderBy(h => h.Index).ToList();
            string instrument = notes.Count > 0 ? KanLevelFiles.LoadScore(beatmap.BeatmapInfo.BeatmapSet)?.Instrument ?? @"keyboard" : @"keyboard";
            state = new KanPlayState(notes, instrument);
        }

        public override DrawableHitObject<KanHitObject> CreateDrawableRepresentation(KanHitObject h) => new DrawableKanHitObject(h);

        protected override PassThroughInputManager CreateInputManager() => new KanInputManager(Ruleset.RulesetInfo);

        protected override Playfield CreatePlayfield() => new KanPlayfield(state);
    }

    /// <summary>The player's input is their instrument (heard, not pressed); keys bind nothing yet.</summary>
    public enum KanAction
    {
        Play,
    }

    /// <summary>
    /// A ruleset input manager so gameplay can record a replay as every player
    /// does (the solo player requires it); there are no key bindings.
    /// </summary>
    public partial class KanInputManager : RulesetInputManager<KanAction>
    {
        public KanInputManager(osu.Game.Rulesets.RulesetInfo ruleset)
            : base(ruleset, 0, SimultaneousBindingMode.Unique)
        {
        }
    }
}
