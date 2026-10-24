// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using NUnit.Framework;
using Kan.Core.Play;

namespace Kan.Core.Tests
{
    /// <summary>Where a heard moment falls on the level's timeline.</summary>
    [TestFixture]
    public class ListeningTimelineTest
    {
        [Test]
        public void BeforeTheClockIsSeenTheAnchorPlacesASound()
        {
            var timeline = new ListeningTimeline();
            timeline.Begin(anchorMs: 1000, rate: 1.5, micDelayMs: 40, playerTimingMs: -20, pacedByLevel: false);

            // (500 - 40) × 1.5 after the anchor, the player's 20 ms ahead taken as meant (+30 at 1.5×).
            Assert.That(timeline.Place(500), Is.EqualTo(1000 + 460 * 1.5 + 30).Within(1e-9));
        }

        [Test]
        public void ASoundFallsBetweenTheFramesThatSawIt()
        {
            var timeline = new ListeningTimeline();
            timeline.Begin(0, 1, 0, 0, false);

            // The level's clock ran 10 ms behind real time.
            for (int real = 0; real <= 1000; real += 16)
                timeline.Remember(real, real - 10);

            Assert.That(timeline.Place(500), Is.EqualTo(490).Within(1e-9));
            Assert.That(timeline.Place(2000), Is.EqualTo(1990).Within(1e-9), "after the last frame: carried on at the rate");
        }

        [Test]
        public void TheMicDelayAndTheHabitAreTakenOff()
        {
            var timeline = new ListeningTimeline();
            timeline.Begin(0, 1, micDelayMs: 50, playerTimingMs: 30, pacedByLevel: false);
            timeline.Remember(0, 0);
            timeline.Remember(1000, 1000);

            // Heard at 600: played at 550, and this player plays 30 ms late by habit.
            Assert.That(timeline.Place(600), Is.EqualTo(520).Within(1e-9));
        }

        [Test]
        public void ALevelStandingStillCarriesOnFromTheLaterFrame()
        {
            var timeline = new ListeningTimeline();
            timeline.Begin(0, 1, 0, 0, false);
            timeline.Remember(0, 0);
            timeline.Remember(1000, 1000);
            timeline.Remember(3000, 1000); // stood still for 2 s (the wait mode)
            timeline.Remember(3016, 1016);

            Assert.That(timeline.Place(2000), Is.EqualTo(1000 - 1000).Within(1e-9), "within the stop: before the later frame by the real time");
            Assert.That(timeline.Place(3008), Is.EqualTo(1008).Within(1e-9));
        }

        [Test]
        public void ASourcePacedByTheLevelIsPlacedByTheAnchorAlone()
        {
            var timeline = new ListeningTimeline();
            timeline.Begin(anchorMs: 200, rate: 2, micDelayMs: 0, playerTimingMs: 0, pacedByLevel: true);
            timeline.Remember(0, 999);
            timeline.Remember(100, 1999);

            Assert.That(timeline.Rate, Is.EqualTo(1), "its time is already the level's");
            Assert.That(timeline.Place(50), Is.EqualTo(250));

            timeline.Freeze(30);
            Assert.That(timeline.Place(50), Is.EqualTo(220), "the time the level stood still is taken off");
        }
    }
}
