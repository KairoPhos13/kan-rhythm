// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using NUnit.Framework;
using Kan.Core.Play;

namespace Kan.Core.Tests
{
    /// <summary>
    /// The detune step of the calibration screen (issue 17): a held reference
    /// note says how far the instrument sits from standard tuning, and the
    /// gate of game/TODO.md ("a −35-cent detuned instrument matched in &lt; 2 s").
    /// </summary>
    [TestFixture]
    public class DetuneMeasureTest
    {
        /// <summary>Readings of a note held <paramref name="ms"/>, off standard by <paramref name="cents"/> (10 ms apart, as the live pipeline gives them).</summary>
        private static void hold(DetuneMeasure m, double cents, double ms, int midi = 69, double wobbleCents = 3, int seed = 1)
        {
            var random = new Random(seed);

            for (double t = 0; t <= ms; t += 10)
                m.Add((float)(midi + cents / 100 + (random.NextDouble() - 0.5) * wobbleCents / 100), t);
        }

        [Test]
        public void AMinusThirtyFiveCentInstrumentIsMatchedInUnderTwoSeconds()
        {
            var m = new DetuneMeasure();
            hold(m, -35, DetuneMeasure.WINDOW_MS);

            Assert.That(m.Ready, Is.True, $"ready after {DetuneMeasure.WINDOW_MS:0} ms of readings");
            Assert.That(m.Progress, Is.EqualTo(1).Within(0.01));

            var r = m.Finish(69);

            Assert.That(r.Valid, Is.True);
            Assert.That(r.DeltaCents, Is.EqualTo(-35).Within(1));
            Assert.That(r.StandardMidi, Is.EqualTo(69));
            Assert.That(r.SpreadCents, Is.LessThan(10), "a held note is steady");
        }

        [Test]
        public void ASlightlySharpInstrumentIsMeasuredToo()
        {
            // "almost a semitone sharp on every note" (the owner's, 2026-10-15).
            var sharp = new DetuneMeasure();
            hold(sharp, 44, DetuneMeasure.WINDOW_MS);

            Assert.That(sharp.Finish(69).DeltaCents, Is.EqualTo(44).Within(1), "told the note: measured against it");
            Assert.That(sharp.Finish().DeltaCents, Is.EqualTo(44).Within(1), "untold: the nearest note is still the one held");

            // Past half a semitone only the told flow reads it right: against
            // the nearest note an instrument 80 cents sharp reads as the next
            // note up, 20 cents flat.
            var verySharp = new DetuneMeasure();
            hold(verySharp, 80, DetuneMeasure.WINDOW_MS);

            Assert.That(verySharp.Finish(69).DeltaCents, Is.EqualTo(80).Within(1), "told the note: past a semitone still reads true");
            Assert.That(verySharp.Finish().DeltaCents, Is.EqualTo(-20).Within(1), "untold: it reads as the next note up");
        }

        [Test]
        public void TheNoteIsInferredWhenNoneWasAskedFor()
        {
            var m = new DetuneMeasure();
            hold(m, -35, DetuneMeasure.WINDOW_MS, midi: 72);

            var r = m.Finish();

            Assert.That(r.StandardMidi, Is.EqualTo(72));
            Assert.That(r.DeltaCents, Is.EqualTo(-35).Within(1));
        }

        [Test]
        public void WrongNotesDoNotMoveTheMeasurement()
        {
            var m = new DetuneMeasure();
            hold(m, -35, 1000);

            // Someone plays a few other notes in between: the median holds.
            var random = new Random(2);
            for (double t = 1010; t <= 1500; t += 10)
                m.Add((float)(60 + random.NextDouble() * 6), t);

            Assert.That(m.Finish(69).DeltaCents, Is.EqualTo(-35).Within(1));
        }

        [Test]
        public void TooEarlyOrTooLittleIsNotReady()
        {
            var early = new DetuneMeasure();
            hold(early, -35, 1000);
            Assert.That(early.Ready, Is.False, "a second of readings is not enough yet");
            Assert.That(early.Progress, Is.LessThan(1));

            // The note stops after 0.4 s: the clock runs on, the measurement does not.
            var few = new DetuneMeasure();
            for (double t = 0; t <= 2000; t += 10)
                few.Add(t < 400 ? 68.65f : 0, t);
            Assert.That(few.Ready, Is.False, "time without a note is not a measurement");
        }

        [Test]
        public void NothingHeardIsNotValid()
        {
            var r = new DetuneMeasure().Finish();
            Assert.That(r.Valid, Is.False);
            Assert.That(r.Readings, Is.EqualTo(0));
        }
    }
}
