// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Overlays;
using osu.Game.Screens.Edit;
using osuTK;
using Kan.Forge.Media;
using Kan.Forge.Native;
using Kan.Forge.Tracks;

namespace Kan.Forge.Editor.Compose
{
    /// <summary>
    /// Measures the mic delay (owner 2026-10-14: "ask the user to play a
    /// note several times for an exact moment"): four count-in clicks, then
    /// eight clicks to play a short note on. The recording is transcribed
    /// with the level's instrument, so the result covers the whole round trip
    /// gameplay will see — speakers, player, microphone and transcriber.
    /// Recording and clicks start together exactly like a section recording
    /// does, so the number applies to those as it is.
    /// </summary>
    internal partial class MicCalibrationPanel : VisibilityContainer
    {
        private const int sample_rate = MicRecorder.SAMPLE_RATE;
        private const double interval_ms = 750; // 80 BPM: easy to follow
        private const double lead_ms = 1000;
        private const int count_in = 4;
        private const int play_clicks = 8;

        [Resolved]
        private AudioManager audio { get; set; } = null!;

        [Resolved]
        private EditorClock clock { get; set; } = null!;

        [Resolved]
        private OsuColour colours { get; set; } = null!;

        private OsuTextFlowContainer text = null!;
        private ForgeButton start = null!, useIt = null!;

        private MicRecorder? recorder;
        private SetMonitor.Channel? clicks;
        private bool clicksStarted;
        private bool busy;
        private MicCalibration.Result? result;

        /// <summary>The play-along clicks, in ms from the take's start.</summary>
        private static double[] playClicksMs => Enumerable.Range(count_in, play_clicks).Select(i => lead_ms + i * interval_ms).ToArray();

        [BackgroundDependencyLoader]
        private void load(OverlayColourProvider colourProvider)
        {
            Width = 440;
            AutoSizeAxes = Axes.Y;
            Anchor = Anchor.Centre;
            Origin = Anchor.Centre;
            Masking = true;
            CornerRadius = 8;

            Children = new Drawable[]
            {
                new Box { RelativeSizeAxes = Axes.Both, Colour = colourProvider.Background5, Alpha = 0.97f },
                new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(0, 10),
                    Padding = new MarginPadding(16),
                    Children = new Drawable[]
                    {
                        new OsuSpriteText { Text = @"Measure the mic delay", Font = OsuFont.GetFont(size: 18, weight: FontWeight.Bold) },
                        text = new OsuTextFlowContainer(t =>
                        {
                            t.Font = OsuFont.Style.Caption1;
                            t.Colour = colourProvider.Content2;
                        })
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                        },
                        start = new ForgeButton(FontAwesome.Solid.Microphone, @"start", colours.Red1) { Action = begin },
                        useIt = new ForgeButton(FontAwesome.Solid.Check, @"use it") { Action = apply, Alpha = 0 },
                        new ForgeButton(FontAwesome.Solid.Times, @"close") { Action = Hide },
                    },
                },
            };

