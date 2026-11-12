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
        private double listeningSince;
        private double lastHitAt;
        private int lastHits = -1;
        private string shown = string.Empty;

        [BackgroundDependencyLoader]
        private void load()
        {
            devices = MicSource.RecordingDevices();
            deviceName = new Bindable<string>(devices.Count > 0 ? devices[0].Name : @"Default microphone")
            {
                Default = devices.Count > 0 ? devices[0].Name : @"Default microphone",
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
            };

            deviceChoice.Items = devices.Select(d => d.Name).ToList();
            deviceChoice.Current = deviceName;

            // In a developer run the "microphone" is KAN_LIVE_INPUT (never the
            // machine's own): choosing a device means nothing there.
            if (MicCalibrator.DevInput != null)
                deviceChoice.Alpha = 0;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            deviceName.BindValueChanged(v => startListening(), true);
        }

        private void startListening()
        {
            listener?.Dispose();
            listener = new KanLiveListener(device());
            listeningSince = Time.Current;
            lastHits = -1;

            if (!listener.Start())
                listener = null;
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
        }

        protected override void Dispose(bool isDisposing)
        {
            stopListening();
            base.Dispose(isDisposing);
        }

        protected override void Update()
        {
            base.Update();

            if (listener == null)
            {
                say($@"The microphone could not be opened. No harm done: you can skip this step and play later (the same steps wait in Settings > Rulesets > kan!).");
                return;
            }

            listener.Update();

            // The meter follows what it hears; the words follow the meter.
            meterFill.Width = (float)(meter.DrawWidth * Math.Clamp(listener.Level / loud_rms, 0, 1));

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
