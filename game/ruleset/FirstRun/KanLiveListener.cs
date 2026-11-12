// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using Kan.Core.Native;
using Kan.Forge.Play;

namespace Kan.Ruleset.FirstRun
{
    /// <summary>
    /// One live listening for the setup screens (the microphone test and the
    /// calibration steps, issues 16 and 17): the very pipeline a play listens
    /// with (<see cref="LiveSession"/>, libkan's describer), so what these
    /// screens show is what gameplay will hear. In a developer run it hears
    /// KAN_LIVE_INPUT and never the microphone (<see cref="MicCalibrator.DevInput"/>),
    /// like every other measurement in the game.
    ///
    /// Call <see cref="Update"/> once a frame while listening, and
    /// <see cref="Dispose"/> when the screen goes: the microphone is closed
    /// with it.
    /// </summary>
    internal sealed class KanLiveListener : IDisposable
    {
        /// <summary>The instrument the game listens for: the player's, else the level's.</summary>
        public static string ListenAs => Configuration.KanPlayer.Instrument ?? @"keyboard";

        private readonly int device;
        private LiveSession? session;

        /// <param name="device">Which microphone (BASS; -1 is the default). A developer run listens to KAN_LIVE_INPUT whatever this says.</param>
        public KanLiveListener(int device = -1)
        {
            this.device = device;
        }

        /// <summary>What it heard (the same facts a play judges from): stream time, level and pitch.</summary>
        public event Action<double, double, float>? Frame;

        /// <summary>A note started: stream time and its pitch (0 = not yet known).</summary>
        public event Action<double, float>? Hit;

        /// <summary>Why nothing is heard (empty while listening).</summary>
        public string Error { get; private set; } = string.Empty;

        /// <summary>The last level heard (RMS, 0..1; 0 before anything).</summary>
        public double Level { get; private set; }

        /// <summary>The last pitch heard (fractional MIDI; 0 = none).</summary>
        public float Midi { get; private set; }

        /// <summary>Notes heard since <see cref="Start"/>.</summary>
        public int Hits { get; private set; }

        /// <summary>The pitch of the newest note heard (fractional MIDI; 0 = not known).</summary>
        public float LastHitMidi { get; private set; }

        /// <summary>Stream time of the newest fact (ms).</summary>
        public double NowMs { get; private set; }

        public bool Listening => session != null;

        /// <summary>Open the microphone (or the developer run's recording) and begin.</summary>
        public bool Start()
        {
            Stop();

            var source = MicCalibrator.DevInput?.Invoke() ?? new MicSource(device);
            session = new LiveSession(source, ListenAs);

            if (!session.Start())
            {
                Error = session.Error.Length > 0 ? session.Error : @"the microphone could not start";
                session.Dispose();
                session = null;
                return false;
            }

            Error = string.Empty;
            return true;
        }

        /// <summary>Take whatever was heard since the last call.</summary>
        public void Update()
        {
            if (session == null) return;

            while (session.TryTake(out var f))
            {
                switch (f.Kind)
                {
                    case KanForgeLiveEvent.FRAME:
                        Level = f.Level;
                        Midi = f.Midi;
                        NowMs = f.TMs;
                        Frame?.Invoke(f.TMs, f.Level, f.Midi);
                        break;

                    case KanForgeLiveEvent.HIT:
                        Hits++;
                        LastHitMidi = f.Midi;
                        NowMs = f.OnsetMs;
                        Hit?.Invoke(f.OnsetMs, f.Midi);
                        break;
                }
            }
        }

        /// <summary>Stop hearing (the microphone is closed; <see cref="Start"/> may come again).</summary>
        public void Stop()
        {
            session?.Dispose();
            session = null;
        }

        public void Dispose() => Stop();
    }
}
