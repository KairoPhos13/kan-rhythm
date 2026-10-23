// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Kan.Forge.Media;

namespace Kan.Forge.Play
{
    /// <summary>
    /// What the player played, heard again in the replay (owner 2026-10-22:
    /// "in the replay I cannot hear myself, only see how I scored"). A play
    /// records what the microphone heard (<see cref="Tap"/>, the describer's
    /// thread) and where the level was at each moment of it (<see cref="Mark"/>,
    /// the update thread); <see cref="Save"/> keeps it next to the level's
    /// files, found again by its id (<see cref="Id"/>, carried in the replay).
    /// Kept: the last <see cref="KEPT_RECENT"/> plays and the best play of each
    /// level (owner 2026-10-22). Only on this computer.
    ///
    /// The recording stays in real time (the player's own pace, rate mods and
    /// waits included); the marks put it on the level's timeline, as the judge
    /// placed what it heard — without the player's timing habit, so the replay
    /// sounds as it was played.
    /// </summary>
    public sealed class PlayTake
    {
        public const int KEPT_RECENT = 20;

        /// <summary>The take's rate: an instrument heard well, a third of the microphone's data.</summary>
        public const int SAMPLE_RATE = 16000;

        private const string folder = @"takes";
        private const int taps = 33;

        /// <summary>A place on both timelines: the level (ms) and the take (ms).</summary>
        public readonly record struct Point(double LevelMs, double TakeMs, int Segment);

        public readonly int Id;

        // The describer's thread taps while the update thread marks and a play's end saves.
        private readonly object gate = new object();
        private bool closed;

        // Written by the describer's thread only; read once it stopped (Save).
        private readonly List<float[]> blocks = new List<float[]>();
        private float[] block = new float[SAMPLE_RATE];
        private int inBlock;
        private long written;

        // Downsampling: a low-pass before keeping one sample in `factor`.
        private readonly float[] history = new float[taps];
        private float[] kernel = Array.Empty<float>();
        private int factor;
        private int phase;
        private int historyAt;

        private readonly List<Point> points = new List<Point>();
        private int segment = -1;
        private double segmentStartMs;
        private double lastMarkLevelMs = double.NaN;

        public PlayTake()
        {
            Id = Random.Shared.Next(1, int.MaxValue);
        }

        /// <summary>The take's length so far (ms). Read between listening sessions.</summary>
        public double LengthMs => written * 1000.0 / SAMPLE_RATE;

        /// <summary>
        /// A listening session starts (its stream's first sample is its real
        /// time 0), before its thread taps. Rates other than a multiple of
        /// <see cref="SAMPLE_RATE"/> are not kept (false).
        /// </summary>
        public bool Begin(int sampleRate)
        {
            lock (gate)
                return begin(sampleRate);
        }

        private bool begin(int sampleRate)
        {
            if (closed || sampleRate <= 0 || sampleRate % SAMPLE_RATE != 0)
            {
                factor = 0;
                return false;
            }

            if (sampleRate / SAMPLE_RATE != factor)
            {
                factor = sampleRate / SAMPLE_RATE;
                kernel = lowPass(factor);
            }

            segment++;
            broken = false;
            segmentStartMs = LengthMs;
            lastMarkLevelMs = double.NaN;
            Array.Clear(history);
            phase = 0;
            return true;
        }

        /// <summary>The describer's thread: samples as they arrived.</summary>
        public void Tap(float[] samples, int count)
        {
            lock (gate)
                tap(samples, count);
        }

        private void tap(float[] samples, int count)
        {
            if (factor == 0 || closed) return;

            for (int i = 0; i < count; i++)
            {
                history[historyAt] = samples[i];
                historyAt = (historyAt + 1) % taps;

                if (++phase < factor) continue;

                phase = 0;
                float sum = 0;
                for (int k = 0; k < taps; k++)
                    sum += kernel[k] * history[(historyAt + k) % taps];

                if (inBlock == block.Length)
                {
                    blocks.Add(block);
                    block = new float[SAMPLE_RATE];
                    inBlock = 0;
                }

                block[inBlock++] = sum;
                written++;
            }
        }

        /// <summary>
        /// The update thread, each frame the level runs: at real time
        /// <paramref name="streamMs"/> of this session, sound that happened
        /// <paramref name="delayMs"/> earlier (the microphone's round trip) was
        /// played at <paramref name="levelMs"/>.
        /// </summary>
        public void Mark(double levelMs, double streamMs, double delayMs)
        {
            lock (gate)
                mark(levelMs, streamMs, delayMs);
        }

        private void mark(double levelMs, double streamMs, double delayMs)
        {
            if (segment < 0 || closed) return;

            broken = false;

            // One point every 100 ms of the level is plenty: between them the take runs straight.
            if (!double.IsNaN(lastMarkLevelMs) && Math.Abs(levelMs - lastMarkLevelMs) < 100) return;

            lastMarkLevelMs = levelMs;
            points.Add(new Point(levelMs, segmentStartMs + streamMs + delayMs, segment));
        }

        private bool broken;

        /// <summary>
        /// The level stands still while the take goes on (the wait mode waits
        /// for the player): what is played meanwhile is on no point of the
        /// level, and the next mark starts a new stretch.
        /// </summary>
        public void Break()
        {
            lock (gate)
            {
                if (broken || segment < 0) return;

                broken = true;
                segment++;
                lastMarkLevelMs = double.NaN;
            }
        }

