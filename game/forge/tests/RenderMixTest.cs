// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Kan.Forge.Pipeline;
using Kan.Forge.Tracks;

namespace Kan.Forge.Tests
{
    /// <summary>
    /// "Render a single track for playback" (owner requirement 2026-10-13):
    /// the track set mixes into one WAV — gains apply, muted tracks are
    /// excluded, the result covers the longest input.
    /// </summary>
    [TestFixture]
    public class RenderMixTest
    {
        private const int sample_rate = 48000;

        private static string writeSine(string name, float seconds, float amplitude)
        {
            string path = Path.Combine(Path.GetTempPath(), "kan-forge-tests",
                                       name + "-" + Guid.NewGuid().ToString("N")[..8] + ".wav");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            int n = (int)(seconds * sample_rate);
            using var f = File.Create(path);
            using var w = new BinaryWriter(f);

            w.Write("RIFF"u8.ToArray());
            w.Write(36 + n * 2);
            w.Write("WAVEfmt "u8.ToArray());
            w.Write(16);
            w.Write((ushort)1); w.Write((ushort)1);
            w.Write(sample_rate);
            w.Write(sample_rate * 2);
            w.Write((ushort)2); w.Write((ushort)16);
            w.Write("data"u8.ToArray());
            w.Write(n * 2);

            for (int i = 0; i < n; i++)
            {
                float s = amplitude * MathF.Sin(2 * MathF.PI * 440 * i / sample_rate);
                w.Write((short)(s * 32767));
            }

            return path;
        }

        private static (int length, float peak) readWav(string path)
        {
            using var f = File.OpenRead(path);
            using var r = new BinaryReader(f);
            f.Seek(44, SeekOrigin.Begin);

            float peak = 0;
            int count = 0;
            while (f.Position + 1 < f.Length)
            {
                short pcm = r.ReadInt16();
                float s = Math.Abs(pcm / 32767f);
                if (s > peak) peak = s;
                count++;
            }

            return (count, peak);
        }

        [Test]
        public async Task TestMixRespectsGainsAndMutes()
        {
            string a = writeSine("a", 0.2f, 0.4f);
            string b = writeSine("b", 0.2f, 0.4f);
            string loud = writeSine("loud", 1.0f, 0.9f);
            string outWav = Path.Combine(Path.GetDirectoryName(a)!, "mix-out.wav");

            var tracks = new List<ForgeTrack>
            {
                new ForgeTrack("a", a) { Gain = 0.5f },
                new ForgeTrack("b", b) { Gain = 0.5f },
                new ForgeTrack("loud", loud) { Gain = 2.0f, Muted = true },
            };

            string result = await ForgeJobs.RenderMixAsync(tracks, outWav);

            Assert.That(result, Is.EqualTo(outWav));
            Assert.That(File.Exists(outWav), Is.True);

            var (length, peak) = readWav(outWav);

            // The muted 1-second track must NOT extend the mix.
            Assert.That(length, Is.EqualTo((int)(0.2f * sample_rate)).Within(32),
                        "muted tracks are excluded from the render");

            // 0.5 + 0.5 of an identical 0.4-amplitude signal ≈ 0.4.
            Assert.That(peak, Is.EqualTo(0.4f).Within(0.02f),
                        "gains apply: two halves sum back to the source level");
        }

        [Test]
        public async Task TestMixPlacesTracksOnTheTimeline()
        {
            string a = writeSine("a", 0.2f, 0.4f);
            string b = writeSine("b", 0.2f, 0.4f);
            string outWav = Path.Combine(Path.GetDirectoryName(a)!, "mix-at-out.wav");

            var tracks = new List<ForgeTrack>
            {
                new ForgeTrack("a", a),
                new ForgeTrack("b", b) { OffsetMs = 500 },
            };

            await ForgeJobs.RenderMixAsync(tracks, outWav);

            var (length, peak) = readWav(outWav);

            // b starts at 0.5 s: the mix spans max(start + length) = 0.7 s.
            Assert.That(length, Is.EqualTo((int)(0.7f * sample_rate)).Within(32),
                        "the render covers the moved track's end");

            // The tracks don't overlap in time, so nothing sums: still 0.4.
            Assert.That(peak, Is.EqualTo(0.4f).Within(0.02f),
                        "moved tracks are not stacked on the others");
        }

