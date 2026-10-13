// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.IO;
using NUnit.Framework;
using Kan.Forge.Media;

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

        [Test]
        public void TestVideoDetection()
        {
            Assert.That(MediaAudioExtractor.IsVideo("clip.mp4"), Is.True);
            Assert.That(MediaAudioExtractor.IsVideo("song.mp3"), Is.False);
            Assert.That(MediaAudioExtractor.CanExtract("clip.webm"), Is.True);
        }
    }
}
