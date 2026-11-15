// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Localisation;
using osu.Framework.Screens;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Overlays;
using osuTK;
using Kan.Core.Play;
using Kan.Forge.Play;
using Kan.Forge.Tracks;
using Kan.Ruleset.UI;

namespace Kan.Ruleset.FirstRun
{
    /// <summary>
    /// The guide's microphone step (issue 16): pick the microphone, see it
    /// hear you, and see it hear a note (the very pipeline a play listens
    /// with (<see cref="KanLiveListener"/>), so what is shown here is what
    /// gameplay will hear. Nothing here is a test: every state is said
    /// kindly ("I can hear you", "I cannot hear anything yet: try X"), and
    /// a dead microphone never traps the player (skip and play later).
    /// </summary>
    [LocalisableDescription(typeof(KanFirstRunStrings), nameof(KanFirstRunStrings.MicrophoneTitle))]
    public partial class KanFirstRunMicrophone : WizardScreen
    {
        /// <summary>How loud is loud (RMS): a level above this asks for a little distance.</summary>
        private const double loud_rms = 0.2;

        /// <summary>How long with nothing heard before the kind "I cannot hear anything yet".</summary>
        private const double silent_ms = 4000;

        private FormDropdown<string> deviceChoice = null!;
        private Container meter = null!;
        private Box meterFill = null!;
        private OsuTextFlowContainer words = null!;
        private IReadOnlyList<(int Device, string Name)> devices = Array.Empty<(int, string)>();
        private Bindable<string> deviceName = null!;

        private KanLiveListener? listener;

        /// <summary>The note being played on the chosen input, watched for fading (issue 39).</summary>
        private readonly HeldNoteCheck watch = new HeldNoteCheck();

        /// <summary>The chosen input fades held notes: shown until the player checks.</summary>
        private double fadingSaidAt = double.NegativeInfinity;

        /// <summary>The microphone check under way: every input listening to one held note.</summary>
        private List<(string Name, KanLiveListener Listener, HeldNoteCheck Check)>? checking;

        private double checkStartedAt, checkHeardAt;

        private RoundedButton checkButton = null!;

        /// <summary>The player's earlier choice is not among the inputs now (renamed by an update, or unplugged).</summary>
        private string missingChoice = string.Empty;
        private double listeningSince;
        private double lastHitAt;
        private int lastHits = -1;
        private string shown = string.Empty;

        [BackgroundDependencyLoader]
        private void load()
        {
            devices = MicSource.RecordingDevices();
            string systemDefault = devices.Count > 0 ? devices[0].Name : @"Default microphone";

            // The player's choice, kept (issue 39: it was only for this meter;
            // plays and calibration always opened the default).
            string chosen = Configuration.KanPlayer.Microphone?.Value ?? string.Empty;
            if (chosen.Length > 0 && !devices.Any(d => d.Device >= 0 && d.Name == chosen))
                missingChoice = chosen;
            deviceName = new Bindable<string>(devices.Any(d => d.Device >= 0 && d.Name == chosen) ? chosen : systemDefault)
            {
                Default = systemDefault,
            };

            Content.Children = new Drawable[]
            {
                KanFirstRunLayout.Words(OverlayColourProvider,
                    @"Which microphone should kan! listen to? Play anything: the bar shows what it hears, and when it hears a note it says so. Get comfortable: this is just a sound check."),
                deviceChoice = new FormDropdown<string>
                {
                    Caption = @"Microphone",
                    HintText = @"The one you speak or play into. The system default is usually right.",
                    RelativeSizeAxes = Axes.X,
                },
                meter = new Container
                {
                    RelativeSizeAxes = Axes.X,
                    Height = 12,
                    Masking = true,
                    CornerRadius = 6,
                    Children = new Drawable[]
                    {
                        new Box { RelativeSizeAxes = Axes.Both, Colour = OverlayColourProvider.Background2 },
                        meterFill = new Box
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            RelativeSizeAxes = Axes.Y,
                            Width = 0,
                            Colour = OverlayColourProvider.Highlight1,
                        },
                    },
                },
                words = KanFirstRunLayout.Words(OverlayColourProvider, string.Empty),
                checkButton = new RoundedButton
                {
                    Width = 260,
                    Height = 32,
                    Text = @"Check my microphones",
                    Action = startCheck,
                },
                KanFirstRunLayout.Words(OverlayColourProvider,
                    @"Some inputs clean up the sound for calls and fade a held note away. The check listens to every input at once while you hold one note, and keeps the one that holds it best."),
            };

            deviceChoice.Items = devices.Select(d => d.Name).ToList();
            deviceChoice.Current = deviceName;