            showIntro();
        }

        private void showIntro()
        {
            text.Text = $"You will hear {count_in} clicks to get ready, then {play_clicks} more: play one short note exactly on each of those {play_clicks}, as you would in a level. "
                        + "Use the instrument you will play, with the microphone where it will be.\n\n"
                        + $"Current mic delay: {ForgeTiming.MIC_DELAY_MS.Value:0} ms"
                        + (ForgeTiming.MicCalibratedAtUtc == default ? " (never measured)." : $" (measured {ForgeTiming.MicCalibratedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm} with {Kan.Forge.Pipeline.ForgeJobs.ProfileLabel(ForgeTiming.MicCalibratedInstrument)}).");
            start.Text = @"start";
            useIt.Alpha = 0;
            result = null;
        }

        private void begin()
        {
            if (busy) return;

            clock.Stop();

            string clickPath = ForgeTrackSession.WorkPath(@"calibration-clicks.wav");
            float[] clickSamples = clickTrack();
            MicRecorder.WriteWav(clickPath, clickSamples);

            clicks?.Dispose();
            clicks = SetMonitor.Channel.Open(audio, new ForgeTrack(@"calibration clicks", clickPath));

            if (clicks == null)
            {
                text.Text = @"The clicks could not be played.";
                return;
            }

            recorder = new MicRecorder(clickSamples.Length / (double)sample_rate + 2);

            if (!recorder.Start(out string error))
            {
                recorder.Dispose();
                recorder = null;
                text.Text = $"Recording failed: {error}.";
                return;
            }

            // As a section recording: the take's first sample is the clicks' time 0.
            clicks.Track.Seek(0);
            clicks.Track.Start();
            clicksStarted = false;
            busy = true;
            start.Enabled.Value = false;
            useIt.Alpha = 0;
            text.Text = @"Listening… play one note on each of the last eight clicks.";
        }

        protected override void Update()
        {
            base.Update();

            if (recorder == null || clicks == null) return;

            var t = clicks.Track;
            if (t.IsRunning) clicksStarted = true;

            if (clicksStarted && t.IsRunning)
            {
                int heard = (int)Math.Floor((t.CurrentTime - lead_ms) / interval_ms) + 1;
                text.Text = heard <= count_in
                    ? $"Get ready… {Math.Max(0, count_in - heard) + 1}"
                    : $"Play on each click: {Math.Min(play_clicks, heard - count_in)} of {play_clicks}";
            }

            if (clicksStarted && (!t.IsRunning || t.CurrentTime >= t.Length - 1))
                finish();
        }

        private void finish()
        {
            float[] take = recorder!.Stop();
            recorder.Dispose();
            recorder = null;
            clicks?.Track.Stop();

            string takePath = ForgeTrackSession.NewWorkPath(@"calibration-take");
            MicRecorder.WriteWav(takePath, take);
            string instrument = ForgeTrackSession.Instrument;
            text.Text = @"Measuring…";

            // The delay measured here is the live judge's: hear the take as the judge does.
            Kan.Forge.Pipeline.ForgeJobs.TranscribeAsync(takePath, instrument, null, Scheduler, KanForgeNative.ENGINE_DESCRIBER)
               .ContinueWith(task => Schedule(() =>
               {
                   busy = false;
                   start.Enabled.Value = true;
                   start.Text = @"try again";

                   if (task.IsFaulted)
                   {
                       text.Text = $"The take could not be analysed: {task.Exception?.GetBaseException().Message}";
                       return;
                   }

                   // Every heard note counts, pitch known or not: only its start matters here.
                   var onsets = task.Result.Notes.Select(n => (double)n.OnsetMs).ToList();
                   var r = MicCalibration.Estimate(playClicksMs, onsets);
                   result = r;

                   if (r.Matched == 0)
                   {
                       text.Text = "No notes were heard near the clicks. Check that the microphone is on and close enough, then try again.";
                       return;
                   }

                   text.Text = $"Your notes arrived {r.DelayMs:0} ms after the clicks (heard {r.Matched} of {r.Clicks}, spread ±{r.SpreadMs:0} ms)."
                               + (r.Reliable
                                   ? "\nUse it: section recordings will be placed this much earlier, and gameplay will judge your mic with it."
                                   : "\nThat is not steady enough to trust — try again, playing as close to each click as you can.");
                   useIt.Alpha = 1;
                   useIt.Enabled.Value = r.Reliable;
               }));
        }

        private void apply()
        {
            if (result is not { Reliable: true } r) return;

            ForgeTiming.SetCalibratedMicDelay(r.DelayMs, ForgeTrackSession.Instrument);
            showIntro();
            text.Text = $"Saved: mic delay {ForgeTiming.MIC_DELAY_MS.Value:0} ms.";
        }

        /// <summary>Short decaying beeps: higher for the count-in.</summary>
        private static float[] clickTrack()
        {
            int total = count_in + play_clicks;
            var samples = new float[(int)((lead_ms + total * interval_ms + 500) / 1000 * sample_rate)];

            for (int i = 0; i < total; i++)
            {
                int at = (int)((lead_ms + i * interval_ms) / 1000 * sample_rate);
                double freq = i < count_in ? 1500 : 1000;
                int length = sample_rate * 40 / 1000;

                for (int s = 0; s < length && at + s < samples.Length; s++)
                {
                    double time = (double)s / sample_rate;
                    samples[at + s] = (float)(0.7 * Math.Sin(2 * Math.PI * freq * time) * Math.Exp(-time * 90));
                }
            }

            return samples;
        }

        private void stopAll()
        {
            if (recorder != null)
            {
                recorder.Stop();
                recorder.Dispose();
                recorder = null;
            }

            clicks?.Track.Stop();
            busy = false;
        }

        protected override void PopIn()
        {
            showIntro();
            start.Enabled.Value = true;
            this.FadeIn(120);
        }

        protected override void PopOut()
        {
            stopAll();
            this.FadeOut(120);
        }

        protected override void Dispose(bool isDisposing)
        {
            stopAll();
            clicks?.Dispose();
            clicks = null;
            base.Dispose(isDisposing);
        }
    }
}
