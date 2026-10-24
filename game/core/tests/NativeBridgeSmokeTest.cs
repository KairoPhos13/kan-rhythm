// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using NUnit.Framework;
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
            // Separation depends on external tooling (bs_roformer-cli + GGUF
            // model). Whatever the machine provides, the bridge must answer
            // with a controlled, human-readable status — never a crash.
            var err = new byte[512];
            KanForgeStatus st = KanForgeNative.SeparateStems("whatever.wav", "/tmp/kan-forge-test-stems",
                                                            err, err.Length);

            if (!KanForgeNative.HasSeparator())
            {
                Assert.That(st, Is.EqualTo(KanForgeStatus.ErrSeparator));
                Assert.That(KanForgeNative.ErrMessage(err), Does.Contain("bs_roformer"));
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
    }
}
