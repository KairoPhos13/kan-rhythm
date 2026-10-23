// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Kan.Forge.Play;

namespace Kan.Forge.Tests
{
    /// <summary>
    /// A live session opened off the game's thread and watched for a source
    /// that stops giving sound (owner 2026-10-23: with the disk saturated, a
    /// restart froze 5 s opening the microphone, which then stopped 1.6 s in).
    /// </summary>
    [TestFixture]
    public class LiveSessionTest
    {
        /// <summary>A microphone stand-in: silence in real time while it flows, slow to open if asked.</summary>
        private sealed class FakeMic : ILiveSource
        {
            public volatile bool Flowing = true;
            public int OpenMs;
            public int Skipped;
            private readonly Stopwatch clock = new Stopwatch();
            private long given;

            public int SampleRate => 48000;

            public bool Start(out string error)
            {
                error = string.Empty;
                Thread.Sleep(OpenMs);
                clock.Start();
                return true;
            }

            public int Read(float[] into)
            {
                if (!Flowing) return 0;

                long due = clock.ElapsedMilliseconds * 48;
                int count = (int)System.Math.Min(into.Length, due - given);
                if (count <= 0) return 0;

                System.Array.Clear(into, 0, count);
                given += count;
                return count;
            }

            public void SkipArrived()
            {
                Skipped++;
                given = clock.ElapsedMilliseconds * 48;
            }

            public void Dispose()
            {
            }
        }

        [Test]
        public void ASlowSourceOpensOffTheGamesThreadAndIsHeardFromWhenItBegins()
        {
            var mic = new FakeMic { OpenMs = 300 };
            var session = new LiveSession(mic, @"ocarina");

            var opening = Task.Run(session.Open);
            Assert.That(opening.IsCompleted, Is.False, "opening does not hold the caller");
            Assert.That(opening.Wait(5000) && opening.Result, Is.True);

            session.Begin();
            Assert.That(mic.Skipped, Is.EqualTo(1), "what arrived while opening is dropped");

            Thread.Sleep(200);
            Assert.That(session.SilentForMs, Is.LessThan(100), "a flowing source");

            mic.Flowing = false;
            Thread.Sleep(400);
            Assert.That(session.SilentForMs, Is.GreaterThan(300), "a source that stopped giving sound");

            session.Dispose();
        }

        [Test]
        public void AnOpenedSessionNeverBegunCloses()
        {
            var session = new LiveSession(new FakeMic(), @"ocarina");
            Assert.That(session.Open(), Is.True);
            Assert.DoesNotThrow(session.Dispose);
        }
    }
}
