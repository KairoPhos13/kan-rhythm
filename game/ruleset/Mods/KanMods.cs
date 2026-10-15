// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System.Collections.Generic;
using System.Linq;
using osu.Framework.Localisation;
using osu.Game.Beatmaps;
using osu.Game.Replays;
using osu.Game.Rulesets.Mods;
using Kan.Ruleset.Objects;
using Kan.Ruleset.Replays;

namespace Kan.Ruleset.Mods
{
    // Mods that need nothing from the level editor (owner 2026-10-15). The
    // rate mods keep the pitch (the player plays the same notes, slower or
    // faster): practice at 75 % or 90 %.

    public class KanModNoFail : ModNoFail
    {
    }

    public class KanModSuddenDeath : ModSuddenDeath
    {
    }

    public class KanModPerfect : ModPerfect
    {
    }

    public class KanModHalfTime : ModHalfTime
    {
        public override LocalisableString Description => @"Slower, same notes: practise a level at 75 % (or any speed down to 50 %).";
    }

    public class KanModDaycore : ModDaycore
    {
    }

    public class KanModDoubleTime : ModDoubleTime
    {
    }

    public class KanModMuted : ModMuted
    {
    }

    public class KanModNightcore : ModNightcore<KanHitObject>
    {
    }

    /// <summary>Wider timing windows (×1.4).</summary>
    public class KanModEasy : ModEasy
    {
        public override LocalisableString Description => @"Wider timing windows.";
    }

    /// <summary>Stricter timing windows (×0.7).</summary>
    public class KanModHardRock : ModHardRock
    {
        public override LocalisableString Description => @"Stricter timing windows.";
    }

    /// <summary>Notes fade out on their way to the line: play from memory.</summary>
    public class KanModHidden : ModHidden
    {
        public override LocalisableString Description => @"Notes fade out before they reach the line.";

        // The staff draws every note (KanStaff, KanPlayState.HideNotes); the
        // judging objects draw nothing.
        protected override void ApplyIncreasedVisibilityState(osu.Game.Rulesets.Objects.Drawables.DrawableHitObject hitObject, osu.Game.Rulesets.Objects.Drawables.ArmedState state)
        {
        }

        protected override void ApplyNormalVisibilityState(osu.Game.Rulesets.Objects.Drawables.DrawableHitObject hitObject, osu.Game.Rulesets.Objects.Drawables.ArmedState state)
        {
        }
    }

    /// <summary>Autoplay without the interface: listen to the level played perfectly.</summary>
    public class KanModCinema : ModCinema<KanHitObject>
    {
        public override ModReplayData CreateReplayData(IBeatmap beatmap, IReadOnlyList<Mod> mods)
            => new ModReplayData(KanModAutoplay.Generate(beatmap.HitObjects.OfType<KanHitObject>()), new ModCreatedUser { Username = @"kan! autoplay" });
    }

    /// <summary>
    /// "Wait for me" (owner 2026-10-15): the level never runs ahead of the
    /// player; it stops at each note until it is played. For learning: it
    /// scores very little, and it cannot fail.
    /// </summary>
    public partial class KanModWait : Mod, IApplicableFailOverride, IApplicableToDrawableRuleset<KanHitObject>, IApplicableToHUD
    {
        private Kan.Ruleset.UI.KanDrawableRuleset? ruleset;

        public void ApplyToDrawableRuleset(osu.Game.Rulesets.UI.DrawableRuleset<KanHitObject> drawableRuleset) => ruleset = drawableRuleset as Kan.Ruleset.UI.KanDrawableRuleset;

        // While the level stands still osu! updates nothing inside the ruleset
        // (FrameStabilityContainer), so the listening goes on from the HUD.
        public void ApplyToHUD(osu.Game.Screens.Play.HUDOverlay overlay) => overlay.Add(new WaitListener(() => ruleset?.State));

        private partial class WaitListener : osu.Framework.Graphics.Component
        {
            private readonly System.Func<Kan.Ruleset.UI.KanPlayState?> state;

            public WaitListener(System.Func<Kan.Ruleset.UI.KanPlayState?> state) => this.state = state;

            protected override void Update()
            {
                base.Update();
                state()?.ListenWhileWaiting();
            }
        }

        public override string Name => @"Wait for me";
        public override string Acronym => @"WT";
        public override LocalisableString Description => @"The level stops at each note until you play it. For learning.";
        public override ModType Type => ModType.DifficultyReduction;
        public override osu.Framework.Graphics.Sprites.IconUsage? Icon => osu.Framework.Graphics.Sprites.FontAwesome.Solid.HourglassHalf;
        public override System.Type[] IncompatibleMods => new[] { typeof(ModAutoplay), typeof(ModFailCondition), typeof(ModNoFail) };
        public override bool ValidForMultiplayer => false;
        public override bool ValidForMultiplayerAsFreeMod => false;

        public bool PerformFail() => false;
        public bool RestartOnFail => false;
    }

    /// <summary>Score multipliers: practice aids score less (owner: waiting "gives very few points").</summary>
    public class KanScoreMultiplierCalculator : osu.Game.Rulesets.Scoring.ScoreMultiplierCalculator
    {
        public KanScoreMultiplierCalculator(osu.Game.Rulesets.Scoring.ScoreMultiplierContext context)
            : base(context)
        {
            Single<KanModWait>(0.1);
            Single<KanModNoFail>(0.5);
            Single<KanModHalfTime>(m => m.SpeedChange.Value);
            Single<KanModDaycore>(m => m.SpeedChange.Value);
            Single<KanModDoubleTime>(1.1);
            Single<KanModNightcore>(1.1);
            Single<KanModEasy>(0.5);
            Single<KanModHardRock>(1.1);
            Single<KanModHidden>(1.06);
        }
    }

    /// <summary>
    /// Autoplay: a perfect performance played back as a replay — every score
    /// note heard at its start with its pitch, its pitch read along it.
    /// </summary>
    public class KanModAutoplay : ModAutoplay
    {
        public override LocalisableString Description => @"Watch the level played perfectly.";

        public override ModReplayData CreateReplayData(IBeatmap beatmap, IReadOnlyList<Mod> mods)
            => new ModReplayData(Generate(beatmap.HitObjects.OfType<KanHitObject>()), new ModCreatedUser { Username = @"kan! autoplay" });

        /// <summary>The facts a perfect player would produce, in time order.</summary>
        public static Replay Generate(IEnumerable<KanHitObject> notes)
        {
            var frames = new List<KanReplayFrame>();
            int index = 0;

            foreach (var n in notes.Where(n => n.HasScore).OrderBy(n => n.StartTime))
            {
                frames.Add(new KanReplayFrame(n.StartTime, KanFactKind.Hit, index));
                frames.Add(new KanReplayFrame(n.StartTime, KanFactKind.PitchFinal, index, n.Midi, n.Cents));

                for (double t = n.StartTime; t < n.EndMs; t += 10)
                    frames.Add(new KanReplayFrame(t, KanFactKind.Reading, midi: n.Midi + n.Cents / 100f, level: 0.8f));

                index++;
            }

            var replay = new Replay();
            replay.Frames.AddRange(frames.OrderBy(f => f.Time)); // stable: a hit stays before its pitch
            return replay;
        }
    }
}