            // In a developer run the "microphone" is KAN_LIVE_INPUT (never the
            // machine's own): choosing a device means nothing there.
            if (MicCalibrator.DevInput != null)
                deviceChoice.Alpha = 0;

            if (devices.Count(d => d.Device >= 0) < 2)
                checkButton.Enabled.Value = false;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            deviceName.BindValueChanged(v =>
            {
                // Only a choice the player makes is kept: opening the step with
                // their microphone missing must not forget it (it may come back).
                if (v.OldValue != v.NewValue && Configuration.KanPlayer.Microphone is { } kept)
                    kept.Value = device() >= 0 ? v.NewValue : string.Empty;
                startListening();
            }, true);
        }

        private void startListening()
        {
            listener?.Dispose();
            listener = new KanLiveListener(device());
            listeningSince = Time.Current;
            lastHits = -1;
            watch.Clear();

            listener.Hit += (_, _) => watch.Clear();
            listener.Frame += (t, level, midi) => watch.Add(t, level, midi);

            if (!listener.Start())
                listener = null;
        }

        /// <summary>Every input listens while the player holds one note (issue 39: names change, behaviour does not).</summary>
        private void startCheck()
        {
            if (checking != null) return;

            listener?.Dispose();
            listener = null;
            checking = new List<(string, KanLiveListener, HeldNoteCheck)>();

            // The system default last: on a Steam Deck it is PipeWire's processed
            // copy of the same hardware, and opened first it would hold the
            // hardware and leave the raw input deaf for the whole check.
            int systemDefault = MicSource.DefaultDevice();

            foreach (var (index, name) in devices.OrderBy(d => d.Device == systemDefault ? 1 : 0))
            {
                if (index < 0) continue;

                var check = new HeldNoteCheck();
                var l = new KanLiveListener(index);
                l.Frame += (t, level, midi) => check.Add(t, level, midi);

                if (l.Start())
                    checking.Add((name, l, check));
                else
                    l.Dispose();
            }

            checkStartedAt = Time.Current;
            checkHeardAt = double.NaN;
            checkButton.Enabled.Value = false;
            say($@"Hold one note, any note, for about {HeldNoteCheck.HOLD_MS / 1000:0} seconds… ({checking.Count} inputs listening)");
        }

        /// <summary>Called each frame while checking: when the hold is over, keep the input that held the note best.</summary>
        private void updateCheck()
        {
            foreach (var c in checking!)
                c.Listener.Update();

            if (double.IsNaN(checkHeardAt) && checking.Any(c => c.Listener.Hits > 0))
                checkHeardAt = Time.Current;

            bool held = !double.IsNaN(checkHeardAt) && Time.Current - checkHeardAt > HeldNoteCheck.HOLD_MS + 500;
            bool gaveUp = double.IsNaN(checkHeardAt) && Time.Current - checkStartedAt > 15000;
            if (!held && !gaveUp) return;

            var results = checking.Select(c => (c.Name, Result: c.Check.Measure())).ToList();

            foreach (var c in checking)
                c.Listener.Dispose();
            checking = null;
            checkButton.Enabled.Value = true;

            foreach (var r in results)
                osu.Framework.Logging.Logger.Log($"kan!: microphone check: {r.Name}: heard {r.Result.Heard} ({r.Result.HeardFraction:P0}), fade {r.Result.FadeDb:0.0} dB, level {r.Result.LevelDb:0} dB");

            string current = deviceName.Value;
            string? best = HeldNoteCheck.Choose(results.Select(r => (r.Name, r.Result)), devices.Any(d => d.Device >= 0 && d.Name == current) ? current : null);

            if (best == null)
            {
                startListening();
                say(@"No input heard a note this time. Play a little closer to the microphone and try the check again, or move on: you can come back any time.");
                lastHitAt = Time.Current;
                return;
            }

            var won = results.First(r => r.Name == best).Result;
            var faded = results.Where(r => r.Name != best && r.Result.Heard && r.Result.FadeDb - won.FadeDb >= HeldNoteCheck.BETTER_DB).OrderByDescending(r => r.Result.FadeDb).FirstOrDefault();
            bool changed = best != current;

            deviceName.Value = best; // kept, and listened to from now on
            if (!changed) startListening();

            say(faded.Name != null
                ? $@"Your note held steady on “{best}”; “{faded.Name}” faded it by {faded.Result.FadeDb:0} dB. kan! listens to “{best}” from now on."
                : $@"Your note held steady on “{best}”. kan! listens to it.");
            lastHitAt = Time.Current; // the words stay a moment
            fadingSaidAt = double.NegativeInfinity;
        }

        private int device()
        {
            foreach (var d in devices)
                if (d.Name == deviceName.Value)
                    return d.Device;

            return -1;
        }

