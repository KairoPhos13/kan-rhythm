// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using ManagedBass;
using Kan.Forge.Media;
using Kan.Forge.Tracks;
using Kan.Core.Native;
using Kan.Core.Play;

namespace Kan.Forge.Play
{
    /// <summary>Where live audio comes from: the microphone, or a file played in real time.</summary>
    public interface ILiveSource : IDisposable
    {
        int SampleRate { get; }

        bool Start(out string error);

        /// <summary>Copy the samples that arrived since the last call (up to the array's length).</summary>
        int Read(float[] into);

        /// <summary>Drop what arrived so far: what comes next is heard from now.</summary>
        void SkipArrived();
    }

    /// <summary>
    /// The default microphone through BASS. The capture callback only copies
    /// into a ring allocated up front (one producer, one consumer: no locks,
    /// no allocation on the audio side).
    /// </summary>
    public sealed class MicSource : ILiveSource
    {
        public const int SAMPLE_RATE = 48000;
        private const int ring_size = 1 << 18; // 5.4 s: far more than the consumer ever lags

        /// <summary>The microphone to open: -1 is the one the system calls its default.</summary>
        private readonly int device;

        private readonly float[] ring = new float[ring_size];
        private readonly RecordProcedure procedure;
        private long written;
        private long read;
        private int handle;

        public MicSource(int device = -1)
        {
            this.device = device;
            procedure = onData; // kept alive while recording (the native side holds it)
        }

        /// <summary>The microphones that can be chosen (BASS), the default first.</summary>
        public static IReadOnlyList<(int Device, string Name)> RecordingDevices()
        {
            var devices = new List<(int, string)> { (-1, @"Default microphone") };

            try
            {
                MediaAudioExtractor.EnsureBass();

                for (int i = 0; i < Bass.RecordingDeviceCount; i++)
                {
                    if (Bass.RecordGetDeviceInfo(i, out var info) && info.IsEnabled)
                        devices.Add((i, string.IsNullOrEmpty(info.Name) ? $"Microphone {i + 1}" : info.Name));
                }
            }
            catch (Exception e)
            {
                osu.Framework.Logging.Logger.Log($"kan!: the microphones could not be listed: {e.Message}");
            }

            return devices;
        }

        public int SampleRate => SAMPLE_RATE;

        public bool Start(out string error)
        {
            error = string.Empty;
            MediaAudioExtractor.EnsureBass();

            if (!Bass.RecordInit(device) && Bass.LastError != Errors.Already)
            {
                error = $"no microphone could be opened ({Bass.LastError})";
                return false;
            }

            // A microphone chosen by hand: the one recording starts on.
            if (device >= 0)
                Bass.CurrentRecordingDevice = device;

            handle = Bass.RecordStart(SAMPLE_RATE, 1, BassFlags.Float, 10, procedure, IntPtr.Zero);

            if (handle == 0)
            {
                error = $"the microphone could not start ({Bass.LastError})";
                return false;
            }

            return true;
        }

        public int Read(float[] into)
        {
            long available = Interlocked.Read(ref written) - read;
            if (available > ring_size) read += available - ring_size; // fell behind: skip ahead

            int count = (int)Math.Min(into.Length, Interlocked.Read(ref written) - read);

            for (int i = 0; i < count; i++)
                into[i] = ring[(int)((read + i) & (ring_size - 1))];

            read += count;
            return count;
        }

        public void SkipArrived() => read = Interlocked.Read(ref written);

        private bool onData(int recording, IntPtr data, int length, IntPtr user)
        {
            int samples = length / sizeof(float);
            long at = written;
            int first = (int)Math.Min(samples, ring_size - (at & (ring_size - 1)));

            Marshal.Copy(data, ring, (int)(at & (ring_size - 1)), first);
            if (samples > first)
                Marshal.Copy(data + first * sizeof(float), ring, 0, samples - first);

            Interlocked.Exchange(ref written, at + samples);
            return true;
        }

        public void Dispose()
        {
            if (handle == 0) return;

            Bass.ChannelStop(handle);
            handle = 0;
        }
    }

