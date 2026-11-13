// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.IO;
using NUnit.Framework;
using Kan.Core.Media;
using Kan.Core.Native;

namespace Kan.Core.Tests
{
    /// <summary>
    /// Smoke test of the C-ABI bridge as seen from C# (P/Invoke through
    /// libkan_forge_native): the call path, the status codes and the error
    /// messages. The heavy DSP behaviour is covered by the native self-test
    /// (game/forge/native/kan_forge_selftest.cpp, known input -> expected
    /// output).
    /// </summary>
    [TestFixture]
    public class NativeBridgeSmokeTest
    {
        [Test]
        public void TestVersionAndCallPathWork()
        {
            string version = KanForgeNative.Version();
            Assert.That(version, Does.Contain("kan_forge_api"), "the native library is loaded and answers");
        }

        [Test]
        public void TestMissingFileIsAFirstClassDecodeError()
        {
            var err = new byte[512];
            KanForgeStatus st = KanForgeNative.TranscribeFile("definitely-not-here.wav", "keyboard",
                                                             new KanForgeNote[4], 4, out int count,
                                                             err, err.Length);

            Assert.That(st, Is.EqualTo(KanForgeStatus.ErrDecode));
            Assert.That(count, Is.EqualTo(0));
            Assert.That(KanForgeNative.ErrMessage(err), Does.Contain("decode"));
        }

        [Test]
        public void TestMissingSeparatorReportsRecoverableError()
        {
            // Separation depends on the compiled RoFormer runtime (libkan
            // KAN_ENABLE_ROFORMER) plus its GGUF model. Whatever the machine
            // provides, the bridge must answer with a controlled, human-readable
            // status, never a crash.
            var err = new byte[512];
            string outDir = Path.Combine(Path.GetTempPath(), "kan-forge-test-stems");
            KanForgeStatus st = KanForgeNative.SeparateStems("definitely-not-here.wav", outDir,
                                                             err, err.Length);

            if (!KanForgeNative.HasSeparator())
            {
                Assert.That(st, Is.EqualTo(KanForgeStatus.ErrSeparator));
                Assert.That(KanForgeNative.ErrMessage(err), Does.Contain("separator"));
            }
            else
            {
                // Separator present on this machine: a bogus input must still
                // produce a first-class error (no stems), not undefined behaviour.
                Assert.That(st, Is.EqualTo(KanForgeStatus.ErrSeparator));
                Assert.That(KanForgeNative.ErrMessage(err), Is.Not.Empty);
            }
        }

        [Test]
        public void TestEnvironmentProbesAreCallable()
        {
            // UI enablement probes: side-effect free, prompt answers.
            bool separator = false, downloader = false;
            Assert.DoesNotThrow(() =>
            {
                separator = KanForgeNative.HasSeparator();
                downloader = KanForgeNative.HasDownloader();
            });
            TestContext.Progress.WriteLine($"separator={separator} downloader={downloader}");
        }

        [Test]
        public void TestProgressHookIsInstalledAndSafe()
        {
            // Installing and clearing the progress hook must not throw and
            // must survive being switched at runtime (the runner does this
            // per job).
            KanForgeNative.SetProgress((_, _, _) => { }, IntPtr.Zero);
            KanForgeNative.SetProgress(null, IntPtr.Zero);
            Assert.Pass();
        }

        [Test]
        public void TestAnUnboundedTrackOffsetIsRefusedNotFatal()
        {
            // 2026-11-12 audit B9: a track offset sizes the whole mix buffer
            // at once. A set.json with "OffsetMs": 1e15 made the bridge
            // allocate the whole timeline in one call; the resulting
            // bad_alloc crossed the C ABI and took the process down. The
            // call must answer with an error instead. (Before the fix this
            // test aborts the run, which is the bug.)
            string input = writeSine(@"mix-input");
            string outWav = Path.Combine(Path.GetTempPath(), "kan-forge-tests", @"mix-" + Guid.NewGuid().ToString("N")[..8] + ".wav");
            var err = new byte[512];

            KanForgeStatus st = KanForgeNative.MixTracksAt(new[] { input }, new[] { 1f }, new[] { 1e15 }, 1, outWav, err, err.Length);

            Assert.That(st, Is.EqualTo(KanForgeStatus.ErrArgs));
            Assert.That(KanForgeNative.ErrMessage(err), Does.Contain("offset"));
            Assert.That(File.Exists(outWav), Is.False);
        }

        [Test]
        public void TestMixingAtAnOffsetStillWorks()
        {
            string input = writeSine(@"mix-input");
            string outWav = Path.Combine(Path.GetTempPath(), "kan-forge-tests", @"mix-" + Guid.NewGuid().ToString("N")[..8] + ".wav");
            var err = new byte[512];

            KanForgeStatus st = KanForgeNative.MixTracksAt(new[] { input, input }, new[] { 0.5f, 0.4f }, new[] { 0.0, 10.0 }, 2, outWav, err, err.Length);

            Assert.That(st, Is.EqualTo(KanForgeStatus.Ok), KanForgeNative.ErrMessage(err));
            Assert.That(File.Exists(outWav), Is.True);
        }

        /// <summary>A short sine as a WAV file (what the mixer decodes).</summary>
        private static string writeSine(string name)
        {
            string path = Path.Combine(Path.GetTempPath(), "kan-forge-tests",
                                       name + "-" + Guid.NewGuid().ToString("N")[..8] + ".wav");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            int n = 48000 / 5;
            var samples = new float[n];
            for (int i = 0; i < n; i++)
                samples[i] = 0.5f * MathF.Sin(2 * MathF.PI * 440 * i / 48000);

            WavFile.Write(path, samples, n, 1, 48000);
            return path;
        }
    }
}
