// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.IO;
using NUnit.Framework;
using Kan.Forge.Media;
using Kan.Core.Media;

namespace Kan.Forge.Tests
{
    /// <summary>
    /// Video/audio merge (owner requirement 2026-10-13): the audio inside a
    /// media container is extracted to a WAV the pipeline can process. The
    /// BASS decode path is exercised with a known WAV here; video containers
    /// go through the same call (covered by the acceptance pass).
    /// </summary>
    [TestFixture]
    public class MediaExtractionTest
    {
        [Test]
        public void TestExtractProducesAUsableWav()
        {
            string src = Path.Combine(Path.GetTempPath(), "kan-forge-tests",
                                      "extract-src-" + Guid.NewGuid().ToString("N")[..8] + ".wav");
            Directory.CreateDirectory(Path.GetDirectoryName(src)!);

            // 0.2 s of 440 Hz at 22050 Hz mono (deliberately not 48 kHz: the
            // bridge resamples; extraction must preserve the source format).
            using (var f = File.Create(src))
            using (var w = new BinaryWriter(f))
            {
                int n = (int)(0.2f * 22050);
                w.Write("RIFF"u8.ToArray());
                w.Write(36 + n * 2);
                w.Write("WAVEfmt "u8.ToArray());
                w.Write(16);
                w.Write((ushort)1); w.Write((ushort)1);
                w.Write(22050);
                w.Write(22050 * 2);
                w.Write((ushort)2); w.Write((ushort)16);
                w.Write("data"u8.ToArray());
                w.Write(n * 2);
                for (int i = 0; i < n; i++)
                    w.Write((short)(0.5f * short.MaxValue * MathF.Sin(2 * MathF.PI * 440 * i / 22050)));
            }

            string outWav = Path.Combine(Path.GetDirectoryName(src)!, "extract-out.wav");

            bool ok = MediaAudioExtractor.TryExtractToWav(src, outWav, out string error);

            Assert.That(ok, Is.True, error);
            Assert.That(File.Exists(outWav), Is.True);

            using var rf = File.OpenRead(outWav);
            Assert.That(rf.Length, Is.EqualTo(44 + (int)(0.2f * 22050) * 2).Within(64),
                        "PCM16 wav of the source's length");
        }

        [Test]
        public void TestMissingMediaIsAControlledError()
        {
            bool ok = MediaAudioExtractor.TryExtractToWav("/tmp/definitely-not-here.mp4",
                                                          "/tmp/out.wav", out string error);
            Assert.That(ok, Is.False);
            Assert.That(error, Is.Not.Empty);
        }

        /// <summary>
        /// Pitch shift (owner 2026-10-19: a companion moved about an octave to
        /// the instrument's range): 440 Hz an octave up is 880 Hz, down 220 Hz,
        /// and the length — the speed — is kept.
        /// </summary>
        [TestCase(12, 880.0)]
        [TestCase(-12, 220.0)]
        public void TestPitchShiftKeepsTheSpeed(int semitones, double expectedHz)
        {
            const int rate = 44100;
            const int frames = rate * 2;

            string dir = Path.Combine(Path.GetTempPath(), "kan-forge-tests");
            string src = Path.Combine(dir, "pitch-src-" + Guid.NewGuid().ToString("N")[..8] + ".wav");
            string outWav = Path.Combine(dir, "pitch-out-" + Guid.NewGuid().ToString("N")[..8] + ".wav");

            var tone = new float[frames];
            for (int i = 0; i < frames; i++)
                tone[i] = 0.5f * MathF.Sin(2 * MathF.PI * 440 * i / rate);
            WavFile.Write(src, tone, frames, 1, rate);

            bool ok = MediaAudioExtractor.TryPitchShiftToWav(src, outWav, semitones, out string error);
            Assert.That(ok, Is.True, error);

            var shifted = readPcm16(outWav);
            Assert.That(shifted.Length, Is.EqualTo(frames).Within(rate / 20), "same length: the speed is unchanged");

            // Count rising zero crossings over the steady middle second.
            int crossings = 0;
            for (int i = rate / 2 + 1; i < rate * 3 / 2; i++)
            {
                if (shifted[i - 1] < 0 && shifted[i] >= 0)
                    crossings++;
            }

            Assert.That(crossings, Is.EqualTo(expectedHz).Within(expectedHz * 0.03));
        }

        private static float[] readPcm16(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            var samples = new float[(bytes.Length - 44) / 2];
            for (int i = 0; i < samples.Length; i++)
                samples[i] = BitConverter.ToInt16(bytes, 44 + i * 2) / 32768f;
            return samples;
        }

        [Test]
        public void TestVideoDetection()
        {
            Assert.That(MediaAudioExtractor.IsVideo("clip.mp4"), Is.True);
            Assert.That(MediaAudioExtractor.IsVideo("song.mp3"), Is.False);
            Assert.That(MediaAudioExtractor.CanExtract("clip.webm"), Is.True);
        }
    }
}
