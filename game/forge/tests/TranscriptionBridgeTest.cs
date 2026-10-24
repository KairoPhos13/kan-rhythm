// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.IO;
using System.Linq;
using Kan.Core.Native;
using NUnit.Framework;
using Kan.Forge.Media;
using Kan.Forge.Pipeline;
using Kan.Core.Media;

namespace Kan.Forge.Tests
{
    /// <summary>
    /// The transcription crossing into C#: the native selftest checks the
    /// notes on the native side, this checks they arrive in C#. Regression
    /// (2026-10-14): without [In, Out] on the notes array, every note reached
    /// C# as zeros (right count, empty values).
    /// </summary>
    [TestFixture]
    public class TranscriptionBridgeTest
    {
        /// <summary>The native selftest's melody: C5 D5 E5 G5 at 0.3 / 1.0 / 1.7 / 2.4 s.</summary>
        private static string writeMelody()
        {
            const int rate = 48000;
            var sig = new float[(int)(3.2f * rate)];
            (float t, float d, float hz, float vib)[] perf = { (0.3f, 0.5f, 523.25f, 0), (1.0f, 0.5f, 587.33f, 0), (1.7f, 0.5f, 659.25f, 0), (2.4f, 0.6f, 783.99f, 5) };

            foreach (var p in perf)
            {
                int at = (int)(p.t * rate), n = (int)(p.d * rate);

                for (int i = 0; at + i < sig.Length && i < n; i++)
                {
                    float t = (float)i / rate;
                    float dev = p.vib > 0 ? p.hz * 0.03f / p.vib * MathF.Sin(6.2831853f * p.vib * t) : 0;
                    float s = 0;
                    for (int h = 1; h <= 5; h++) s += MathF.Sin(6.2831853f * (p.hz + dev) * h * t) / h;
                    float env = i < n / 50 ? (float)i / (n / 50) : i > n - n / 50 ? (float)(n - i) / (n / 50) : 1;
                    sig[at + i] += 0.25f * s * env;
                }
            }

            string path = Path.Combine(Path.GetTempPath(), "kan-forge-tests", Guid.NewGuid().ToString("N")[..8], "melody.wav");
            WavFile.Write(path, sig, sig.Length, 1, rate);
            return path;
        }

        [Test]
        public void TestNotesArriveInCSharp()
        {
            var draft = ForgeJobs.TranscribeAsync(writeMelody(), "keyboard").GetAwaiter().GetResult();

            Assert.That(draft.Notes, Is.Not.Empty);
            Assert.That(draft.Notes.Any(n => n.OnsetMs > 0), Is.True, "onsets arrive (not all zeros)");

            var pitched = draft.Notes.Where(n => n.Midi >= 0).ToList();
            foreach (var (ms, midi) in new[] { (300, 72), (1000, 74), (1700, 76), (2400, 79) })
            {
                Assert.That(pitched.Any(n => n.Midi == midi && Math.Abs(n.OnsetMs - ms) < 80), Is.True,
                    $"{midi} near {ms} ms (got: {string.Join(", ", pitched.Select(n => $"{n.Midi}@{n.OnsetMs:0}"))})");
            }

            Assert.That(pitched.All(n => n.DurMs > 0), Is.True, "durations arrive");
        }

        [Test]
        public void TestBothTranscribersHearTheMelody()
        {
            // The editor transcribes with Basic Pitch; the mic calibration hears as the live judge.
            string melody = writeMelody();

            foreach (string engine in new[] { KanForgeNative.ENGINE_NEURAL, KanForgeNative.ENGINE_DESCRIBER })
            {
                var draft = ForgeJobs.TranscribeAsync(melody, "ocarina", engine: engine).GetAwaiter().GetResult();
                var pitched = draft.Notes.Where(n => n.Midi >= 0).ToList();

                foreach (var (ms, midi) in new[] { (300, 72), (1000, 74), (1700, 76), (2400, 79) })
                {
                    Assert.That(pitched.Any(n => n.Midi == midi && Math.Abs(n.OnsetMs - ms) < 80), Is.True,
                        $"{engine}: {midi} near {ms} ms (got: {string.Join(", ", pitched.Select(n => $"{n.Midi}@{n.OnsetMs:0}"))})");
                }
            }

            Assert.ThrowsAsync<ForgeException>(async () => await ForgeJobs.TranscribeAsync(melody, "ocarina", engine: "nonsense"),
                "an unknown engine is refused");
        }

        [Test]
        public void TestNativeErrorMessagesArriveInCSharp()
        {
            var ex = Assert.ThrowsAsync<ForgeException>(async () => await ForgeJobs.TranscribeAsync("/does/not/exist.wav", "keyboard"));
            Assert.That(ex!.Message, Is.Not.Empty, "the native error text is copied back");
        }
    }
}
