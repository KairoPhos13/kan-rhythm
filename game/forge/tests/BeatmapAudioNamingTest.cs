// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using NUnit.Framework;
using Kan.Forge.Editor;

namespace Kan.Forge.Tests
{
    /// <summary>
    /// The level's file store ignores case (2026-11-12 audit B7), so the
    /// editor's name choices must ignore it too: "Audio-Render.wav" is the
    /// same file as "audio-render.wav", and the name check that decides
    /// which old render to remove followed the store's rule nowhere. Before
    /// the fix this file does not compile: there was no shared rule, the
    /// comparison lived inline and case-sensitively.
    /// </summary>
    [TestFixture]
    public class BeatmapAudioNamingTest
    {
        [Test]
        public void TheSameFileInAnotherCaseIsTheSameFile()
        {
            Assert.That(BeatmapAudio.SameFile("Audio-Render.WAV", "audio-render.wav"), Is.True);
            Assert.That(BeatmapAudio.SameFile("audio-render.wav", "audio-render-1.wav"), Is.False);
            Assert.That(BeatmapAudio.SameFile(null, "audio-render.wav"), Is.False);
        }

        [Test]
        public void ATakenNameGetsTheNextFreeOne()
        {
            Assert.That(BeatmapAudio.FreeFileName("audio-render.wav", new[] { "Audio-Render.wav" }), Is.EqualTo("audio-render-1.wav"),
                        "a name taken in another case is taken (the store ignores case)");
            Assert.That(BeatmapAudio.FreeFileName("audio-render.wav", new[] { "audio-render.wav", "audio-render-1.wav" }),
                        Is.EqualTo("audio-render-2.wav"));
            Assert.That(BeatmapAudio.FreeFileName("audio-render.wav", new string[0]), Is.EqualTo("audio-render.wav"));
        }
    }
}
