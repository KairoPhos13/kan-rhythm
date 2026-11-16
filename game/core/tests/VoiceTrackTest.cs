// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System.Linq;
using NUnit.Framework;
using Kan.Core.Tracks;

namespace Kan.Core.Tests
{
    /// <summary>
    /// A companion that is a voice (owner 2026-11-16): it goes to the level as
    /// its own file, which the No voice mod silences. The flag survives the
    /// set file, undo and copies, and splits the companions in two.
    /// </summary>
    [TestFixture]
    public class VoiceTrackTest
    {
        [Test]
        public void TestVoicesAndInstrumentalSplitTheCompanions()
        {
            var set = new ForgeTrackSet();
            var drums = set.Import("drums", "/tmp/drums.wav")!;
            var vocals = set.Import("vocals", "/tmp/vocals.wav")!;
            var other = set.Import("other", "/tmp/other.wav")!;

            drums.IsCompanion = true;
            vocals.IsCompanion = vocals.IsVoice = true;
            other.IsVoice = true; // a voice that is not a companion is not in the level

            Assert.That(set.Companions, Is.EquivalentTo(new[] { drums, vocals }));
            Assert.That(set.Voices, Is.EquivalentTo(new[] { vocals }));
            Assert.That(set.Instrumental, Is.EquivalentTo(new[] { drums }));
            Assert.That(other.IsVoiceCompanion, Is.False);

            vocals.Muted = true;
            Assert.That(set.Voices, Is.Empty, "a muted voice is not rendered");
        }

        [Test]
        public void TestTheFlagSurvivesTheSetFileAndCopies()
        {
            var set = new ForgeTrackSet();
            var vocals = set.Import("vocals", "/tmp/vocals.wav")!;
            vocals.IsCompanion = vocals.IsVoice = true;

            var doc = ForgeSetFile.From(set, _ => false, null, null);
            var back = ForgeSetFile.ToTracks(doc).Single().Track;

            Assert.That(back.IsVoiceCompanion, Is.True);
            Assert.That(vocals.Clone().IsVoice, Is.True);
        }

        [Test]
        public void TestUndoBringsTheFlagBack()
        {
            var set = new ForgeTrackSet();
            var vocals = set.Import("vocals", "/tmp/vocals.wav")!;
            vocals.IsCompanion = true;

            var history = new ForgeSetHistory(set);
            history.Record();
            vocals.IsVoice = true;

            Assert.That(history.Undo(), Is.True);
            Assert.That(vocals.IsVoice, Is.False);
        }
    }
}
