// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Bindables;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Beatmaps;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Overlays;
using osu.Game.Screens.Play.PlayerSettings;
using osuTK;
using osuTK.Graphics;
using Kan.Forge.Play;
using Kan.Forge.Tracks;
using Kan.Ruleset.Beatmaps;
using Kan.Ruleset.Configuration;

namespace Kan.Ruleset.UI
{
    /// <summary>
    /// The player loader's "Microphone" group (owner 2026-10-17: the mic delay
    /// belongs to the player, not to a song — it was in compose — so it is set
    /// right before playing): the delay, a measurement on clicks with a ball
    /// coming to the ring (<see cref="MicTimingOverlay"/>), and, after a
    /// play, how late its notes landed, to correct the delay by, as osu!'s
    /// offset does.
    /// </summary>
    public partial class KanMicSettings : PlayerSettingsGroup
    {
        [Resolved]
        private osu.Game.OsuGame? game { get; set; }

        [Resolved]
        private IBindable<WorkingBeatmap> beatmap { get; set; } = null!;

        private MicTimingOverlay? overlay;
        private OsuTextFlowContainer lastPlay = null!;
        private RoundedButton correct = null!;

        public KanMicSettings()
            : base(@"Microphone")
        {
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            if (KanLevelFiles.ForgeRoot is { } root)
                ForgeTiming.Load(root);

            Children = new Drawable[]
            {
                new PlayerSliderBar<double>
                {
                    LabelText = @"Mic delay (ms)",
                    Current = ForgeTiming.MIC_DELAY_MS,
                    KeyboardStep = 1,
                },
                new RoundedButton
                {
                    RelativeSizeAxes = Axes.X,
                    Height = 32,
                    Text = @"Measure it on clicks",
                    Action = measure,
                },
                lastPlay = new OsuTextFlowContainer(t => t.Font = OsuFont.GetFont(size: 13))
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                },
                correct = new RoundedButton
                {
                    RelativeSizeAxes = Axes.X,
                    Height = 32,
                    Action = correctByLastPlay,
                },
            };

            showLastPlay();
        }

        /// <summary>The instrument the play will listen for (the player's, else the level's).</summary>
        private string instrument => KanPlayer.Instrument ?? KanLevelFiles.LoadScore(beatmap.Value.BeatmapSetInfo)?.Instrument ?? @"keyboard";

        private void measure()
        {
            if (game == null) return;

            if (overlay == null)
            {
                // Over everything (a focused overlay holds the player loader while it is open).
                overlay = new MicTimingOverlay(() => instrument);
                game.Add(overlay);
            }

            overlay.Show();
        }

        /// <summary>Developer tooling: open the measurement and start it.</summary>
        internal void DevMeasure()
        {
            measure();
            Scheduler.AddDelayed(() => overlay?.DevStart(), 1000);
        }

        /// <summary>A play's notes land late by the delay missing: the one it was played with, plus how late they landed.</summary>
        private double? corrected => KanPlayState.LastPlay is { } p ? Math.Round(p.MicDelayMs + p.MedianMs) : null;

        private void showLastPlay()
        {
            if (KanPlayState.LastPlay is not { } p)
            {
                lastPlay.Text = @"After a play, its timing shows here.";
                correct.Alpha = 0;
                return;
            }

            double late = Math.Round(p.MedianMs);
            lastPlay.Text = Math.Abs(late) < 8
                ? $"Your last play was on time ({late:+0;-0;0} ms over {p.Notes} notes)."
                : $"Your last play landed {Math.Abs(late):0} ms {(late > 0 ? "late" : "early")} on average ({p.Notes} notes). If you played it as you meant to, the mic delay is off by that much.";
            correct.Text = $"Set the mic delay to {corrected:0} ms";
            correct.Alpha = Math.Abs(late) < 8 || corrected == ForgeTiming.MIC_DELAY_MS.Value ? 0 : 1;
        }

        private void correctByLastPlay()
        {
            if (corrected is not { } ms) return;

            ForgeTiming.SetCalibratedMicDelay(ms, instrument);
            showLastPlay();
        }

        protected override void Dispose(bool isDisposing)
        {
            // Leaving the loader: the measurement goes with it.
            overlay?.Expire();
            base.Dispose(isDisposing);
        }
    }
}
