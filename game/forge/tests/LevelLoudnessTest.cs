// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using Kan.Core.Media;
using Kan.Core.Tracks;
using Kan.Forge.Pipeline;

namespace Kan.Forge.Tests
{
    /// <summary>Every level's audio at the game's reference loudness (owner 2026-10-23: levels 12 dB apart).</summary>
    [TestFixture]
    public class LevelLoudnessTest
    {
        private string dir = null!;

        [SetUp]
        public void SetUp() => Directory.CreateDirectory(dir = Path.Combine(Path.GetTempPath(), $"kan-loudness-{Guid.NewGuid():N}"));

        [TearDown]
        public void TearDown() => Directory.Delete(dir, true);

        private string tone(double amplitude)
        {
            const int rate = 48000;
            var x = new float[rate * 4];
            for (int i = 0; i < x.Length; i++) x[i] = (float)(amplitude * Math.Sin(2 * Math.PI * 997 * i / rate));
            string path = Path.Combine(dir, $"tone-{amplitude}.wav");
            WavFile.Write(path, x, x.Length, 1, rate);
            return path;
        }

        [TestCase(0.01, -43.0)] // far too soft: comes up 23 dB
        [TestCase(0.99, -3.1)]  // full scale: goes down 17 dB
        public async Task ALevelIsRenderedAtTheReference(double amplitude, double measuredLufs)
        {
            string input = tone(amplitude);
            Assert.That(ForgeJobs.MeasureLoudness(input)!.Value.Lufs, Is.EqualTo(measuredLufs).Within(0.1));

            string output = Path.Combine(dir, "levelled.wav");
            var (before, after) = await ForgeJobs.NormalizeLoudnessAsync(input, LevelLoudness.TargetLufs(0), output);

            Assert.That(before, Is.EqualTo(measuredLufs).Within(0.1));
            Assert.That(after, Is.EqualTo(LevelLoudness.REFERENCE_LUFS).Within(0.1));
            Assert.That(ForgeJobs.MeasureLoudness(output)!.Value.Lufs, Is.EqualTo(LevelLoudness.REFERENCE_LUFS).Within(0.1), "as written");
        }

        [Test]
        public async Task TheCreatorSetsALevelFromTheReference()
        {
            string output = Path.Combine(dir, "louder.wav");
            var (_, after) = await ForgeJobs.NormalizeLoudnessAsync(tone(0.01), LevelLoudness.TargetLufs(+4), output);

            Assert.That(after, Is.EqualTo(LevelLoudness.REFERENCE_LUFS + 4).Within(0.1));
            Assert.That(LevelLoudness.TargetLufs(+40), Is.EqualTo(LevelLoudness.REFERENCE_LUFS + LevelLoudness.MAX_OFFSET_DB), "within the offset's range");
        }
    }
}
