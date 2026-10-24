// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using NUnit.Framework;
using Kan.Core.Native;
using Kan.Core.Tracks;

namespace Kan.Core.Tests
{
    /// <summary>
    /// Tracks on a shared timeline (owner 2026-10-14): every track can be
    /// moved in time; edits keep the result where its audio was.
    /// </summary>
    [TestFixture]
    public class TrackTimelineTest
    {
        [Test]
        public void TestOffsetIsNeverNegative()
        {
            var t = new ForgeTrack("a", "/tmp/a.wav") { OffsetMs = -250 };
            Assert.That(t.OffsetMs, Is.EqualTo(0));

            t.OffsetMs = double.NaN;
            Assert.That(t.OffsetMs, Is.EqualTo(0));

            t.OffsetMs = 1500;
            Assert.That(t.OffsetMs, Is.EqualTo(1500));
            Assert.That(t.Clone().OffsetMs, Is.EqualTo(1500), "clones keep their place");
        }

        [Test]
        public void TestATrimmedTrackMovesUntilItsAudiblePartReachesZero()
        {
            // Owner 2026-10-14: what may not go before 0:00 is the part that
            // plays, not the part trimmed away.
            var t = new ForgeTrack("a", "/tmp/a.wav") { ClipStartMs = 800 };

            t.OffsetMs = -500;
            Assert.That(t.OffsetMs, Is.EqualTo(-500), "the hidden start may sit before 0:00");
            Assert.That(t.TimelineStartMs, Is.EqualTo(300));

            t.OffsetMs = -2000;
            Assert.That(t.TimelineStartMs, Is.EqualTo(0), "…but the audible part stops at 0:00");

            t.ClipStartMs = 100;
            Assert.That(t.ClipStartMs, Is.EqualTo(800), "un-trimming can't reveal audio before 0:00");

            var copy = t.Clone();
            Assert.That(copy.OffsetMs, Is.EqualTo(-800), "copies keep the position");
            Assert.That(copy.ClipStartMs, Is.EqualTo(800));
        }

        [Test]
        public void TestEditsKeepTheResultInPlace()
        {
            var t = new ForgeTrack("a", "/tmp/a.wav") { OffsetMs = 2000 };

            Assert.That(t.OffsetAfterEdit(KanForgeEditMode.Keep, 3000), Is.EqualTo(2000), "keep preserves timing");
            Assert.That(t.OffsetAfterEdit(KanForgeEditMode.Mute, 3000), Is.EqualTo(2000), "silence preserves timing");
            Assert.That(t.OffsetAfterEdit(KanForgeEditMode.Trim, 3000), Is.EqualTo(5000), "trim starts where the range started");
        }
    }
}
