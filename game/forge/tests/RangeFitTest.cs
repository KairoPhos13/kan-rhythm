// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System.Linq;
using NUnit.Framework;
using Kan.Forge.Play;

namespace Kan.Forge.Tests
{
    /// <summary>Any level on any instrument: the octave that fits, then folding (owner 2026-10-15).</summary>
    [TestFixture]
    public class RangeFitTest
    {
        private static readonly (int Lowest, int Highest) ocarina = RangeFit.DefaultRange(@"ocarina"); // A4–F6

        [Test]
        public void ALevelThatFitsStaysPut()
        {
            var fit = RangeFit.Plan(new[] { 72, 74, 76, 77, 79 }, ocarina.Lowest, ocarina.Highest);
            Assert.That(fit, Is.EqualTo(new RangeFit.Fit(0, 0, 5)));
            Assert.That(fit.Changes, Is.False);
        }

        [Test]
        public void ALowMelodyMovesUpAnOctave()
        {
            // A cello-like line (C3–G3) on an ocarina: one octave up is not enough, two are.
            var low = new[] { 48, 50, 52, 53, 55 };
            var fit = RangeFit.Plan(low, ocarina.Lowest, ocarina.Highest);

            Assert.That(fit.OctaveShift, Is.EqualTo(2));
            Assert.That(low.Select(m => RangeFit.Apply(m, fit, ocarina.Lowest, ocarina.Highest)), Is.EqualTo(new[] { 72, 74, 76, 77, 79 }));
        }

        [Test]
        public void ANoteStillOutsideIsFolded()
        {
            // Mostly in range, one high note (C7) above the ocarina: no shift, that note folds down.
            var melody = new[] { 72, 74, 76, 77, 79, 96 };
            var fit = RangeFit.Plan(melody, ocarina.Lowest, ocarina.Highest);

            Assert.That(fit.OctaveShift, Is.EqualTo(0));
            Assert.That(fit.Folded, Is.EqualTo(1));
            Assert.That(RangeFit.Apply(96, fit, ocarina.Lowest, ocarina.Highest), Is.EqualTo(84), "C7 → C6");
        }

        [Test]
        public void TheSmallestMoveWinsATie()
        {
            // Fits equally an octave down or two up: the smaller move.
            var fit = RangeFit.Plan(new[] { 84, 86 }, 60, 75);
            Assert.That(fit.OctaveShift, Is.EqualTo(-1));
        }

        [Test]
        public void ARangeNarrowerThanAnOctaveDoesNotFold()
        {
            var fit = RangeFit.Plan(new[] { 60, 72 }, 60, 65);
            Assert.That(RangeFit.Apply(72, fit, 60, 65), Is.EqualTo(72 + fit.OctaveShift * 12));
        }
    }
}
