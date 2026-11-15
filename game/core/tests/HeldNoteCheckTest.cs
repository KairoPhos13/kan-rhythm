// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using Kan.Core.Play;
using NUnit.Framework;

namespace Kan.Core.Tests
{
    [TestFixture]
    public class HeldNoteCheckTest
    {
        /// <summary>A held note read every 10 ms: silence, then the note from 500 ms, its level falling by <paramref name="fadeDb"/> over the hold.</summary>
        private static HeldNoteCheck hold(double fadeDb, float midi = 72, double level = 0.05, double untilMs = 4500, double pitchLostBelowDb = double.NegativeInfinity)
        {
            var check = new HeldNoteCheck();

            for (double t = 0; t <= untilMs; t += 10)
            {
                if (t < 500)
                {
                    check.Add(t, 0.0005, 0);
                    continue;
                }

                double db = -fadeDb * (t - 500) / (untilMs - 500);
                double rms = level * Math.Pow(10, db / 20);
                check.Add(t, rms, db < pitchLostBelowDb ? 0 : midi + 0.1f);
            }

            return check;
        }

        [Test]
        public void ASteadyNoteKeepsItsLevel()
        {
            var r = hold(0).Measure();

            Assert.That(r.Heard, Is.True);
            Assert.That(r.FadeDb, Is.EqualTo(0).Within(0.5));
            Assert.That(r.HeardFraction, Is.GreaterThan(0.95));
            Assert.That(r.Midi, Is.EqualTo(72.1f).Within(0.01f));
        }

        [Test]
        public void AProcessedInputFadesTheNote()
        {
            // Noise suppression: -20 dB over the hold, the pitch lost in its last part.
            var r = hold(20, pitchLostBelowDb: -15).Measure();

            Assert.That(r.Heard, Is.True);
            Assert.That(r.FadeDb, Is.GreaterThan(15));
        }

        [Test]
        public void NothingPitchedIsNotHeard()
        {
            var check = new HeldNoteCheck();
            for (double t = 0; t < 4000; t += 10) check.Add(t, 0.01, 0);

            Assert.That(check.Measure().Heard, Is.False);
        }

        [Test]
        public void TheSteadiestInputWins()
        {
            string def = "Default", raw = "acp5x", monitor = "Monitor";

            var chosen = HeldNoteCheck.Choose(new[]
            {
                (def, hold(20, pitchLostBelowDb: -15).Measure()),
                (raw, hold(1, level: 0.02).Measure()),
                (monitor, new HeldNoteCheck().Measure()),
            }, def);

            Assert.That(chosen, Is.EqualTo(raw));
        }

        [Test]
        public void TheCurrentInputStaysWhenItIsAboutAsGood()
        {
            string current = "USB", other = "Built-in";

            var chosen = HeldNoteCheck.Choose(new[]
            {
                (current, hold(3).Measure()),
                (other, hold(0.5).Measure()),
            }, current);

            Assert.That(chosen, Is.EqualTo(current));
        }

        [Test]
        public void NoInputHeardIsNoChoice()
        {
            Assert.That(HeldNoteCheck.Choose(new[] { ("a", new HeldNoteCheck().Measure()) }, "a"), Is.Null);
        }

        [Test]
        public void WhilePlayingAFadingHeldNoteIsNoticed()
        {
            var fading = hold(18, untilMs: 2600);
            Assert.That(fading.Fading(out double db), Is.True);
            Assert.That(db, Is.GreaterThan(HeldNoteCheck.FADING_DB));

            Assert.That(hold(2, untilMs: 2600).Fading(out _), Is.False, "a steady note");
            Assert.That(hold(18, untilMs: 1200).Fading(out _), Is.False, "too short to tell");
        }

        [Test]
        public void ANoteThePlayerEndsIsNotFading()
        {
            // The player stops: the level falls, but the pitch goes with it.
            Assert.That(hold(30, untilMs: 2600, pitchLostBelowDb: -5).Fading(out _), Is.False);
        }
    }
}
