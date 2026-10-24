// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System.Linq;
using NUnit.Framework;
using Kan.Forge.Tracks;

namespace Kan.Forge.Tests
{
    /// <summary>Mic delay from a play-along take.</summary>
    [TestFixture]
    public class MicCalibrationTest
    {
        private static readonly double[] clicks = Enumerable.Range(0, 8).Select(i => 3000 + i * 750.0).ToArray();

        [Test]
        public void SteadyDelayIsMeasured()
        {
            // Every note 120 ms after its click, give or take a human's 10 ms.
            double[] jitter = { 0, 8, -6, 10, -10, 4, -3, 2 };
            var onsets = clicks.Select((c, i) => c + 120 + jitter[i]).ToArray();

            var r = MicCalibration.Estimate(clicks, onsets);

            Assert.That(r.Matched, Is.EqualTo(8));
            Assert.That(r.DelayMs, Is.EqualTo(121).Within(2));
            Assert.That(r.Reliable, Is.True);
        }

        [Test]
        public void MissedAndExtraNotesDoNotMoveIt()
        {
            // Two clicks unanswered, one doubled note, one stray sound far from any click.
            var onsets = clicks.Where((_, i) => i != 2 && i != 5).Select(c => c + 90).ToList();
            onsets.Add(clicks[3] + 90 + 200); // a second note after the first: ignored
            onsets.Add(clicks[7] + 700); // beyond the last click's window

            var r = MicCalibration.Estimate(clicks, onsets);

            Assert.That(r.Matched, Is.EqualTo(6));
            Assert.That(r.DelayMs, Is.EqualTo(90).Within(0.01));
            Assert.That(r.Reliable, Is.True);
        }

        [Test]
        public void AnticipatedNotesCountAsEarly()
        {
            var r = MicCalibration.Estimate(clicks, clicks.Select(c => c - 30).ToArray());
            Assert.That(r.DelayMs, Is.EqualTo(-30).Within(0.01));
        }

        [Test]
        public void ScatteredPlayingIsNotTrusted()
        {
            double[] wild = { 0, 150, -100, 250, 60, -90, 200, 10 };
            var r = MicCalibration.Estimate(clicks, clicks.Select((c, i) => c + 100 + wild[i]).ToArray());
            Assert.That(r.Reliable, Is.False);

            Assert.That(MicCalibration.Estimate(clicks, new double[0]).Reliable, Is.False, "nothing heard");
        }
    }
}
