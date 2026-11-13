// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System.Globalization;
using NUnit.Framework;
using Kan.Core.Tracks;
using Kan.Forge.Editor;

namespace Kan.Forge.Tests
{
    /// <summary>
    /// The level audio is rendered again when its signature changes, so the
    /// signature is part of the level's files in all but name
    /// (<c>ForgeTrackSession.LevelAudioSignature</c>). It must read the same
    /// on every machine (2026-11-12 audit B13: a locale change used to force
    /// a re-render on every save).
    /// </summary>
    [TestFixture]
    public class BeatmapAudioSignatureTest
    {
        [Test]
        public void TheSignatureDoesNotFollowTheMachinesLocale()
        {
            var set = new ForgeTrackSet();
            var track = set.Import("a", "/tmp/a.wav")!;
            track.IsCompanion = true;
            track.Gain = 0.5f;
            track.OffsetMs = 12.5;

            var culture = CultureInfo.CurrentCulture;

            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
                string german = BeatmapAudio.Signature(set);

                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
                string english = BeatmapAudio.Signature(set);

                Assert.That(english, Is.EqualTo(german), "one render, whatever the machine's locale");
                Assert.That(german, Does.Contain("0.500"), "the numbers are invariant, not the locale's");
            }
            finally
            {
                CultureInfo.CurrentCulture = culture;
            }
        }
    }
}
