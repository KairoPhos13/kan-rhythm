// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using Kan.Forge.Pipeline;

namespace Kan.Forge.Tests
{
    /// <summary>
    /// End-to-end separation through the bridge (validation of libkan's
    /// self-contained RoFormer runtime, 2026-10-13): a known clip must
    /// separate into the 6 stems in official order with nothing installed by
    /// the user beyond the auto-downloaded model.
    /// </summary>
    [TestFixture]
    public class SeparationTest
    {
        [Test]
        public async Task TestSeparationProducesTheSixStems()
        {
            if (!Kan.Core.Native.KanForgeNative.HasSeparator())
                Assert.Ignore("no separator runtime in this build");

            // 1 s of a 440 Hz tone is enough to exercise the whole path.
            string src = Path.Combine(Path.GetTempPath(), "kan-forge-tests",
                                      "sep-src-" + Guid.NewGuid().ToString("N")[..8] + ".wav");
            Directory.CreateDirectory(Path.GetDirectoryName(src)!);
            await File.WriteAllBytesAsync(src, buildSineWav(1.0f));

            string outDir = Path.Combine(Path.GetTempPath(), "kan-forge-tests",
                                         "sep-out-" + Guid.NewGuid().ToString("N")[..8]);

            string stemDir = await ForgeJobs.SeparateStemsAsync(src, outDir);

            Assert.That(Directory.Exists(stemDir), Is.True, "the stem directory is returned");
            foreach (string expected in new[] { "bass", "drums", "guitar", "other", "piano", "vocals" })
                Assert.That(File.Exists(Path.Combine(stemDir, expected + ".wav")), Is.True,
                            $"stem '{expected}' exists in official order/layout");
        }

        [Test]
        public void TestMissingRuntimeIsAControlledError()
        {
            // Whatever the machine provides, the bridge answers with a
            // controlled status and a human-readable message.
            var err = new byte[512];
            var st = Kan.Core.Native.KanForgeNative.SeparateStems(
                Path.Combine(Path.GetTempPath(), "kan-forge-tests", "does-not-exist.wav"),
                Path.Combine(Path.GetTempPath(), "kan-forge-tests", "sep-stems"),
                err, err.Length);
            Assert.That(st, Is.EqualTo(Kan.Core.Native.KanForgeStatus.ErrSeparator).Or.EqualTo(Kan.Core.Native.KanForgeStatus.Ok),
                        "controlled statuses only, never a crash");
        }

        private static byte[] buildSineWav(float seconds)
        {
            int n = (int)(seconds * 48000);
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);

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
                w.Write((short)(0.5f * short.MaxValue * MathF.Sin(2 * MathF.PI * 440 * i / 48000)));

            return ms.ToArray();
        }
    }
}
