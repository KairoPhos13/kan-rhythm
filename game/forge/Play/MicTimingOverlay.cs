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
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterfaceV2;
using osuTK;
using osuTK.Graphics;
using Kan.Forge.Tracks;

namespace Kan.Forge.Play
{
    /// <summary>
    /// Measures the mic delay (<see cref="MicCalibrator"/>): four count-in
    /// clicks, then eight to play a short note on. A ball comes to the ring
    /// on each click (owner 2026-10-17: "a ball approaching the circle where
    /// the sound must be played, besides the sound, so the player sees the
    /// exact moment to attack"); each answer shows as a mark beside the ring.
    /// Opened before playing (the player loader's Microphone group) and from
    /// compose (section recordings use the same delay). While it is open the
    /// level does not start (a focused overlay holds the player loader), and
    /// the song is silent.
    /// </summary>
    public partial class MicTimingOverlay : OsuFocusedOverlayContainer
    {
        private const float lane_width = 460;
        private const float ring_x = 360;
        private const float ball_size = 26;

        /// <summary>How long a ball takes to come to the ring (ms).</summary>
        private const double approach_ms = MicCalibrator.INTERVAL_MS * 2;

        private readonly Func<string> instrument;

        [Resolved]
        private AudioManager audio { get; set; } = null!;

        [Resolved]
        private osu.Game.Overlays.MusicController music { get; set; } = null!;

        private MicCalibrator? calibrator;
        private readonly Circle[] balls = new Circle[MicCalibrator.COUNT_IN + MicCalibrator.PLAY_CLICKS];
        private readonly Container marks;
        private int marked;
        private readonly CircularContainer ring;
        private readonly OsuTextFlowContainer text;
        private readonly RoundedButton start, useIt;
        private readonly BindableDouble silence = new BindableDouble(0);
        private osu.Framework.Graphics.Audio.DrawableTrack? silenced;

        protected override bool BlockNonPositionalInput => true;

        protected override bool DimMainContent => true;

