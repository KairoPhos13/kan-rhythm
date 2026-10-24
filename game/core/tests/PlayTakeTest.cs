// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Kan.Core.Play;

namespace Kan.Core.Tests
{
    /// <summary>A play's recording, heard in its replay (owner 2026-10-22).</summary>
    [TestFixture]
    public class PlayTakeTest
    {
        private static string tempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "kan-forge-tests", "takes-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(dir);
            return dir;
        }

        /// <summary>Feed <paramref name="seconds"/> of a 440 Hz tone at 48 kHz, in 10 ms blocks.</summary>
        private static void play(PlayTake take, double seconds)
        {
            var block = new float[480];

            for (int b = 0; b < seconds * 100; b++)
            {
                for (int i = 0; i < block.Length; i++)
                    block[i] = 0.1f * MathF.Sin(2 * MathF.PI * 440 * (b * 480 + i) / 48000f);
                take.Tap(block, block.Length);
            }
        }

        [Test]
        public void TheTakeIsPlacedWhereTheLevelWas()
        {
            var take = new PlayTake();
            Assert.That(take.Begin(48000), Is.True);

            // The level at 5 s when listening starts, the microphone 40 ms behind.
            for (int ms = 0; ms <= 2000; ms += 16)
                take.Mark(5000 + ms, ms, 40);
            play(take, 2);

            Assert.That(take.LengthMs, Is.EqualTo(2000).Within(1), "a third of the samples: 16 kHz");

            // A pause: a new session, the level where it stopped.
            take.Begin(48000);
            for (int ms = 0; ms <= 1000; ms += 16)
                take.Mark(7000 + ms, ms, 40);
            play(take, 1);

            string dir = tempDir();
            take.Save(dir, Guid.NewGuid(), 1000);

            var found = PlayTake.Find(dir, take.Id);
            Assert.That(found, Is.Not.Null);

            var points = found!.Value.Info.Points;
            Assert.That(PlayTake.TakeAt(points, 6000), Is.EqualTo(1040).Within(20), "a second in, plus the microphone's delay");
            Assert.That(PlayTake.TakeAt(points, 7500), Is.EqualTo(2540).Within(20), "after the pause: in the second stretch");
            Assert.That(PlayTake.TakeAt(points, 4000), Is.Null, "before listening: nothing played");
            Assert.That(new FileInfo(found.Value.WavPath).Length, Is.EqualTo(44 + 3 * 16000 * 2).Within(100));
        }

        [Test]
        public void AWaitIsOnNoPointOfTheLevel()
        {
            var take = new PlayTake();
            take.Begin(48000);

            for (int ms = 0; ms <= 1000; ms += 16) take.Mark(1000 + ms, ms, 0);
            take.Break(); // the level waits at 2 s for 3 s
            take.Break();
            for (int ms = 4000; ms <= 5000; ms += 16) take.Mark(2000 + ms - 4000, ms, 0);

            var points = typeof(PlayTake).GetField("points", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(take) as System.Collections.Generic.List<PlayTake.Point>;
            Assert.That(PlayTake.TakeAt(points!, 2500), Is.EqualTo(4500).Within(20), "after the wait: where it went on");
            Assert.That(PlayTake.TakeAt(points!, 1500), Is.EqualTo(500).Within(20));
        }

        [Test]
        public void TheLastPlaysAndEachLevelsBestAreKept()
        {
            string dir = tempDir();
            Guid levelA = Guid.NewGuid(), levelB = Guid.NewGuid();

            PlayTake save(Guid level, long score)
            {
                var take = new PlayTake();
                take.Begin(48000);
                take.Mark(0, 0, 0);
                take.Mark(1000, 1000, 0);
                play(take, 1);
                take.Save(dir, level, score);
                return take;
            }

            var best = save(levelA, 900_000);
            var firstOfB = save(levelB, 10);

            for (int i = 0; i < PlayTake.KEPT_RECENT; i++)
                save(levelB, 100 + i);

            var kept = Directory.GetFiles(Path.Combine(dir, "takes"), "*.wav").Select(Path.GetFileNameWithoutExtension).ToList();
            Assert.That(kept, Has.Count.EqualTo(PlayTake.KEPT_RECENT + 1));
            Assert.That(kept, Does.Contain(best.Id.ToString()), "level A's best, though old");
            Assert.That(kept, Does.Not.Contain(firstOfB.Id.ToString()), "neither recent nor best");
        }
    }
}
