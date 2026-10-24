// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using osu.Framework.Audio;
using Kan.Forge.Editor;
using Kan.Forge.Media;
using Kan.Core.Native;
using Kan.Forge.Tracks;
using Kan.Forge.Audio;
using Kan.Core.Media;

namespace Kan.Forge.Play
{
    /// <summary>
    /// Measures the microphone delay the way a play hears the player (owner
    /// 2026-10-17: "the program consistently marks me late"; the delay
    /// measured in compose went through another path — a recorder and an
    /// offline transcription — and left plays ~45 ms late). Clicks play on a
    /// track; the player answers on the instrument; the live describer
    /// (<see cref="LiveSession"/>, as in a play) hears it, its onsets placed
    /// on the track's clock exactly as a play places them on the level's:
    /// the clock's time when listening starts, plus the stream's time. The
    /// delay is how late the answers land (<see cref="MicCalibration.Estimate"/>),
    /// which is what a play takes off (<c>KanPlayState.toGameplay</c>).
    /// Call <see cref="Update"/> every frame.
    /// </summary>
    public sealed class MicCalibrator : IDisposable
    {
        public const int SAMPLE_RATE = 48000;

        /// <summary>80 BPM: easy to follow.</summary>
        public const double INTERVAL_MS = 750;

        public const double LEAD_MS = 1500;
        public const int COUNT_IN = 4;
        public const int PLAY_CLICKS = 8;

        public enum Phase
        {
            Idle,
            Running,
            Done,
            Failed,
        }

        private readonly ILiveSource? source;
        private AudioChannel? clicks;
        private LiveSession? session;
        private double anchorMs;
        private readonly List<double> onsets = new List<double>();

        /// <summary>
        /// Developer runs: what a measurement listens to instead of the
        /// microphone (set by the ruleset's dev tooling; a test instance must
        /// never hear the person at the computer).
        /// </summary>
        public static Func<ILiveSource?>? DevInput;

        public Phase State { get; private set; } = Phase.Idle;

        /// <summary>Why it failed, when it did.</summary>
        public string Error { get; private set; } = string.Empty;

        public MicCalibration.Result? Result { get; private set; }

        /// <summary>Every click (count-in first), ms on the click track.</summary>
        public static IReadOnlyList<double> Clicks { get; } = Enumerable.Range(0, COUNT_IN + PLAY_CLICKS).Select(i => LEAD_MS + i * INTERVAL_MS).ToArray();

        /// <summary>The clicks to play on.</summary>
        public static IReadOnlyList<double> PlayClicks { get; } = Clicks.Skip(COUNT_IN).ToArray();

        /// <summary>Where the click track is (ms), for the animation.</summary>
        public double TimeMs => clicks?.Track.CurrentTime ?? 0;

        /// <summary>The answers heard so far (ms on the click track).</summary>
        public IReadOnlyList<double> Onsets => onsets;

        /// <param name="source">What to listen to: the microphone (null), or a recording (tests).</param>
        public MicCalibrator(ILiveSource? source = null)
        {
            this.source = source;
        }

        /// <summary>Start the clicks; listening starts once they run (as a play's does once its clock runs).</summary>
        public bool Start(AudioManager audio)
        {
            float[] samples = ClickTrack();
            using var wav = new MemoryStream();
            WavFile.Write(wav, samples, samples.Length, 1, SAMPLE_RATE);

            clicks = AudioChannel.Open(audio, @"kan-mic-clicks.wav", wav.ToArray());

            if (clicks == null)
                return fail(@"the clicks could not be played");

            clicks.Track.Seek(0);
            clicks.Track.Start();
            State = Phase.Running;
            return true;
        }

        public void Update(string instrument)
        {
            if (State != Phase.Running || clicks == null) return;

            var t = clicks.Track;

            if (session == null)
            {
                if (!t.IsRunning) return;

                session = new LiveSession(source ?? new MicSource(), instrument);

                if (!session.Start())
                {
                    fail(session.Error);
                    return;
                }

                // As a play: the stream's first sample is the clock's "now".
                anchorMs = t.CurrentTime;
            }

            while (session.TryTake(out var f))
            {
                if (f.Kind == KanForgeLiveEvent.HIT)
                    onsets.Add(anchorMs + f.OnsetMs);
            }

            if (t.CurrentTime >= Clicks[^1] + INTERVAL_MS || (!t.IsRunning && t.CurrentTime > LEAD_MS))
                finish();
        }

        private void finish()
        {
            stop();
            Result = MicCalibration.Estimate(PlayClicks, onsets);
            State = Phase.Done;
        }

        private bool fail(string error)
        {
            stop();
            Error = error;
            State = Phase.Failed;
            return false;
        }

        private void stop()
        {
            session?.Dispose();
            session = null;
            clicks?.Track.Stop();
        }

        /// <summary>Short decaying beeps: higher for the count-in.</summary>
        public static float[] ClickTrack()
        {
            var samples = new float[(int)((Clicks[^1] + INTERVAL_MS + 500) / 1000 * SAMPLE_RATE)];

            for (int i = 0; i < Clicks.Count; i++)
            {
                int at = (int)(Clicks[i] / 1000 * SAMPLE_RATE);
                double freq = i < COUNT_IN ? 1500 : 1000;
                int length = SAMPLE_RATE * 40 / 1000;

                for (int s = 0; s < length && at + s < samples.Length; s++)
                {
                    double time = (double)s / SAMPLE_RATE;
                    samples[at + s] = (float)(0.7 * Math.Sin(2 * Math.PI * freq * time) * Math.Exp(-time * 90));
                }
            }

            return samples;
        }

        public void Dispose()
        {
            stop();
            clicks?.Dispose();
            clicks = null;
        }
    }
}