        public MicTimingOverlay(Func<string> instrument)
        {
            this.instrument = instrument;
            RelativeSizeAxes = Axes.Both;

            Container lane;

            Child = new Container
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Width = lane_width + 40,
                AutoSizeAxes = Axes.Y,
                Masking = true,
                CornerRadius = 10,
                Children = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both, Colour = new Color4(0.09f, 0.08f, 0.13f, 0.97f) },
                    new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Vertical,
                        Padding = new MarginPadding(20),
                        Spacing = new Vector2(0, 14),
                        Children = new Drawable[]
                        {
                            new OsuSpriteText { Text = @"Microphone timing", Font = OsuFont.GetFont(size: 22, weight: FontWeight.Bold) },
                            text = new OsuTextFlowContainer(t => t.Font = OsuFont.GetFont(size: 15))
                            {
                                RelativeSizeAxes = Axes.X,
                                AutoSizeAxes = Axes.Y,
                            },
                            lane = new Container
                            {
                                Width = lane_width,
                                Height = 70,
                                Masking = true,
                                CornerRadius = 6,
                                Children = new Drawable[]
                                {
                                    new Box { RelativeSizeAxes = Axes.Both, Colour = Color4.White.Opacity(0.05f) },
                                    new Box { Anchor = Anchor.CentreLeft, Origin = Anchor.CentreLeft, RelativeSizeAxes = Axes.X, Height = 2, Colour = Color4.White.Opacity(0.15f) },
                                    marks = new Container { RelativeSizeAxes = Axes.Both },
                                    ring = new CircularContainer
                                    {
                                        Anchor = Anchor.CentreLeft,
                                        Origin = Anchor.Centre,
                                        X = ring_x,
                                        Size = new Vector2(ball_size + 18),
                                        Masking = true,
                                        BorderThickness = 3,
                                        BorderColour = Color4.White,
                                        Child = new Box { RelativeSizeAxes = Axes.Both, Alpha = 0, AlwaysPresent = true },
                                    },
                                },
                            },
                            new FillFlowContainer
                            {
                                AutoSizeAxes = Axes.Both,
                                Direction = FillDirection.Horizontal,
                                Spacing = new Vector2(10, 0),
                                Children = new Drawable[]
                                {
                                    start = new RoundedButton { Width = 140, Height = 34, Text = @"Start", Action = begin },
                                    useIt = new RoundedButton { Width = 160, Height = 34, Text = @"Use it", Action = apply, Alpha = 0 },
                                    new RoundedButton { Width = 100, Height = 34, Text = @"Close", Action = Hide },
                                },
                            },
                        },
                    },
                },
            };

            for (int i = 0; i < balls.Length; i++)
            {
                lane.Add(balls[i] = new Circle
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.Centre,
                    Size = new Vector2(ball_size),
                    Alpha = 0,
                    Colour = i < MicCalibrator.COUNT_IN ? Color4.White.Opacity(0.45f) : new Color4(1f, 0.82f, 0.31f, 1),
                });
            }

            // The ring above the balls.
            lane.ChangeChildDepth(ring, -1);
        }

        /// <summary>Developer tooling: start measuring (as the Start button).</summary>
        public void DevStart()
        {
            begin();
            Scheduler.AddDelayed(() => osu.Framework.Logging.Logger.Log($"kan! dev play: mic timing, song volume {music.CurrentTrack.AggregateVolume.Value:0.00} (running {music.CurrentTrack.IsRunning})",
                level: osu.Framework.Logging.LogLevel.Important), 2000);
        }

        private void intro()
        {
            text.Text = $"How long your microphone takes, measured the way a level hears you. "
                        + $"Four clicks count in, then play one short note on each of the next eight, "
                        + $"as the ball reaches the ring, with the click. Now: {ForgeTiming.MIC_DELAY_MS.Value:0} ms.";
            start.Text = @"Start";
            start.Enabled.Value = true;
            useIt.Alpha = 0;
        }

        private void begin()
        {
            calibrator?.Dispose();
            marks.Clear();
            marked = 0;
            currentDelayMs = ForgeTiming.MIC_DELAY_MS.Value;
            calibrator = new MicCalibrator(MicCalibrator.DevInput?.Invoke());

            if (!calibrator.Start(audio))
            {
                text.Text = $"Could not start: {calibrator.Error}.";
                return;
            }

            start.Enabled.Value = false;
            useIt.Alpha = 0;
            text.Text = @"Get ready…";
        }

        protected override void Update()
        {
            base.Update();

            // The song about to start is silent while this is open,
            // whichever track is playing now.
            if (State.Value == Visibility.Visible && !ReferenceEquals(silenced, music.CurrentTrack))
            {
                silenced?.RemoveAdjustment(AdjustableProperty.Volume, silence);
                silenced = music.CurrentTrack;
                silenced.AddAdjustment(AdjustableProperty.Volume, silence);
            }

            if (calibrator == null) return;

            calibrator.Update(instrument());
            double now = calibrator.TimeMs;

            // Each click's ball comes from the left and reaches the ring on its click.
            for (int i = 0; i < balls.Length; i++)
            {
                double until = MicCalibrator.Clicks[i] - now;
                bool coming = calibrator.State == MicCalibrator.Phase.Running && until <= approach_ms && until > -250;

                balls[i].Alpha = coming ? (float)Math.Clamp(1 - until / approach_ms, 0, 1) * (until < 0 ? (float)(1 + until / 250) : 1) : 0;
                balls[i].X = ring_x - (float)(until / approach_ms) * ring_x;
                balls[i].Scale = new Vector2(until < 0 ? 1 + (float)(-until / 250) * 0.6f : 1);
            }

            // A click: the ring pulses.
            double sinceClick = double.MaxValue;
            foreach (double c in MicCalibrator.Clicks)
                if (now >= c) sinceClick = now - c;
            ring.BorderThickness = sinceClick < 120 ? 6 - (float)(sinceClick / 40) : 3;

            // Each answer: a mark beside the ring, as far as it was from its click.
            while (marked < calibrator.Onsets.Count)
            {
                // Where a play with the current delay would place it, beside its own click
                // (owner 2026-10-17: count-in notes, marked against the first click to
                // play, looked far early, as if they spoiled the measurement). Count-in
                // answers are grey: they do not count.
                double heard = calibrator.Onsets[marked++] - currentDelayMs;
                int nearest = 0;
                for (int i = 1; i < MicCalibrator.Clicks.Count; i++)
                    if (Math.Abs(heard - MicCalibrator.Clicks[i]) < Math.Abs(heard - MicCalibrator.Clicks[nearest])) nearest = i;

                bool counts = nearest >= MicCalibrator.COUNT_IN;

                marks.Add(new Box
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.Centre,
                    X = ring_x + (float)Math.Clamp(heard - MicCalibrator.Clicks[nearest], -150, 150) * 0.5f,
                    Size = new Vector2(3, counts ? 46 : 30),
                    Colour = counts ? new Color4(0.42f, 1f, 0.85f, 0.8f) : Color4.White.Opacity(0.3f),
                });
            }

            switch (calibrator.State)
            {
                case MicCalibrator.Phase.Running:
                    int heard = MicCalibrator.Clicks.Count - countAfter(now);
                    text.Text = heard <= MicCalibrator.COUNT_IN
                        ? $"Get ready… {Math.Max(1, MicCalibrator.COUNT_IN - heard + 1)}"
                        : $"Play on each click: {Math.Min(MicCalibrator.PLAY_CLICKS, heard - MicCalibrator.COUNT_IN)} of {MicCalibrator.PLAY_CLICKS}";
                    break;

                case MicCalibrator.Phase.Failed:
                    text.Text = $"The microphone could not be heard: {calibrator.Error}.";
                    finished();
                    break;

                case MicCalibrator.Phase.Done:
                    var r = calibrator.Result!.Value;
                    double raw = Math.Round(r.DelayMs);
                    // No device hears early: below 0 is the player ahead of the clicks.
                    double measured = Math.Max(0, raw);
                    double off = measured - Math.Round(currentDelayMs);

                    string verdict = raw < 0
                        ? $"Your notes landed {-raw:0} ms before the clicks: that is you playing ahead, not the microphone; no device hears a note early. "
                          + (Math.Abs(off) < 1 ? "Your mic delay stays at 0 ms." : $"The mic delay can only go down to 0 ms (now {currentDelayMs:0} ms). Try again waiting for each click.")
                        : Math.Abs(off) < 8
                            ? $"With your mic delay ({currentDelayMs:0} ms) your notes landed on the clicks ({off:+0;-0;0} ms): it is right."
                            : $"With your mic delay ({currentDelayMs:0} ms) your notes landed {Math.Abs(off):0} ms {(off > 0 ? "late" : "early")}: the right delay is {measured:0} ms.";

                    osu.Framework.Logging.Logger.Log($"kan!: mic timing says: {verdict}");

                    text.Text = r.Matched == 0
                        ? "No notes were heard near the clicks. Check that the microphone is on and close enough, then try again."
                        : $"{verdict}\n(heard {r.Matched} of {r.Clicks}, spread ±{r.SpreadMs:0} ms)"
                          + (r.Reliable ? string.Empty : "\nThat is not steady enough to trust: try again, as close to each click as you can.");
                    useIt.Alpha = r.Matched > 0 && Math.Abs(off) >= 1 ? 1 : 0;
                    useIt.Enabled.Value = r.Reliable;
                    useIt.Text = $"Use {measured:0} ms";
                    finished();
                    break;
            }
        }

        private static int countAfter(double now)
        {
            int n = 0;
            foreach (double c in MicCalibrator.Clicks)
                if (c > now) n++;
            return n;
        }

        private void finished()
        {
            start.Text = @"Try again";
            start.Enabled.Value = true;
            osu.Framework.Logging.Logger.Log($"kan!: mic timing {calibrator!.State}: {(calibrator.Result is { } r ? $"{r.DelayMs:0} ms, {r.Matched} of {r.Clicks}, ±{r.SpreadMs:0}" : calibrator.Error)}");
            calibrator.Dispose();
            lastResult = calibrator.Result;
            calibrator = null;
        }

        private MicCalibration.Result? lastResult;

        /// <summary>
        /// The mic delay in use when the measurement started. The measurement
        /// itself is raw (the whole delay, nothing taken off); the marks and
        /// the words show it as a play would with this delay (owner 2026-10-17:
        /// with a delay set, the marks still landed late, as if it were not).
        /// </summary>
        private double currentDelayMs;

        private void apply()
        {
            if (lastResult is not { Reliable: true } r) return;

            ForgeTiming.SetCalibratedMicDelay(Math.Max(0, r.DelayMs), instrument());
            text.Text = $"Saved: mic delay {ForgeTiming.MIC_DELAY_MS.Value:0} ms.";
            useIt.Alpha = 0;
        }

        protected override void PopIn()
        {
            intro();
            this.FadeIn(150);
        }

        protected override void PopOut()
        {
            base.PopOut();
            calibrator?.Dispose();
            calibrator = null;
            this.FadeOut(150);
            silenced?.RemoveAdjustment(AdjustableProperty.Volume, silence);
            silenced = null;
        }

        protected override void Dispose(bool isDisposing)
        {
            calibrator?.Dispose();
            silenced?.RemoveAdjustment(AdjustableProperty.Volume, silence);
            base.Dispose(isDisposing);
        }
    }
}