        public override void OnEntering(ScreenTransitionEvent e)
        {
            base.OnEntering(e);
            if (listener == null) startListening();
        }

        public override void OnResuming(ScreenTransitionEvent e)
        {
            base.OnResuming(e);
            startListening();
        }

        public override void OnSuspending(ScreenTransitionEvent e)
        {
            stopListening();
            base.OnSuspending(e);
        }

        public override bool OnExiting(ScreenExitEvent e)
        {
            stopListening();
            return base.OnExiting(e);
        }

        private void stopListening()
        {
            listener?.Dispose();
            listener = null;

            if (checking != null)
            {
                foreach (var c in checking)
                    c.Listener.Dispose();
                checking = null;
                checkButton.Enabled.Value = true;
            }
        }

        protected override void Dispose(bool isDisposing)
        {
            stopListening();
            base.Dispose(isDisposing);
        }

        protected override void Update()
        {
            base.Update();

            if (checking != null)
            {
                updateCheck();
                return;
            }

            if (listener == null)
            {
                say($@"The microphone could not be opened. No harm done: you can skip this step and play later (the same steps wait in Settings > Rulesets > kan!).");
                return;
            }

            listener.Update();

            if (!listener.Listening)
            {
                meterFill.Width = 0;
                say(listener.Waiting
                    ? @"Waiting for the microphone: it is still being let go (by the check, or another program)…"
                    : @"The microphone could not be opened. No harm done: you can skip this step and play later (the same steps wait in Settings > Rulesets > kan!).");
                return;
            }

            // The meter follows what it hears; the words follow the meter.
            meterFill.Width = (float)(meter.DrawWidth * Math.Clamp(listener.Level / loud_rms, 0, 1));

            // A held note dying away while the player still plays it: this
            // input processes the sound (issue 39). Said once a while.
            if (watch.Fading(out double fadeDb) && Time.Current - fadingSaidAt > 10000 && checkButton.Enabled.Value)
            {
                fadingSaidAt = Time.Current;
                lastHitAt = Time.Current;
                say($@"Your note is fading although you hold it ({fadeDb:0} dB): this input may be cleaning up the sound for calls. Press “Check my microphones” and hold one note: kan! keeps the input that holds it.");
                return;
            }

            if (Time.Current - fadingSaidAt < 6000)
                return;

            if (missingChoice.Length > 0 && Time.Current - listeningSince < 6000)
            {
                say($@"The microphone you chose before (“{missingChoice}”) is not here now, so kan! listens to the default. Press “Check my microphones” and hold one note to choose again.");
                return;
            }

            if (listener.Hits != lastHits)
            {
                lastHits = listener.Hits;
                lastHitAt = Time.Current;
                say(listener.LastHitMidi > 0
                    ? $@"There it is: I heard {KanNotation.Label((int)MathF.Round(listener.LastHitMidi))}{cents(listener.LastHitMidi)}. kan! is listening. Play a few more, or move on when you like."
                    : @"There it is: I heard your note. kan! is listening. Play a few more, or move on when you like.");
                return;
            }

            // A note heard stays on screen a moment: the words are for the
            // player, not a ticker.
            if (Time.Current - lastHitAt < 4000)
                return;

            if (listener.Level >= loud_rms)
            {
                say($@"That is loud for the microphone ({NoiseFloorMeasure.Db(listener.Level):0} dB): a little further away is gentler on the sound and on your ears.");
                return;
            }

            bool open = listener.Level >= openLevel;

            if (open)
            {
                say($@"I can hear you ({NoiseFloorMeasure.Db(listener.Level):0} dB). Play one note, any note you like.");
                return;
            }

            if (listener.Level > 0)
            {
                say($@"I hear a little something ({NoiseFloorMeasure.Db(listener.Level):0} dB), but it is very quiet: the microphone may be far away.");
                return;
            }

            if (Time.Current - listeningSince > silent_ms)
                say(@"I cannot hear anything yet. Try playing one note, or move the microphone a little closer.");
            else
                say(@"Listening… play anything.");
        }

        /// <summary>The level at which a sound counts as a note: the room's calibrated floor, or a gentle default.</summary>
        private static double openLevel => (ForgeTiming.NoiseFloorRms > 0 ? ForgeTiming.NoiseFloorRms : 0.002) * Math.Pow(10, NoiseFloorMeasure.OPEN_DB / 20);

        private static string cents(float midi)
        {
            double off = Math.Round((midi - MathF.Round(midi)) * 100);
            return off == 0 ? string.Empty : $@" ({off:+0;-0;0} cents)";
        }

        private void say(string text)
        {
            if (text == shown) return;
            shown = text;
            words.Text = text;
        }
    }
}