    /// <summary>
    /// A recording played as if it were the microphone (testing without one,
    /// KAN_LIVE_INPUT=path): it advances with the game's clock, so a take of
    /// the reference plays the level by itself.
    /// </summary>
    public sealed class FileSource : ILiveSource
    {
        private readonly string path;
        private readonly Func<double> elapsedMs;
        private float[] samples = Array.Empty<float>();
        private int fed;

        private readonly double startMs;

        /// <param name="elapsedMs">Milliseconds of the file that should have been heard by now.</param>
        /// <param name="startMs">Where in the file listening starts, like a microphone switched on mid-song (nothing before is heard).</param>
        public FileSource(string path, Func<double> elapsedMs, double startMs = 0)
        {
            this.path = path;
            this.elapsedMs = elapsedMs;
            this.startMs = Math.Max(0, startMs);
        }

        public int SampleRate { get; private set; } = 48000;

        public bool Start(out string error)
        {
            error = string.Empty;
            var decoded = PitchSpectrogram.DecodeMono(path, out int rate);

            if (decoded == null)
            {
                error = $"the live input file could not be read ({path})";
                return false;
            }

            samples = decoded;
            SampleRate = rate;
            fed = (int)Math.Clamp(startMs / 1000 * rate, 0, samples.Length);
            return true;
        }

        public int Read(float[] into)
        {
            int due = (int)Math.Clamp(elapsedMs() / 1000 * SampleRate, 0, samples.Length);
            int count = Math.Min(into.Length, due - fed);
            if (count <= 0) return 0;

            Array.Copy(samples, fed, into, 0, count);
            fed += count;
            return count;
        }

        public void SkipArrived()
        {
            // Paced by a clock from where it started: nothing arrives early.
        }

        public void Dispose()
        {
        }
    }

    /// <summary>
    /// Live description of what the player plays: a source's samples go
    /// through libkan's describer (kan_forge_live) on a worker thread, and its
    /// facts come out on <see cref="TryTake"/> for the game thread. Fact times
    /// are milliseconds since the session started listening.
    /// </summary>
    public sealed class LiveSession : IDisposable
    {
        private const int chunk = 480; // 10 ms at 48 kHz

        private readonly ILiveSource source;
        private readonly string instrument;
        private readonly ConcurrentQueue<KanForgeLiveEvent> facts = new ConcurrentQueue<KanForgeLiveEvent>();
        private Thread? worker;
        private volatile bool running;

        public LiveSession(ILiveSource source, string instrument)
        {
            this.source = source;
            this.instrument = instrument;
        }

        /// <summary>Why the session is not listening (empty while it is).</summary>
        public string Error { get; private set; } = string.Empty;

        /// <summary>Also hands what is heard to a recording (<see cref="PlayTake"/>), on the describer's thread.</summary>
        public PlayTake? Take { get; init; }

        /// <summary>Open and begin at once (<see cref="Open"/>, <see cref="Begin"/>).</summary>
        public bool Start()
        {
            if (!Open()) return false;

            Begin();
            return true;
        }

        private IntPtr live;

        /// <summary>
        /// Open the source and the describer, which may take a while (a busy
        /// audio system once took 5 s to give the microphone, 2026-10-23), so
        /// it may run off the game's thread. Nothing is heard until <see cref="Begin"/>.
        /// </summary>
        public bool Open()
        {
            if (!source.Start(out string error))
            {
                Error = error;
                return false;
            }

            try
            {
                live = KanForgeNative.LiveOpen(instrument, source.SampleRate);
            }
            catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
            {
                Error = "the note detector is not available in this build";
                source.Dispose();
                return false;
            }

            if (live == IntPtr.Zero)
            {
                Error = "the note detector could not start";
                source.Dispose();
                return false;
            }

            // The room's silence, as the game's noise-floor step measured it
            // (issue 17): a sound must sit above it to start a note, so a
            // humming room stops raising false ones.
            if (ForgeTiming.NoiseFloorRms > 0)
                KanForgeNative.LiveSetNoiseFloor(live, (float)(ForgeTiming.NoiseFloorRms * ForgeTiming.NoiseFloorRms));

            return true;
        }

