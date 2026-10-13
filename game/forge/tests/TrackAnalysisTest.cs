// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Kan.Forge.Tracks;

namespace Kan.Forge.Tests
{
    /// <summary>
    /// Owner rules (2026-10-13): after separation, stems that seem to carry
    /// no meaningful audio are labelled and moved to the end of the list —
    /// but never removed (the user may know better). The waveform preview
    /// comes from the same analysis pass.
    /// </summary>
    [TestFixture]
    public class TrackAnalysisTest
    {
        private static string writeSignal(string name, float amplitude)
        {
            string path = Path.Combine(Path.GetTempPath(), "kan-forge-tests",
                                       name + "-" + Guid.NewGuid().ToString("N")[..8] + ".wav");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            int n = (int)(0.5f * 48000);
            using var f = File.Create(path);
            using var w = new BinaryWriter(f);

            w.Write("RIFF"u8.ToArray());
            w.Write(36 + n * 2);
            w.Write("WAVEfmt "u8.ToArray());
            w.Write(16);
            w.Write((ushort)1); w.Write((ushort)1);
            w.Write(48000);
            w.Write(48000 * 2);
            w.Write((ushort)2); w.Write((ushort)16);
            w.Write("data"u8.ToArray());
            w.Write(n * 2);
            for (int i = 0; i < n; i++)
                w.Write((short)(amplitude * short.MaxValue * MathF.Sin(2 * MathF.PI * 440 * i / 48000)));

            return path;
        }

        [Test]
        public void TestMeaningfulAudioRule()
        {
            Assert.That(TrackAnalysis.HasMeaningfulAudio(0.1f, 0.05f), Is.True);
            Assert.That(TrackAnalysis.HasMeaningfulAudio(0.001f, 0.0001f), Is.False);
            Assert.That(TrackAnalysis.HasMeaningfulAudio(0.5f, 0.0001f), Is.False,
                        "a click without body is not meaningful content");
            Assert.That(TrackAnalysis.HasMeaningfulAudio(0.001f, 0.5f), Is.False);
        }

        [Test]
        public void TestEmptyTracksGoToTheEndOfTheList()
        {
            var set = new ForgeTrackSet();
            var a = set.Import("a", "/tmp/a.wav");
            var quiet = set.Import("quiet", "/tmp/quiet.wav");
            var b = set.Import("b", "/tmp/b.wav");

            quiet.LikelyEmpty = true;

            set.OrderByMeaning();

            Assert.That(set.Tracks.Select(t => t.Name), Is.EqualTo(new[] { "a", "b", "quiet" }),
                        "meaningful tracks keep their order; empty ones are demoted to the end");
        }

        [Test]
        public void TestAnalysisFeedsWaveformAndMarking()
        {
            var loud = new ForgeTrack("loud", writeSignal("loud", 0.5f));
            var silent = new ForgeTrack("silent", writeSignal("silent", 0.0f));

            TrackAnalysis.Analyze(loud);
            TrackAnalysis.Analyze(silent);

            Assert.That(loud.Peaks, Is.Not.Null);
            Assert.That(loud.Peaks!.Length, Is.EqualTo(TrackAnalysis.BUCKETS));
            Assert.That(loud.Peaks.Max(), Is.GreaterThan(0.4f), "the waveform shows the activity");
            Assert.That(loud.LikelyEmpty, Is.False);

            Assert.That(silent.Peaks, Is.Not.Null);
            Assert.That(silent.Peaks!.Max(), Is.EqualTo(0.0f));
            Assert.That(silent.LikelyEmpty, Is.True, "a silent stem is labelled, not hidden");
        }
    }
}
