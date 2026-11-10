// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using NUnit.Framework;
using Kan.Core.Tracks;

namespace Kan.Core.Tests
{
    /// <summary>
    /// Owner rule (2026-10-13): tracks that are already a mixdown must not be
    /// offered for demixing again. The naming rule is the contract between
    /// the render step (which writes audio-render*.wav) and the pickers.
    /// </summary>
    [TestFixture]
    public class MixdownRuleTest
    {
        [Test]
        public void TestRenderOutputsAreRecognisedAsMixdowns()
        {
            Assert.That(ForgeTrack.LooksLikeMixdown("audio-render.wav"), Is.True);
            Assert.That(ForgeTrack.LooksLikeMixdown("/tmp/kan-forge/audio-render-1.wav"), Is.True);
            Assert.That(ForgeTrack.LooksLikeMixdown("Audio-Render-Something.mp3"), Is.True);

            Assert.That(ForgeTrack.LooksLikeMixdown("bass.wav"), Is.False);
            Assert.That(ForgeTrack.LooksLikeMixdown("piano-take.mp3"), Is.False);
            Assert.That(ForgeTrack.LooksLikeMixdown("source.wav"), Is.False);
        }

        [Test]
        public void TestMixdownFlagTravelsWithTheTrack()
        {
            var track = new ForgeTrack("mix", "/tmp/audio-render.wav");
            track.IsMixdown = ForgeTrack.LooksLikeMixdown(track.Path);

            Assert.That(track.IsMixdown, Is.True, "a rendered mix is mixed material, no re-demix");

            var clone = track.Clone();
            Assert.That(clone.IsMixdown, Is.True);
        }
    }
}