        /// <summary>
        /// Start hearing, from now: what the source took in while opening is
        /// dropped, so the stream's first sample is this moment.
        /// </summary>
        public void Begin()
        {
            source.SkipArrived();

            // Before its thread taps: the take's new stretch starts with the stream.
            Take?.Begin(source.SampleRate);

            Interlocked.Exchange(ref lastArrival, Stopwatch.GetTimestamp());
            running = true;

            // The describer handle passes to the worker exactly once: a
            // Dispose racing this line must not close it as well (audit B14:
            // a double close frees the describer twice).
            var opened = Interlocked.Exchange(ref live, IntPtr.Zero);
            worker = new Thread(() => run(opened)) { IsBackground = true, Name = @"kan live describer" };
            worker.Start();
        }

        private long lastArrival;

        /// <summary>How long (ms) since the source last gave samples: a microphone gives them all the time, silence included.</summary>
        public double SilentForMs => Stopwatch.GetElapsedTime(Interlocked.Read(ref lastArrival)).TotalMilliseconds;

        public bool TryTake(out KanForgeLiveEvent fact) => facts.TryDequeue(out fact);

        // Worst since the last TakeLoad (µs; written by the worker, swapped out by the game).
        private long worstChunkUs;
        private long worstBatchUs;

        /// <summary>
        /// How hard the describer works (owner 2026-10-19, lag hunt): the slowest
        /// 10 ms block it described, and the most audio it found waiting at once
        /// (a lot means it fell behind), both in ms since the last call.
        /// </summary>
        public (double WorstChunkMs, double WorstBacklogMs) TakeLoad()
            => (Interlocked.Exchange(ref worstChunkUs, 0) / 1000.0, Interlocked.Exchange(ref worstBatchUs, 0) / 1000.0);

        private static void raise(ref long worst, long value)
        {
            long seen;
            while (value > (seen = Interlocked.Read(ref worst)) && Interlocked.CompareExchange(ref worst, value, seen) != seen)
            {
            }
        }

        private void run(IntPtr live)
        {
            var samples = new float[chunk * 10];
            var events = new KanForgeLiveEvent[1024];

            try
            {
                while (running)
                {
                    int got = source.Read(samples);

                    if (got == 0)
                    {
                        Thread.Sleep(4);
                        continue;
                    }

                    Interlocked.Exchange(ref lastArrival, Stopwatch.GetTimestamp());
                    raise(ref worstBatchUs, (long)got * 1_000_000 / source.SampleRate);
                    Take?.Tap(samples, got);

                    // The describer measures in its own blocks; hand it 10 ms at a time.
                    for (int at = 0; at < got; at += chunk)
                    {
                        int n = Math.Min(chunk, got - at);
                        long started = Stopwatch.GetTimestamp();
                        int count = KanForgeNative.LiveProcess(live, at == 0 ? samples : slice(samples, at, n), n, events, events.Length);
                        raise(ref worstChunkUs, (long)Stopwatch.GetElapsedTime(started).TotalMicroseconds);

                        for (int i = 0; i < count; i++)
                            facts.Enqueue(events[i]);
                    }
                }
            }
            finally
            {
                KanForgeNative.LiveClose(live);
            }
        }

        private float[]? sliceBuffer;

        private float[] slice(float[] samples, int at, int n)
        {
            sliceBuffer ??= new float[chunk];
            Array.Copy(samples, at, sliceBuffer, 0, n);
            return sliceBuffer;
        }

        /// <summary>Stop and close (may take a while with a busy audio system: it may run off the game's thread).</summary>
        public void Dispose()
        {
            running = false;
            worker?.Join(500);
            worker = null;

            // Opened, never begun: the describer is still ours to close.
            // Taken atomically (audit B14): racing Begin, or a second Dispose,
            // closes it at most once.
            var opened = Interlocked.Exchange(ref live, IntPtr.Zero);
            if (opened != IntPtr.Zero)
                KanForgeNative.LiveClose(opened);

            source.Dispose();
        }
    }
}
