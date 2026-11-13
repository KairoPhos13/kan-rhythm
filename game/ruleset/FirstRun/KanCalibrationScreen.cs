// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Localisation;
using osu.Framework.Screens;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Overlays;
using osuTK;
using Kan.Core.Play;
using Kan.Forge.Play;
using Kan.Forge.Tracks;
using Kan.Ruleset.Configuration;
using Kan.Ruleset.UI;

namespace Kan.Ruleset.FirstRun
{
    /// <summary>
    /// The 3-step calibration screen (issue 17): the microphone's delay, the
    /// room's own sound, and the instrument's tuning. Every step is a service
    /// to the player, never a test: each can be skipped, re-run whenever, and
    /// what it measures is kept (kan-forge/timing.json) and shown in
    /// Settings &gt; kan!. The latency step runs the editor's own measurement
    /// (<see cref="MicTimingOverlay"/>, <see cref="MicCalibrator"/>); the
    /// other two listen with the game's own pipeline
    /// (<see cref="KanLiveListener"/>), KAN_LIVE_INPUT in a developer run.
    ///
    /// It is one wizard screen so the first-run guide can carry it as a step
    /// and Settings can show it on its own (<see cref="KanCalibrationOverlay"/>):
    /// one screen, one place to keep in step with the game.
    /// </summary>
    [LocalisableDescription(typeof(KanFirstRunStrings), nameof(KanFirstRunStrings.CalibrationTitle))]
    public partial class KanCalibrationScreen : WizardScreen
    {
        private KanLatencyStep latency = null!;
        private KanNoiseStep noise = null!;
        private KanDetuneStep detune = null!;

