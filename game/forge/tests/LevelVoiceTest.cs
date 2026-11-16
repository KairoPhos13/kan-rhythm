// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using NUnit.Framework;
using Kan.Core.Tracks;
using Kan.Forge.Editor;

namespace Kan.Forge.Tests
{
    /// <summary>
    /// The level's voice file (owner 2026-11-16): marking a companion as a
    /// voice renders the level audio again, and the game finds the voice file
    /// by its name.
    /// </summary>
    [TestFixture]
    public class LevelVoiceTest
    {
        [Test]
        public void TestMarkingAVoiceRendersTheLevelAudioAgain()
        {
            var set = new ForgeTrackSet();
            var vocals = set.Import("vocals", "/tmp/vocals.wav")!;
            vocals.IsCompanion = true;

            string before = BeatmapAudio.Signature(set);
            vocals.IsVoice = true;

            Assert.That(BeatmapAudio.Signature(set), Is.Not.EqualTo(before));
        }

        [Test]
        public void TestTheVoiceFileIsFoundByItsName()
        {
            Assert.That(BeatmapAudio.IsLevelVoice("No hay lugar mas alto - level voice.wav"), Is.True);
            Assert.That(BeatmapAudio.IsLevelVoice("LEVEL VOICE.WAV"), Is.True);
            Assert.That(BeatmapAudio.IsLevelVoice("No hay lugar mas alto - level audio.wav"), Is.False);
            Assert.That(BeatmapAudio.IsLevelVoice("vocals.wav"), Is.False);
            Assert.That(BeatmapAudio.IsLevelVoice(null), Is.False);
        }
    }
}
