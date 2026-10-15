// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Linq;
using NUnit.Framework;
using Kan.Forge.Media;

namespace Kan.Forge.Tests
{
    /// <summary>The compose tab's spectrogram: energy lands on the right pitch row at the right time.</summary>
    [TestFixture]
    public class PitchSpectrogramTest
    {
        private static float[] tone(double hz, double seconds, int rate, double startSeconds = 0)
        {
            var x = new float[(int)((startSeconds + seconds) * rate)];
            for (int i = (int)(startSeconds * rate); i < x.Length; i++)
                x[i] = 0.5f * MathF.Sin((float)(2 * Math.PI * hz * i / rate));
            return x;
        }

        private static int loudestPitch(PitchSpectrogram s, int frame)
            => Enumerable.Range(0, PitchSpectrogram.PITCHES).OrderByDescending(m => s.At(frame, m)).First();

        [Test]
        public void TestToneLandsOnItsPitch()
        {
            const int rate = 44100;
            var a4 = PitchSpectrogram.Compute(tone(440, 1, rate), rate);
            var g4 = PitchSpectrogram.Compute(tone(392, 1, rate), rate);

            Assert.That(loudestPitch(a4, a4.Frames / 2), Is.EqualTo(69), "440 Hz is A4 (69)");
            Assert.That(loudestPitch(g4, g4.Frames / 2), Is.EqualTo(67), "392 Hz is G4 (67)");
            Assert.That(a4.At(a4.Frames / 2, 69), Is.EqualTo(1).Within(0.01f), "the loudest point reads 1");
            Assert.That(a4.At(a4.Frames / 2, 60), Is.LessThan(0.5f), "other rows stay dim");
        }

        [Test]
        public void TestTimeAxis()
        {
            const int rate = 44100;
            // Silence for 1 s, then A4 for 1 s.
            var s = PitchSpectrogram.Compute(tone(440, 1, rate, startSeconds: 1), rate);

            Assert.That(s.HopMs, Is.EqualTo(1024 * 1000.0 / rate).Within(1e-9));
            int early = (int)(300 / s.HopMs), late = (int)(1500 / s.HopMs);

            Assert.That(s.At(early, 69), Is.EqualTo(0), "nothing before the tone starts");
            Assert.That(s.At(late, 69), Is.GreaterThan(0.9f), "the tone is there after it starts");
        }
    }
}
