// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System.Collections.Generic;
using System.Linq;
using osu.Framework.Input;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.StateChanges;
using osu.Game.Beatmaps;
using osu.Game.Input.Handlers;
using osu.Game.Replays;
using osu.Game.Scoring;
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
        public Kan.Forge.Play.RangeFit.Fit Fit { get; }

        public KanDrawableRuleset(osu.Game.Rulesets.Ruleset ruleset, IBeatmap beatmap, IReadOnlyList<Mod>? mods = null)
            : base(ruleset, beatmap, mods)
        {
            var notes = Beatmap.HitObjects.Where(h => h.HasScore).OrderBy(h => h.Index).ToList();
            var set = beatmap.BeatmapInfo.BeatmapSet as osu.Game.Beatmaps.BeatmapSetInfo;
            var score = notes.Count > 0 ? KanLevelFiles.LoadScore(set) : null;
            string instrument = score?.Instrument ?? @"keyboard";
            var design = KanLevelFiles.DesignFor(set, score?.Notes ?? new List<Kan.Forge.Score.ScoreNote>(), beatmap.BeatmapInfo.Length);

            osu.Framework.Logging.Logger.Log($"kan! design ({(design.Generated ? "generated" : "the level's")}, {design.Palette}): "
                                             + string.Join(@", ", design.Sections.Select(s => $"{s.DisplayName}{(s.Climax ? "*" : "")} {s.StartMs / 1000:0.0}-{s.EndMs / 1000:0.0}s")));

            // Any level on any instrument: the notes move by the octave that fits the
            // player's range, the rest fold in; the game shows and expects them so.
            Fit = Kan.Ruleset.Configuration.KanPlayer.FitOf(notes.Where(n => !n.PitchUnknown).Select(n => n.Midi).ToList());

            if (Fit.Changes)
            {
                foreach (var n in notes)
                    n.Midi = Kan.Ruleset.Configuration.KanPlayer.Apply(n.Midi, Fit);

                osu.Framework.Logging.Logger.Log($"kan!: fitted to the player's range: {Kan.Ruleset.Configuration.KanPlayer.Describe(Fit)}");
            }

            // Listen for what the player plays (their instrument), else what the level is written for.
            instrument = Kan.Ruleset.Configuration.KanPlayer.Instrument ?? instrument;

            bool practice = Mods.Any(m => m is Kan.Ruleset.Mods.KanModPractice);
            listening = practice && Kan.Ruleset.Mods.KanModPractice.Options.Listen;

            state = new KanPlayState(notes, instrument)
            {
                Design = design,
                WaitForPlayer = Mods.Any(m => m is Kan.Ruleset.Mods.KanModWait) || (practice && Kan.Ruleset.Mods.KanModPractice.Options.Wait),
                HideNotes = Mods.Any(m => m is Kan.Ruleset.Mods.KanModHidden),
                // Listening while practising: the part played for the player, as autoplay does.
                ReplaySource = listening ? () => listen ??= Kan.Ruleset.Mods.KanModAutoplay.Generate(notes) : () => ReplayScore?.Replay,
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            // Easy / hard rock: wider or stricter timing windows.
            // Say once how the level was fitted to the player's instrument.
            if (Fit.Changes && Kan.Ruleset.Configuration.KanPlayer.Describe(Fit) is { } fitted)
            {
                state.Status.Value = $"For your instrument: {fitted.ToLowerInvariant()}.";
                Scheduler.AddDelayed(() =>
                {
                    if (state.Status.Value.StartsWith(@"For your instrument", System.StringComparison.Ordinal)) state.Status.Value = string.Empty;
                }, 6000);
            }

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