        /// <summary>
        /// Keep the take (off the update thread: it writes the file), with the
        /// level it belongs to and the play's score, and forget the oldest.
        /// Nothing to keep (no sound, no marks): nothing saved.
        /// </summary>
        public void Save(string forgeRoot, Guid set, long score)
        {
            float[] samples;
            List<Point> marked;

            // Closed from here: what is heard after the play ended is not part of it.
            lock (gate)
            {
                if (closed) return;

                closed = true;
                if (written < SAMPLE_RATE || points.Count < 2) return;

                samples = new float[written];
                int at = 0;

                foreach (var b in blocks)
                {
                    Array.Copy(b, 0, samples, at, b.Length);
                    at += b.Length;
                }

                Array.Copy(block, 0, samples, at, inBlock);
                marked = points.ToList();
                blocks.Clear();
            }

            // A quiet microphone made audible next to the music: to a fixed peak, at most +20 dB.
            float peak = samples.Max(MathF.Abs);
            float gain = peak > 0 ? Math.Min(10, 0.7f / peak) : 1;
            for (int i = 0; i < samples.Length; i++) samples[i] *= gain;

            string dir = Path.Combine(forgeRoot, folder);
            Directory.CreateDirectory(dir);

            MediaAudioExtractor.WriteWav(Path.Combine(dir, $"{Id}.wav"), samples, samples.Length, 1, SAMPLE_RATE);
            File.WriteAllText(Path.Combine(dir, $"{Id}.json"), JsonSerializer.Serialize(new Info(set, score, DateTime.UtcNow, marked)));

            Prune(dir);
        }

        /// <summary>A kept take's facts (its file is <c>{id}.wav</c> next to it).</summary>
        public sealed record Info(Guid Set, long Score, DateTime SavedAtUtc, List<Point> Points);

        /// <summary>Forget all but the last <see cref="KEPT_RECENT"/> takes and each level's best.</summary>
        public static void Prune(string dir)
        {
            var takes = new List<(string Id, Info Info)>();

            foreach (string json in Directory.GetFiles(dir, "*.json"))
            {
                try
                {
                    if (JsonSerializer.Deserialize<Info>(File.ReadAllText(json)) is { } info)
                        takes.Add((Path.GetFileNameWithoutExtension(json), info));
                }
                catch (Exception e) when (e is IOException or JsonException)
                {
                }
            }

            var keep = takes.OrderByDescending(t => t.Info.SavedAtUtc).Take(KEPT_RECENT).Select(t => t.Id).ToHashSet();

            foreach (var level in takes.GroupBy(t => t.Info.Set))
                keep.Add(level.OrderByDescending(t => t.Info.Score).ThenByDescending(t => t.Info.SavedAtUtc).First().Id);

            foreach (var (id, _) in takes.Where(t => !keep.Contains(t.Id)))
            {
                try
                {
                    File.Delete(Path.Combine(dir, $"{id}.wav"));
                    File.Delete(Path.Combine(dir, $"{id}.json"));
                }
                catch (IOException)
                {
                }
            }
        }

        /// <summary>A kept take, or null when it is gone (pruned, or the play was not recorded).</summary>
        public static (string WavPath, Info Info)? Find(string forgeRoot, int id)
        {
            string json = Path.Combine(forgeRoot, folder, $"{id}.json"), wav = Path.Combine(forgeRoot, folder, $"{id}.wav");
            if (!File.Exists(json) || !File.Exists(wav)) return null;

            try
            {
                return JsonSerializer.Deserialize<Info>(File.ReadAllText(json)) is { } info ? (wav, info) : null;
            }
            catch (Exception e) when (e is IOException or JsonException)
            {
                return null;
            }
        }

        /// <summary>
        /// Where in the take the level's time <paramref name="levelMs"/> was
        /// played, or null when nothing was (before listening, a pause, a wait).
        /// <paramref name="points"/> in the order they were marked.
        /// </summary>
        public static double? TakeAt(IReadOnlyList<Point> points, double levelMs)
        {
            // The last stretch covering it: a level played over (a seek back) is heard as played last.
            for (int i = points.Count - 2; i >= 0; i--)
            {
                var a = points[i];
                var b = points[i + 1];

                if (a.Segment != b.Segment || b.LevelMs <= a.LevelMs) continue;
                if (levelMs < a.LevelMs || levelMs > b.LevelMs) continue;

                return a.TakeMs + (b.TakeMs - a.TakeMs) * (levelMs - a.LevelMs) / (b.LevelMs - a.LevelMs);
            }

            return null;
        }

        /// <summary>A windowed-sinc low-pass just under the new rate's Nyquist frequency.</summary>
        private static float[] lowPass(int factor)
        {
            var k = new float[taps];
            double cutoff = 0.45 / factor; // of the source rate
            double sum = 0;

            for (int i = 0; i < taps; i++)
            {
                double x = i - (taps - 1) / 2.0;
                double sinc = x == 0 ? 2 * cutoff : Math.Sin(2 * Math.PI * cutoff * x) / (Math.PI * x);
                double window = 0.54 - 0.46 * Math.Cos(2 * Math.PI * i / (taps - 1));
                k[i] = (float)(sinc * window);
                sum += k[i];
            }

            for (int i = 0; i < taps; i++) k[i] = (float)(k[i] / sum);
            return k;
        }
    }
}
