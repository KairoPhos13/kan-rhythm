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
    // Mods that need nothing from the level editor. The
    // rate mods run the same notes slower or faster, at the level's own
    // pitch: practice at 75 % or 90 %. No kan! mod shifts the pitch (owner
    // 2026-11-10: Daycore and Nightcore, which did, are gone).

    public class KanModNoFail : ModNoFail
    {
        public override LocalisableString Description => @"You cannot fail the level.";
    }

    public class KanModSuddenDeath : ModSuddenDeath
    {
        public override LocalisableString Description => @"One missed note fails the level.";
    }

    public class KanModPerfect : ModPerfect
    {
    }

    public class KanModHalfTime : ModHalfTime
    {
        public override LocalisableString Description => @"Slower, same notes: practise a level at 75 % (or any speed down to 50 %).";
    }

    public class KanModDoubleTime : ModDoubleTime
    {
        public override LocalisableString Description => @"Faster, same notes: the level plays at 150 % (or any speed up to 200 %).";
    }

    public class KanModMuted : ModMuted
    {
    }

    /// <summary>Wider timing windows (×1.4) and pitch tolerance (±75 cents instead of ±50).</summary>
    public class KanModEasy : ModEasy
    {
        public override LocalisableString Description => @"Wider timing windows, and notes up to 75 cents out of tune still count.";
    }

    /// <summary>
    /// Stricter timing windows (×0.7), and nothing else: osu!'s Hard Rock
    /// also raises the level's difficulty values (health drains 40 % faster),
    /// which is not what kan!'s Hard Rock says it does (owner 2026-11-10).
    /// Pitch is untouched: neither the sound nor the pitch tolerance change.
    /// </summary>
    public class KanModHardRock : ModHardRock
    {
        public override LocalisableString Description => @"Stricter timing windows.";

        public override void ApplyToDifficulty(BeatmapDifficulty difficulty)
        {
        }
    }

    /// <summary>Notes fade out on their way to the line: play from memory.</summary>
    public class KanModHidden : ModHidden
    {
        public override LocalisableString Description => @"Notes fade out before they reach the line.";

        public override System.Type[] IncompatibleMods => base.IncompatibleMods.Append(typeof(KanModByHeart)).ToArray();

        // The staff draws every note (KanStaff, KanPlayState.HideNotes); the
        // judging objects draw nothing.
        protected override void ApplyIncreasedVisibilityState(osu.Game.Rulesets.Objects.Drawables.DrawableHitObject hitObject, osu.Game.Rulesets.Objects.Drawables.ArmedState state)
        {
        }

        protected override void ApplyNormalVisibilityState(osu.Game.Rulesets.Objects.Drawables.DrawableHitObject hitObject, osu.Game.Rulesets.Objects.Drawables.ArmedState state)
        {
        }
    }

    /// <summary>
    /// "By heart" (owner 2026-10-23: hidden showed them how much better they
    /// play by ear, "but to learn the songs I need to see the notes less,
    /// to play them practically from memory"): each note shows only for a
    /// moment as it comes into view, then is gone until it is played. How
    /// long it shows is the player's, to shorten as the song is learnt.
    /// </summary>
    public class KanModByHeart : Mod, IApplicableMod // read by the staff (KanDrawableRuleset), as Hidden is
    {
        public override string Name => @"By heart";
        public override string Acronym => @"BH";
        public override LocalisableString Description => @"Each note shows only for a moment as it comes into view: play the song by heart.";
        public override ModType Type => ModType.DifficultyIncrease;
        public override osu.Framework.Graphics.Sprites.IconUsage? Icon => osu.Framework.Graphics.Sprites.FontAwesome.Solid.Brain;
        public override System.Type[] IncompatibleMods => new[] { typeof(KanModHidden) };

        // Ranked as Hidden is, and also seen for less: shortening it as the song is learnt is the point.
        public override bool Ranked => SeenForMs.Value <= SeenForMs.Default;

        [osu.Game.Configuration.SettingSource("Seen for (ms)", "How long each note shows as it comes into view.")]
        public osu.Framework.Bindables.BindableDouble SeenForMs { get; } = new osu.Framework.Bindables.BindableDouble(500)
        {
            MinValue = 150,
            MaxValue = 2000,
            Precision = 50,
        };
    }

    /// <summary>Autoplay without the interface: listen to the level played perfectly.</summary>
    public class KanModCinema : ModCinema<KanHitObject>
    {
        public override LocalisableString Description => @"Listen to the level played perfectly, without the interface.";

        public override ModReplayData CreateReplayData(IBeatmap beatmap, IReadOnlyList<Mod> mods)
            => new ModReplayData(KanModAutoplay.Generate(beatmap.HitObjects.OfType<KanHitObject>()), new ModCreatedUser { Username = @"kan! autoplay" });
    }

    /// <summary>
    /// "Wait for me": the level never runs ahead of the
    /// player; a little past each note (<see cref="KanPlayState.WAIT_GRACE_MS"/>)
    /// it stops until the note is played. A gentle walk for learning: while
    /// it waits, the music is held as a soft sustained sound and the player's pitch shows live
    /// against the note (higher, lower); a wrong note breaks the combo; it
    /// cannot fail, and it scores almost nothing.
    /// </summary>
    public partial class KanModWait : Mod, IApplicableFailOverride, IApplicableToDrawableRuleset<KanHitObject>, IApplicableToHUD
    {
        private Kan.Ruleset.UI.KanDrawableRuleset? ruleset;

        public void ApplyToDrawableRuleset(osu.Game.Rulesets.UI.DrawableRuleset<KanHitObject> drawableRuleset) => ruleset = drawableRuleset as Kan.Ruleset.UI.KanDrawableRuleset;

        // While the level stands still osu! updates nothing inside the ruleset
        // (FrameStabilityContainer), so the listening, the live pitch and the
        // held sound go on from the HUD.
        public void ApplyToHUD(osu.Game.Screens.Play.HUDOverlay overlay)
        {
            var live = new Kan.Ruleset.UI.KanWaitOverlay(() => ruleset);
            overlay.Add(live);
            overlay.Add(new Kan.Forge.Play.WaitHold(() => ruleset?.State is { Waiting: true } s ? s.WaitResumeTime : null, () => live.UserPaused));
        }

        public override string Name => @"Wait for me";
        public override string Acronym => @"WT";
        public override LocalisableString Description => @"The level waits at each note until you play it. A gentle walk for learning: almost no score.";
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
            Single<KanModWait>(0.02); // owner 2026-10-15: passing a level this way is almost symbolic
            Single<KanModPractice>(0); // practice: no score at all
            Single<KanModNoFail>(0.5);
            Single<KanModHalfTime>(m => m.SpeedChange.Value);
            Single<KanModDoubleTime>(1.1);
            Single<KanModEasy>(0.5);
            Single<KanModHardRock>(1.1);
            Single<KanModHidden>(1.06);
            Single<KanModByHeart>(1.12);
        }
    }

    /// <summary>
    /// Autoplay: a perfect performance played back as a replay, every score
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
