// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System.Linq;
using NUnit.Framework;
using Kan.Forge.Tracks;

namespace Kan.Forge.Tests
{
    /// <summary>
    /// The unified track-list roles (owner 2026-10-13): one list, roles
    /// chosen per track — reference (exclusive), accompaniment (many), demux
    /// (queue). Separated/mixdown tracks are never demuxable again. Nothing
    /// is mandatory: demux and transcription are independent optional steps.
    /// </summary>
    [TestFixture]
    public class TrackRolesTest
    {
        [Test]
        public void TestReferenceIsExclusive()
        {
            var set = new ForgeTrackSet();
            var a = set.Import("a", "/tmp/a.wav")!;
            var b = set.Import("b", "/tmp/b.wav")!;

            set.SetReference(a);
            Assert.That(set.Reference, Is.SameAs(a));

            set.SetReference(b);
            Assert.That(set.Reference, Is.SameAs(b));
            Assert.That(a.IsReference, Is.False, "choosing a new reference clears the old one");
            Assert.That(b.IsReference, Is.True);

            set.ClearReference();
            Assert.That(set.Reference, Is.Null, "the reference role is optional");
        }

        [Test]
        public void TestCompanionsMakeTheLevelAudio()
        {
            var set = new ForgeTrackSet();
            var lead = set.Import("lead", "/tmp/lead.wav")!;
            var pads = set.Import("pads", "/tmp/pads.wav")!;
            var drums = set.Import("drums", "/tmp/drums.wav")!;

            lead.IsCompanion = false;
            pads.IsCompanion = true;
            drums.IsCompanion = true;
            drums.Muted = true;

            Assert.That(set.Companions.Select(t => t.Name), Is.EqualTo(new[] { "pads" }),
                        "the level audio mixes the companion tracks, minus muted ones");
        }

        [Test]
        public void TestSeparatedAndMixdownTracksCannotBeDemuxedAgain()
        {
            var set = new ForgeTrackSet();
            var source = set.Import("source", "/tmp/source.wav")!;
            var stem = set.Import("stem", "/tmp/roformer/bass.wav")!;
            var mix = set.Import("mix", "/tmp/audio-render.wav")!;

            stem.IsSeparated = true;
            mix.IsMixdown = ForgeTrack.LooksLikeMixdown(mix.Path);

            source.DemuxPending = true;
            stem.DemuxPending = true;
            mix.DemuxPending = true;

            Assert.That(source.CanDemux, Is.True);
            Assert.That(stem.CanDemux, Is.False, "demux results never get demuxed again");
            Assert.That(mix.CanDemux, Is.False, "a mixdown is mixed material");

            Assert.That(set.DemuxQueue.Select(t => t.Name), Is.EqualTo(new[] { "source" }),
                        "the queue only carries eligible tracks");

            // A demuxed source becomes ineligible too ("hace perder el tiempo").
            source.IsSeparated = true;
            Assert.That(source.CanDemux, Is.False);
        }

        [Test]
        public void TestDuplicateImportsAreRefused()
        {
            var set = new ForgeTrackSet();

            Assert.That(set.Import("a", "/tmp/a.wav"), Is.Not.Null);
            Assert.That(set.Import("a again", "/tmp/a.wav"), Is.Null,
                        "the same file is never added twice");
            Assert.That(set.Import("a case", "/TMP/A.WAV"), Is.Null,
                        "paths compare case-insensitively");
            Assert.That(set.Tracks.Count, Is.EqualTo(1));
            Assert.That(set.ContainsPath("/tmp/a.wav"), Is.True);
        }

        [Test]
        public void TestDemuxResultsCanTakeOtherRoles()
        {
            var set = new ForgeTrackSet();
            var stem = set.Import("bass", "/tmp/roformer/bass.wav")!;
            stem.IsSeparated = true;

            set.SetReference(stem);
            stem.IsCompanion = true;

            Assert.That(set.Reference, Is.SameAs(stem), "stems can be the reference");
            Assert.That(set.Companions.Contains(stem), Is.True, "stems can be companions");
        }
    }
}
