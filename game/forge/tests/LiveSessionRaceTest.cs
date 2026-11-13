// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Kan.Forge.Play;

namespace Kan.Forge.Tests
{
    /// <summary>
    /// The native describer handle is owned by exactly one side (2026-11-12
    /// audit B14): Begin hands it to its worker, Dispose closes what it
    /// still holds. Racing the two (or two Disposes) used to close the same
    /// handle twice, freeing the describer twice. This is a race
    /// regression: before the fix it fails by aborting the run when the
    /// double close lands; after it the outcome is always one close.
    /// </summary>
    [TestFixture]
    public class LiveSessionRaceTest
    {
        /// <summary>A silent microphone stand-in.</summary>
        private sealed class SilentMic : ILiveSource
        {
            public int SampleRate => 48000;

            public bool Start(out string error)
            {
                error = string.Empty;
                return true;
            }

            public int Read(float[] into) => 0;

            public void SkipArrived()
            {
            }

            public void Dispose()
            {
            }
        }

        [Test]
        public void BeginAndDisposeRacingCloseTheDescriberOnce()
        {
            for (int i = 0; i < 100; i++)
            {
                var session = new LiveSession(new SilentMic(), @"ocarina");
                Assert.That(session.Open(), Is.True);

                race(session.Begin, session.Dispose);
                session.Dispose(); // quiesce whatever the race left running
            }
        }

        [Test]
        public void TwoDisposesAtOnceAreOneDispose()
        {
            for (int i = 0; i < 100; i++)
            {
                var session = new LiveSession(new SilentMic(), @"ocarina");
                Assert.That(session.Open(), Is.True);

                race(session.Dispose, session.Dispose);
                session.Dispose();
            }
        }

        private static void race(Action a, Action b)
        {
            using var start = new Barrier(2);
            var first = Task.Run(() => { start.SignalAndWait(); a(); });
            var second = Task.Run(() => { start.SignalAndWait(); b(); });
            Task.WaitAll(first, second);
        }
    }
}
