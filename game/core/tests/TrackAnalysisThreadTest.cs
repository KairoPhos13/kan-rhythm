// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using NUnit.Framework;
using Kan.Core.Tracks;

namespace Kan.Core.Tests
{
    /// <summary>
    /// The analysis is a worker's measurement, handed to the track on the
    /// update thread (<c>ForgeTrackSet</c>'s rule: the UI reads the set
    /// every frame; 2026-11-12 audit B8). Measuring alone must leave the
    /// track untouched, or a background job writes shared state under the
    /// UI's feet.
    /// </summary>
    [TestFixture]
    public class TrackAnalysisThreadTest
    {
        [Test]
        public void MeasuringAloneNeverTouchesTheTrack()
        {
            var track = new ForgeTrack("some", "unused.wav");

            TrackAnalysis.Analyze(track);

            Assert.That(track.Peaks, Is.Null, "the measurements cross on the update thread, never in place");
            Assert.That(track.AnalysisFailed, Is.False, "measuring reports; only the update thread decides");
            Assert.That(track.LikelyEmpty, Is.False);
        }
    }
}
