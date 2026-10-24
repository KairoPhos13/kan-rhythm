// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System.Collections.Generic;
using System.Linq;
using osu.Framework.Graphics;
using osu.Framework.Input;
using osu.Framework.Screens;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.StateChanges;
using osu.Game.Beatmaps;
using osu.Game.Input.Handlers;
using osu.Game.Replays;
using osu.Game.Scoring;
using osu.Game.Screens.Play;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.UI;
using Kan.Ruleset.Beatmaps;
using Kan.Ruleset.Objects;
using Kan.Ruleset.Replays;

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

        public KanPlayState State => state;

        private readonly bool listening;
        private osu.Game.Replays.Replay? listen;

        /// <summary>How the level was fitted to the player's instrument range.</summary>
        public Kan.Core.Play.RangeFit.Fit Fit { get; private set; }

        /// <summary>The notes as written, and the range they were fitted to (to fit them again).</summary>
        private readonly List<(KanHitObject Note, int Midi)> written;

        private (string? Instrument, int Lowest, int Highest) fittedTo;
        private bool fitChecked;

        public KanDrawableRuleset(osu.Game.Rulesets.Ruleset ruleset, IBeatmap beatmap, IReadOnlyList<Mod>? mods = null)
            : base(ruleset, beatmap, mods)
        {
            var notes = Beatmap.HitObjects.Where(h => h.HasScore).OrderBy(h => h.Index).ToList();
            var set = beatmap.BeatmapInfo.BeatmapSet as osu.Game.Beatmaps.BeatmapSetInfo;
            var score = notes.Count > 0 ? KanLevelFiles.LoadScore(set) : null;
            string instrument = score?.Instrument ?? @"keyboard";
            var design = KanLevelFiles.DesignFor(set, score?.Notes ?? new List<Kan.Core.Score.ScoreNote>(), beatmap.BeatmapInfo.Length);

            osu.Framework.Logging.Logger.Log($"kan! design ({(design.Generated ? "generated" : "the level's")}, {design.Palette}): "
                                             + string.Join(@", ", design.Sections.Select(s => $"{s.DisplayName}{(s.Climax ? "*" : "")} {s.StartMs / 1000:0.0}-{s.EndMs / 1000:0.0}s")));

            // Any level on any instrument: the notes move by the octave that fits the
            // player's range, the rest fold in; the game shows and expects them so.
            written = notes.Select(n => (n, n.Midi)).ToList();
            fitToPlayer();

            // Listen for what the player plays (their instrument), else what the level is
            // written for — decided when listening starts (KanPlayState), so a choice made
            // in the player loader, after the level was prepared, still counts.

            var practice = Mods.OfType<Kan.Ruleset.Mods.KanModPractice>().FirstOrDefault();
            var practiceOptions = practice?.Begin(Mods);
            // (with Autoplay selected the replay already plays the part)
            listening = practiceOptions is { Listen: true } && !practice!.AutoplaySelected;

            state = new KanPlayState(notes, instrument)
            {
                Design = design,
                SetId = beatmap.BeatmapInfo.BeatmapSet?.ID ?? System.Guid.Empty,
                Ticks = ticks(beatmap, notes),
                // While practising, W decides (it starts from the selected mods).
                WaitForPlayer = practiceOptions?.Wait ?? Mods.Any(m => m is Kan.Ruleset.Mods.KanModWait),
                HideNotes = Mods.Any(m => m is Kan.Ruleset.Mods.KanModHidden),
                SeenForMs = Mods.OfType<Kan.Ruleset.Mods.KanModByHeart>().FirstOrDefault()?.SeenForMs.Value,
                // Listening while practising: the part played for the player, as autoplay does.
                ReplaySource = listening ? () => listen ??= Kan.Ruleset.Mods.KanModAutoplay.Generate(notes) : () => ReplayScore?.Replay,
            };
        }

        /// <summary>The staff's grid: bars, beats, and beats cut as finely as the notes need.</summary>
        private static IReadOnlyList<Kan.Core.Score.Bars.Tick> ticks(osu.Game.Beatmaps.IBeatmap beatmap, List<KanHitObject> notes)
        {
            var tempos = beatmap.ControlPointInfo.TimingPoints.Select(t => new Kan.Core.Score.Bars.Tempo(t.Time, t.BeatLength, t.TimeSignature.Numerator)).ToList();
            double until = System.Math.Max(beatmap.BeatmapInfo.Length, notes.Count > 0 ? notes.Max(n => n.EndMs) + 4000 : 0);

            return Kan.Core.Score.Bars.Ticks(tempos, until, Kan.Core.Score.Bars.DivisionOf(tempos, notes.Select(n => n.StartTime)));
        }

        [osu.Framework.Allocation.BackgroundDependencyLoader(true)]
        private void load(Player? player)
        {
            // Practice has no score to rank: no leaderboard, set
            // before the HUD shows it (the practice bar keeps Tab from toggling it).
            if (player != null && Mods.Any(m => m is Kan.Ruleset.Mods.KanModPractice))
                player.Configuration.ShowLeaderboard = false;
        }

        /// <summary>Fit the written notes to the player's range as it is now (logged when they move).</summary>
        private void fitToPlayer()
        {
            fittedTo = (Kan.Ruleset.Configuration.KanPlayer.Instrument, Kan.Ruleset.Configuration.KanPlayer.Lowest, Kan.Ruleset.Configuration.KanPlayer.Highest);
            Fit = Kan.Ruleset.Configuration.KanPlayer.FitOf(written.Where(w => !w.Note.PitchUnknown).Select(w => w.Midi).ToList());

            foreach (var (note, midi) in written)
                note.Midi = Fit.Changes ? Kan.Ruleset.Configuration.KanPlayer.Apply(midi, Fit) : midi;

            if (Fit.Changes)
                osu.Framework.Logging.Logger.Log($"kan!: fitted to the player's range: {Kan.Ruleset.Configuration.KanPlayer.Describe(Fit)}");
        }

        protected override void Update()
        {
            base.Update();

            // The level was prepared while the player loader showed; an instrument or
            // range set there fits it again as the play starts. A
            // replay or autoplay keeps the fit its frames were made with.
            if (fitChecked || this.FindClosestParent<Player>() is not { } player || !player.IsCurrentScreen()) return;

            fitChecked = true;

            if (ReplayScore != null || Mods.Any(m => m is ModAutoplay)) return;
            if (fittedTo == (Kan.Ruleset.Configuration.KanPlayer.Instrument, Kan.Ruleset.Configuration.KanPlayer.Lowest, Kan.Ruleset.Configuration.KanPlayer.Highest)) return;

            fitToPlayer();
            osu.Framework.Logging.Logger.Log(@"kan!: fitted again: the instrument or range changed in the player loader");
            sayFit();
        }

        private void sayFit()
        {
            if (Fit.Changes && Kan.Ruleset.Configuration.KanPlayer.Describe(Fit) is { } fitted)
            {
                state.Status.Value = $"For your instrument: {fitted.ToLowerInvariant()}.";
                Scheduler.AddDelayed(() =>
                {
                    if (state.Status.Value.StartsWith(@"For your instrument", System.StringComparison.Ordinal)) state.Status.Value = string.Empty;
                }, 6000);
            }
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            // Easy / hard rock: wider or stricter timing windows.
            // Say once how the level was fitted to the player's instrument.
            sayFit();

            // Easy also forgives intonation: up to 75 cents off still counts as the note.
            if (Mods.Any(m => m is Kan.Ruleset.Mods.KanModEasy))
            {
                state.Judge.WindowScale = 1.4;
                state.Judge.PitchToleranceCents = 75;
            }

            if (Mods.Any(m => m is Kan.Ruleset.Mods.KanModHardRock)) state.Judge.WindowScale = 0.7;

            // Autoplay is heard: the perfect player's part plays with the level.
            if (Mods.Any(m => m is ModAutoplay) || listening)
                AddInternal(new Kan.Forge.Play.AutoplayPart(KanLevelFiles.LoadScore, () => KanLevelFiles.ForgeRoot));

            // A replay is heard too: what the player played, when its recording is kept.
            if (ReplayScore?.Replay.Frames.OfType<KanReplayFrame>().FirstOrDefault(f => f.Kind == KanFactKind.Take) is { } take && KanLevelFiles.ForgeRoot is { } root)
                AddInternal(new Kan.Forge.Play.TakePart(root, take.Index));
        }

        /// <summary>A play records what the judge heard (<see cref="KanReplayRecorder"/>).</summary>
        protected override ReplayRecorder CreateReplayRecorder(Score score) => state.Recorder = new KanReplayRecorder(score);

        /// <summary>A replay (or autoplay) is heard from its frames, not the microphone.</summary>
        protected override ReplayInputHandler CreateReplayInputHandler(Replay replay) => new KanReplayInputHandler(replay);

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
