// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using NUnit.Framework;
using Kan.Core.Play;

namespace Kan.Core.Tests
{
    /// <summary>
    /// The noise-floor step of the calibration screen (issue 17): what the
    /// room sounds like when nobody plays, and where the note detector's gate
    /// opens above it.
    /// </summary>
    [TestFixture]
    public class NoiseFloorMeasureTest
    {
        [Test]
        public void AQuietRoomIsMeasured()
        {
            var m = new NoiseFloorMeasure();
            for (int i = 0; i < 500; i++) m.Add(0.01); // 5 s of steady room tone

            var r = m.Finish();

            Assert.That(r.Valid, Is.True);
            Assert.That(r.FloorRms, Is.EqualTo(0.01).Within(1e-9));
            Assert.That(r.FloorDb, Is.EqualTo(-40).Within(0.01), "0.01 RMS is -40 dB");
            Assert.That(r.Energy, Is.EqualTo(1e-4).Within(1e-12), "the detector's floor is mean-squared amplitude");
            Assert.That(r.OpenRms, Is.EqualTo(0.01 * System.Math.Pow(10, NoiseFloorMeasure.OPEN_DB / 20)).Within(1e-9),
                "a note must sit OPEN_DB above the room to be heard");
            Assert.That(r.Readings, Is.EqualTo(500));
        }

        [Test]
        public void ADoorDoesNotRaiseTheFloor()
        {
            var m = new NoiseFloorMeasure();
            for (int i = 0; i < 300; i++) m.Add(i == 150 ? 0.5 : 0.005); // one slam in the quiet

            var r = m.Finish();

            Assert.That(r.FloorRms, Is.EqualTo(0.005).Within(1e-9), "the floor is the room, not the door");
            Assert.That(r.PeakRms, Is.EqualTo(0.5).Within(1e-9), "the slam is still reported, to mention kindly");
        }

        [Test]
        public void DifferentRoomsGetDifferentGates()
        {
            var quiet = new NoiseFloorMeasure();
            var busy = new NoiseFloorMeasure();
            for (int i = 0; i < 200; i++)
            {
                quiet.Add(0.002);
                busy.Add(0.02);
            }

            Assert.That(busy.Finish().OpenRms, Is.GreaterThan(quiet.Finish().OpenRms * 5),
                "a louder room opens the gate higher, so the room does not trigger notes");
        }

        [Test]
        public void NothingHeardIsNotValid()
        {
            var r = new NoiseFloorMeasure().Finish();
            Assert.That(r.Valid, Is.False);
            Assert.That(r.Readings, Is.EqualTo(0));
        }
    }
}
