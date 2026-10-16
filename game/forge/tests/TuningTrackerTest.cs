// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using NUnit.Framework;
using Kan.Forge.Play;

namespace Kan.Forge.Tests
{
    /// <summary>An instrument tuned off A440 is followed, a wrong note is not (owner 2026-10-15).</summary>
    [TestFixture]
    public class TuningTrackerTest
    {
        private static readonly int[] melody = { 72, 74, 76, 77, 79, 77, 76, 74 };

        /// <summary>Readings of a melody played <paramref name="cents"/> off, with a little wobble.</summary>
        private static void play(TuningTracker t, double cents, int readingsPerNote = 30, int notes = 40, int seed = 1)
        {
            var random = new Random(seed);

            for (int n = 0; n < notes; n++)
            {
                int midi = melody[n % melody.Length];
                for (int r = 0; r < readingsPerNote; r++)
                    t.Observe((float)(midi + cents / 100 + (random.NextDouble() - 0.5) * 0.12), midi);
            }
        }

        [Test]
        public void LearnsASharpInstrument()
        {
            var t = new TuningTracker();
            play(t, 44); // "almost a semitone sharp" (owner's instrument, roughly)
            Assert.That(t.Cents, Is.EqualTo(44).Within(4));

            // Corrected, its notes land on the score's.
            Assert.That(MathF.Round(t.Correct(72.44f)), Is.EqualTo(72));
            Assert.That(MathF.Round(t.Correct(72.56f)), Is.EqualTo(72), "a reading past the half-semitone is still the right note");
        }

        [Test]
        public void LearnsAFlatInstrument()
        {
            var t = new TuningTracker();
            play(t, -30);
            Assert.That(t.Cents, Is.EqualTo(-30).Within(4));
        }

        [Test]
        public void WrongNotesDoNotPullTheTuning()
        {
            var t = new TuningTracker(20);
            play(t, 20);

            // A semitone below every note, many times: other notes, not tuning.
            play(t, -80, notes: 80, seed: 2);
            Assert.That(t.Cents, Is.EqualTo(20).Within(4));
        }

        [Test]
        public void NeedsEnoughReadingsBeforeMoving()
        {
            var t = new TuningTracker();
            play(t, 40, readingsPerNote: 5, notes: 8); // 40 readings: too few
            Assert.That(t.Cents, Is.EqualTo(0));
        }

        [Test]
        public void StaysWithinItsRange()
        {
            Assert.That(new TuningTracker(300).Cents, Is.EqualTo(TuningTracker.MAX_CENTS));
            Assert.That(new TuningTracker().Correct(0), Is.EqualTo(0), "no pitch stays no pitch");
        }
    }
}