        [BackgroundDependencyLoader]
        private void load()
        {
            Content.Children = new Drawable[]
            {
                new OsuTextFlowContainer(t => t.Font = OsuFont.Default.With(size: CONTENT_FONT_SIZE))
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Text = @"Three quiet measurements, in any order you like. Each takes a few seconds, each can wait for later, and none of them is a test: they only tell kan! how to listen to you.",
                },
                latency = new KanLatencyStep(OverlayColourProvider),
                noise = new KanNoiseStep(OverlayColourProvider),
                detune = new KanDetuneStep(OverlayColourProvider),
                new OsuTextFlowContainer(t => t.Font = OsuFont.Default.With(size: CONTENT_FONT_SIZE))
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Colour = OverlayColourProvider.Content2,
                    Text = @"Everything measured here is yours: it stays on this computer, and Settings > Rulesets > kan! shows and changes it at any time.",
                },
            };
        }

        public override void OnSuspending(ScreenTransitionEvent e)
        {
            // Listening only while this screen is the one being looked at.
            noise.Stop();
            detune.Stop();
            base.OnSuspending(e);
        }

        public override bool OnExiting(ScreenExitEvent e)
        {
            noise.Stop();
            detune.Stop();
            return base.OnExiting(e);
        }

        protected override void Dispose(bool isDisposing)
        {
            noise?.Stop();
            detune?.Stop();
            base.Dispose(isDisposing);
        }

        /// <summary>
        /// Developer tooling: run one step on its own (KAN_DEV_SCREEN=calibrate
        /// with KAN_DEV_STEP=latency / noise / detune).
        /// </summary>
        internal void DevRun(string step)
        {
            KanCalibrationStep? target = step switch
            {
                @"latency" => latency,
                @"noise" => noise,
                @"detune" => detune,
                _ => null,
            };

            if (target == null)
            {
                osu.Framework.Logging.Logger.Log($"kan! dev: no calibration step named {step} (latency, noise or detune)", level: osu.Framework.Logging.LogLevel.Important);
                return;
            }

            this.FindClosestParent<osu.Game.Graphics.Containers.OsuScrollContainer>()?.ScrollIntoView(target);
            Scheduler.AddDelayed(target.Measure, 1000);
        }
    }

    /// <summary>
    /// One step of the calibration screen: its words, its result, and its
    /// three buttons (measure, keep, skip). The steps measure, never judge.
    /// </summary>
    internal abstract partial class KanCalibrationStep : CompositeDrawable
    {
        protected readonly OverlayColourProvider Colours;

        /// <summary>The result as the player reads it (empty until there is one).</summary>
        protected readonly OsuTextFlowContainer Status;

        /// <summary>How to take the measurement again, once one exists.</summary>
        protected readonly RoundedButton Run;

        /// <summary>Keep what was measured (hidden until there is something to keep).</summary>
        protected readonly RoundedButton Use;

        private readonly RoundedButton skip;

        protected KanCalibrationStep(OverlayColourProvider colours, string title, string prompt)
        {
            Colours = colours;

            RelativeSizeAxes = Axes.X;
            AutoSizeAxes = Axes.Y;
            Masking = true;
            CornerRadius = 10;

            InternalChildren = new Drawable[]
            {
                new Box { RelativeSizeAxes = Axes.Both, Colour = colours.Background4 },
                new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(8, 0),
                    Padding = new MarginPadding(14),
                    Children = new Drawable[]
                    {
                        new OsuSpriteText
                        {
                            Text = title,
                            Font = OsuFont.GetFont(size: 18, weight: FontWeight.SemiBold),
                            Colour = colours.Content1,
                        },
                        new OsuTextFlowContainer(t => t.Font = OsuFont.GetFont(size: 15))
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Colour = colours.Content2,
                            Text = prompt,
                        },
                        Status = new OsuTextFlowContainer(t => t.Font = OsuFont.GetFont(size: 15))
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Colour = colours.Content1,
                        },
                        new FillFlowContainer
                        {
                            AutoSizeAxes = Axes.Both,
                            Direction = FillDirection.Horizontal,
                            Spacing = new Vector2(8, 0),
                            Children = new Drawable[]
                            {
                                Run = new RoundedButton
                                {
                                    Width = 170,
                                    Height = 32,
                                    Action = Measure,
                                },
                                Use = new RoundedButton
                                {
                                    Width = 170,
                                    Height = 32,
                                    Text = @"Use it",
                                    Alpha = 0,
                                    Action = Apply,
                                },
                                skip = new RoundedButton
                                {
                                    Width = 130,
                                    Height = 32,
                                    Text = @"Skip for now",
                                    Action = Skip,
                                },
                            },
                        },
                    },
                },
            };
        }

        /// <summary>Take (or take again) the measurement.</summary>
        public abstract void Measure();

        /// <summary>Keep what was measured.</summary>
        protected abstract void Apply();

        /// <summary>Leave it for later (never a failure).</summary>
        protected abstract void Skip();

        /// <summary>Stop whatever is running (the screen went away).</summary>
        public abstract void Stop();

        /// <summary>What to call the measurement again once one exists.</summary>
        protected void Offer(string again)
        {
            Run.Text = again;
        }

        protected string Instrument => KanLiveListener.ListenAs;

        /// <summary>The instrument's tuning as the game already knows it.</summary>
        protected static double TuningCents => ForgeTiming.TUNING_CENTS.Value;
    }

    /// <summary>
    /// Step 1, acoustic latency (issue 17): how long the microphone takes,
    /// measured the way a play hears the player: clicks to play along to.
    /// The measurement is the editor's own (<see cref="MicTimingOverlay"/>),
    /// reused here rather than written again; its "Use it" keeps the result,
    /// which a play then takes off what it hears (judging offsets).
    /// </summary>
    internal partial class KanLatencyStep : KanCalibrationStep
    {
        [Resolved(CanBeNull = true)]
        private osu.Game.OsuGame? game { get; set; }

        private MicTimingOverlay? overlay;
        private BindableNumber<double> delay = null!;

        public KanLatencyStep(OverlayColourProvider colours)
            : base(colours, @"1. Microphone delay", @"How long your microphone takes to carry a note to the game. Four clicks count in, then play one short note on each of the next eight, as the ball reaches the ring. kan! takes this delay off what it hears, so your notes are judged where you meant them.")
        {
            Run.Text = @"Measure on clicks";
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            if (Kan.Ruleset.Beatmaps.KanLevelFiles.ForgeRoot is { } root)
                ForgeTiming.Load(root);

            delay = ForgeTiming.MIC_DELAY_MS.GetBoundCopy();
            delay.BindValueChanged(_ => show(), true);
        }

        private void show()
        {
            Status.Text = ForgeTiming.MicCalibratedAtUtc == default
                ? @"Not measured yet. Most microphones take 20 to 60 milliseconds; guessing is all right too (it is settable in the player's Microphone group)."
                : $@"Your microphone delay: {delay.Value:0} ms (measured {ForgeTiming.MicCalibratedAtUtc:yyyy-MM-dd}). kan! takes it off every note it hears.";

            Offer(ForgeTiming.MicCalibratedAtUtc == default ? @"Measure on clicks" : @"Measure again");
            Use.Alpha = 0;
        }

        public override void Measure()
        {
            if (game == null) return;

            overlay ??= new MicTimingOverlay(() => Instrument);
            if (overlay.Parent == null)
                game.Add(overlay);

            overlay.Show();

            // A developer run has no one to press Start: it starts by itself.
            // A player gets the moment to get ready first.
            if (Dev.KanDevPlay.Enabled)
                Scheduler.AddDelayed(() => overlay.DevStart(), 1000);
        }

        protected override void Apply()
        {
            // The measurement overlay keeps its own result ("Use it"); this
            // step only shows what is kept.
            show();
        }

        protected override void Skip()
        {
            Stop();
            Status.Text = @"Left for later: it can be measured any time from here or from the player's Microphone group. Playing with the default (0 ms) is perfectly playable.";
        }

        public override void Stop()
        {
        }

        protected override void Dispose(bool isDisposing)
        {
            delay?.UnbindAll();
            base.Dispose(isDisposing);
        }
    }

    /// <summary>
    /// Step 2, the noise floor (issue 17): what the room sounds like when
    /// nobody plays. The measurement calibrates the note detector's gate: a
    /// sound must sit above the room to start a note, so a humming computer
    /// or a street outside stops raising false ones (docs/issues/08 is where
    /// that gate is measured formally).
    /// </summary>
    internal partial class KanNoiseStep : KanCalibrationStep
    {
        /// <summary>How long the room is listened to (ms).</summary>
        private const double listen_ms = 3000;

        private KanLiveListener? listener;
        private NoiseFloorMeasure? measure;
        private double fromMs;
        private double startedAt;
        private NoiseFloorResult result;

        public KanNoiseStep(OverlayColourProvider colours)
            : base(colours, @"2. The room's own sound", @"Stay quiet for three seconds: kan! listens to the room itself (the hum of a computer, a fan, the street) so none of it counts as a note. No need for absolute silence; simply do not play.")
        {
            Run.Text = @"Listen to the room";
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            if (Kan.Ruleset.Beatmaps.KanLevelFiles.ForgeRoot is { } root)
                ForgeTiming.Load(root);

            show();
        }

        private void show()
        {
            if (!result.Valid)
            {
                Status.Text = ForgeTiming.NoiseCalibratedAtUtc == default
                    ? @"Not measured yet. Without it kan! uses a gentle default gate, which is fine too."
                    : $@"Your room: about {NoiseFloorMeasure.Db(ForgeTiming.NoiseFloorRms):0} dB when nobody plays (measured {ForgeTiming.NoiseCalibratedAtUtc:yyyy-MM-dd}).";
                Use.Alpha = 0;
                return;
            }

            double openDb = NoiseFloorMeasure.Db(result.OpenRms);
            Status.Text = $@"Your room sits at about {result.FloorDb:0} dB when nobody plays. kan! will start hearing notes from about {openDb:0} dB, so the room's hum never counts as one."
                          + (result.PeakRms > result.FloorRms * 4 ? @" There was a loud moment while listening (a door, a chair?); it did not count against you." : string.Empty);
            Offer(@"Listen again");
            Use.Alpha = 1;
        }

        public override void Measure()
        {
            Stop();

            listener = new KanLiveListener();
            measure = new NoiseFloorMeasure();
            fromMs = 0;
            startedAt = Time.Current;
            result = default;

            if (!listener.Start())
            {
                Status.Text = $@"Nothing could be heard ({listener.Error}). No harm done: the room can be measured later, from here or from Settings > kan!.";
                listener.Dispose();
                listener = null;
                return;
            }

            Status.Text = @"Listening to the room…";
            Run.Enabled.Value = false;
            listener.Frame += onFrame;
        }

        protected override void Update()
        {
            base.Update();

            if (listener == null || measure == null) return;

            listener.Update();

            if (fromMs == 0 && listener.NowMs > 0)
                fromMs = listener.NowMs;

            // Stream time drives the measurement; if the stream never arrives,
            // real time ends the step kindly instead of listening forever.
            if (listener.NowMs - fromMs < listen_ms && Time.Current - startedAt < listen_ms + 7000)
            {
                Status.Text = $@"Listening to the room… {Math.Clamp((listener.NowMs - fromMs) / 1000, 0, 3):0.0} of 3 seconds";
                return;
            }

            result = measure.Finish();
            Stop();
            Run.Enabled.Value = true;

            if (!result.Valid)
            {
                Status.Text = @"Nothing was heard at all. No harm done: check that the microphone is on, or measure the room later.";
                return;
            }

            show();
        }

        private void onFrame(double _, double level, float __) => measure?.Add(level);

        protected override void Apply()
        {
            if (!result.Valid) return;

            ForgeTiming.SetCalibratedNoiseFloor(result.FloorRms, Instrument);
            Status.Text = $@"Saved: the room at {result.FloorDb:0} dB. Every play from now on listens above it.";
            Use.Alpha = 0;
        }

        protected override void Skip()
        {
            Stop();
            Run.Enabled.Value = true;
            Status.Text = @"Left for later. The room can be listened to any time from here or from Settings > kan!.";
        }

        public override void Stop()
        {
            if (listener != null)
                listener.Frame -= onFrame;

            listener?.Dispose();
            listener = null;
        }
    }

    /// <summary>
    /// Step 3, the instrument's detune (issue 17): a held reference note says
    /// how far the instrument sits from standard tuning, and kan! follows it.
    /// The correction goes where it belongs: the detector's targets (a note a
    /// little flat on a flat instrument is the right note) and the companion,
    /// which shifts the same way so instrument and song lock together
    /// (kanfx's consonance shift takes the same Δc when it is wired).
    /// </summary>
    internal partial class KanDetuneStep : KanCalibrationStep
    {
        private KanLiveListener? listener;
        private DetuneMeasure? measure;
        private DetuneResult result;
        private double expected = double.NaN;
        private double startedAt;

        /// <summary>Whether the note held was another one than the note asked for.</summary>
        private bool heldAnotherNote;

        public KanDetuneStep(OverlayColourProvider colours)
            : base(colours, @"3. Your instrument's tuning", @"Hold one comfortable note as steady as you can, for about two seconds. A slightly flat or sharp instrument is not punished: this is how kan! learns to follow yours.")
        {
            Run.Text = @"Measure my tuning";
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            if (Kan.Ruleset.Beatmaps.KanLevelFiles.ForgeRoot is { } root)
                ForgeTiming.Load(root);

            show();
        }

        /// <summary>The note to hold: the middle of the player's range, or A4.</summary>
        private int referenceNote => KanPlayer.Instrument == null
            ? 69
            : (int)Math.Clamp(Math.Round((KanPlayer.Lowest + KanPlayer.Highest) / 2.0), 21, 108);

        private void show()
        {
            if (!result.Valid)
            {
                Status.Text = ForgeTiming.TUNING_CENTS.Value == 0
                    ? $"Not measured yet. When you are ready: hold {KanNotation.Label(referenceNote)} for about two seconds."
                    : $@"Your instrument's tuning: {TuningCents:+0;-0;0} cents ({KanNotation.Label(referenceNote)} is the note to hold for a measurement).";
                Use.Alpha = 0;
                return;
            }

            double off = Math.Round(result.DeltaCents);
            string how = off == 0
                ? @"plays in tune with A440"
                : off < 0 ? $"plays about {Math.Abs(off):0} cents flat" : $"plays about {off:0} cents sharp";

            Status.Text = $"Your instrument {how}; that is fine, kan! will follow you."
                          + (heldAnotherNote ? $@" (You held {KanNotation.Label((int)Math.Round(result.MeasuredMidi))}, not {KanNotation.Label((int)expected)}: kan! measured against what you played.)" : string.Empty)
                          + (result.SpreadCents > 25 ? @" That note wobbled a little: a steadier one gives a finer result." : string.Empty);
            Offer(@"Measure again");
            Use.Alpha = 1;
        }

        public override void Measure()
        {
            Stop();

            expected = referenceNote;
            listener = new KanLiveListener();
            measure = new DetuneMeasure();
            result = default;
            startedAt = Time.Current;

            if (!listener.Start())
            {
                Status.Text = $@"Nothing could be heard ({listener.Error}). No harm done: the tuning can be measured later, or set by hand in Settings > kan!.";
                listener.Dispose();
                listener = null;
                return;
            }

            Status.Text = $"Hold {KanNotation.Label((int)expected)} now, and keep it sounding…";
            Run.Enabled.Value = false;
            listener.Frame += onFrame;
        }

        protected override void Update()
        {
            base.Update();

            if (listener == null || measure == null) return;

            listener.Update();

            if (!measure.Ready)
            {
                // Stream time drives the measurement; real time ends the step
                // kindly if the stream, or the note, never quite arrives.
                if (Time.Current - startedAt <= DetuneMeasure.WINDOW_MS + 12000)
                {
                    Status.Text = measure.Readings == 0
                        ? Time.Current - startedAt > 5000
                            ? $"I cannot hear a note yet: hold {KanNotation.Label((int)expected)} steadily, close to the microphone."
                            : $"Hold {KanNotation.Label((int)expected)} now, and keep it sounding…"
                        : $"Hold {KanNotation.Label((int)expected)}… {Math.Clamp(1 - measure.Progress, 0, 1) * DetuneMeasure.WINDOW_MS / 1000:0.0} seconds to go";
                    return;
                }

                if (measure.Readings == 0)
                {
                    Stop();
                    Run.Enabled.Value = true;
                    Status.Text = $@"No note was heard at all. No harm done: try again holding {KanNotation.Label((int)expected)} all the way through, or set the tuning by hand in Settings > kan!.";
                    return;
                }

                // A little something was heard: measure what there is (the
                // words will say if it was not steady).
            }

            // Told the note, if the one held is that note; otherwise measured
            // against the note that was actually held (questioned gently,
            // never rejected).
            var guided = measure.Finish(expected);
            heldAnotherNote = Math.Abs(guided.MeasuredMidi - expected) >= 0.5;
            result = heldAnotherNote ? measure.Finish() : guided;

            Stop();
            Run.Enabled.Value = true;
            show();
        }

        private void onFrame(double _, double __, float midi) => measure?.Add(midi, listener?.NowMs ?? 0);

        protected override void Apply()
        {
            if (!result.Valid) return;

            ForgeTiming.TUNING_CENTS.Value = Math.Round(result.DeltaCents);
            Status.Text = $@"Saved: {TuningCents:+0;-0;0} cents. kan! takes it off what it hears, and the companion sits with your instrument.";
            Use.Alpha = 0;
        }

        protected override void Skip()
        {
            Stop();
            Run.Enabled.Value = true;
            Status.Text = @"Left for later. The tuning can be measured again here, learnt while you play (Settings > kan!), or set by hand.";
        }

        public override void Stop()
        {
            if (listener != null)
                listener.Frame -= onFrame;

            listener?.Dispose();
            listener = null;
        }
    }
}