        [Test]
        public async Task TestMixPlaysOnlyTheTrimmedPart()
        {
            string a = writeSine("a", 1.0f, 0.4f);
            string outWav = Path.Combine(Path.GetDirectoryName(a)!, "mix-clip-out.wav");

            // Plays file time [0.2 s, 0.5 s) and the track starts at 1 s:
            // the trimmed audio stays in place, i.e. at [1.2 s, 1.5 s).
            var tracks = new List<ForgeTrack>
            {
                new ForgeTrack("a", a) { OffsetMs = 1000, ClipStartMs = 200, ClipEndMs = 500 },
            };

            await ForgeJobs.RenderMixAsync(tracks, outWav);

            var (length, peak) = readWav(outWav);
            Assert.That(length, Is.EqualTo((int)(1.5f * sample_rate)).Within(32), "the render ends where the trimmed part ends");
            Assert.That(peak, Is.EqualTo(0.4f).Within(0.02f));
            Assert.That(Directory.GetFiles(Path.GetDirectoryName(outWav)!, "prep-*.wav"), Is.Empty, "temporary clips are cleaned up");
        }

        [Test]
        public async Task TestTrackRendersAsHeard()
        {
            // 1 s tone; keep [0.1, 0.2) and [0.6, 0.7), trim the sides to
            // [0.05, 0.9): the saved file is 0.85 s long, starts 0.05 s later,
            // and is silent between the kept parts.
            string a = writeSine("heard", 1.0f, 0.4f);
            string outWav = Path.Combine(Path.GetDirectoryName(a)!, "heard-out.wav");

            var track = new ForgeTrack("heard", a) { OffsetMs = 1000, ClipStartMs = 50, ClipEndMs = 900 };
            track.Edits.Ranges.Add(new PendingRange(Kan.Forge.Native.KanForgeEditMode.Keep, 100, 200));
            track.Edits.Ranges.Add(new PendingRange(Kan.Forge.Native.KanForgeEditMode.Keep, 600, 700));

            double start = await ForgeJobs.RenderTrackAsync(track, outWav);

            Assert.That(start, Is.EqualTo(1050), "the result starts where its audio sits on the timeline");

            var (length, peak) = readWav(outWav);
            Assert.That(length, Is.EqualTo((int)(0.85f * sample_rate)).Within(48));
            Assert.That(peak, Is.EqualTo(0.4f).Within(0.02f));
            Assert.That(peakBetween(outWav, 0.20f, 0.50f), Is.LessThan(0.001f), "between the kept parts is silent");
            Assert.That(peakBetween(outWav, 0.06f, 0.14f), Is.GreaterThan(0.3f), "the first kept part is heard");
            Assert.That(peakBetween(outWav, 0.56f, 0.64f), Is.GreaterThan(0.3f), "the second kept part is heard too");
        }

        /// <summary>Peak of a mono PCM16 WAV between two times (seconds).</summary>
        private static float peakBetween(string path, float from, float to)
        {
            var bytes = File.ReadAllBytes(path);
            float peak = 0;

            for (int i = (int)(from * sample_rate); i < (int)(to * sample_rate); i++)
            {
                int at = 44 + i * 2;
                if (at + 1 >= bytes.Length) break;
                peak = Math.Max(peak, Math.Abs(BitConverter.ToInt16(bytes, at) / 32767f));
            }

            return peak;
        }

        [Test]
        public void TestEmptyTrackSetIsAFriendlyError()
        {
            var ex = Assert.ThrowsAsync<ForgeException>(async ()
                => await ForgeJobs.RenderMixAsync(Enumerable.Empty<ForgeTrack>(), "/tmp/unused.wav"));
            Assert.That(ex!.Message, Does.Contain("no audible tracks"));
        }
    }
}
