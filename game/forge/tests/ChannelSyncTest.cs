// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using NUnit.Framework;
using osu.Framework.Audio.Track;
using Kan.Forge.Editor;

namespace Kan.Forge.Tests
{
    /// <summary>Channels follow the timeline from their first start (owner 2026-10-14).</summary>
    [TestFixture]
    public class ChannelSyncTest
    {
        [Test]
        public void FirstStartPlaysFromTheTimelinePosition()
        {
            var track = new TrackVirtual(200_000);
            var sync = new ChannelSync();

            sync.Update(track, 60_000, false, 0); // cued while the toggle is off
            sync.Update(track, 60_000, true, 16);

            Assert.That(track.IsRunning, Is.True);
            Assert.That(track.CurrentTime, Is.EqualTo(60_000).Within(100));
        }

        [Test]
        public void AChannelFarFromTheTimelineIsPutInPlace()
        {
            var track = new TrackVirtual(200_000);
            var sync = new ChannelSync();

            sync.Update(track, 60_000, true, 0);
            // The first start lost its seek (seen 2026-10-14 with the reference
            // in compose): the channel plays from 0, nowhere near a buffer's offset.
            track.Seek(0);
            sync.Update(track, 60_300, true, 300); // settled: measure

            Assert.That(track.CurrentTime, Is.EqualTo(60_300).Within(100));
            Assert.That(sync.TakeResyncs(), Is.EqualTo(1));
        }
    }
}
