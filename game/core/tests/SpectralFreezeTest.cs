// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Linq;
using NUnit.Framework;
using Kan.Core.Media;

namespace Kan.Core.Tests
{
    /// <summary>The wait mode holds the tone: the freeze keeps pitch and loudness, and loops without a seam.</summary>
    [TestFixture]
    public class SpectralFreezeTest
    {
        private const int rate = 48000;

        private static float[] sine(double hz, double seconds, float amplitude)
            => Enumerable.Range(0, (int)(seconds * rate)).Select(i => amplitude * (float)Math.Sin(2 * Math.PI * hz * i / rate)).ToArray();

        /// <summary>The strongest frequency of a signal (zero-crossing count is enough for one held tone).</summary>
        private static double frequency(float[] x)
        {
            int crossings = 0;
            for (int i = 1; i < x.Length; i++)
                if (x[i - 1] < 0 && x[i] >= 0) crossings++;
            return crossings / ((double)x.Length / rate);
        }

        private static double rms(float[] x) => Math.Sqrt(x.Average(v => (double)v * v));

        [Test]
        public void HoldsThePitchAndTheLoudness()
        {
            var input = sine(440, 0.5, 0.3f);
            var held = SpectralFreeze.Render(input, rate, 2);

            Assert.That(held.Length / (double)rate, Is.EqualTo(2).Within(0.05));
            Assert.That(frequency(held), Is.EqualTo(440).Within(8));
            Assert.That(rms(held), Is.EqualTo(rms(input)).Within(rms(input) * 0.1));
        }

        [Test]
        public void HoldsOnlyWhatSoundedAtTheEnd()
        {
            // A 330 Hz note, then 660 Hz for the last 0.2 s: the freeze holds 660.
            var input = sine(330, 0.5, 0.3f).Concat(sine(660, 0.2, 0.3f)).ToArray();
            Assert.That(frequency(SpectralFreeze.Render(input, rate, 1)), Is.EqualTo(660).Within(15));
        }

        [Test]
        public void LoopsWithoutASeam()
        {
            var held = SpectralFreeze.Render(sine(440, 0.5, 0.3f), rate, 1);

            // The step from the last sample back to the first is like any other step.
            double maxStep = 0;
            for (int i = 1; i < held.Length; i++)
                maxStep = Math.Max(maxStep, Math.Abs(held[i] - held[i - 1]));

            Assert.That(Math.Abs(held[0] - held[^1]), Is.LessThanOrEqualTo(maxStep * 1.05));

            // And the loudness does not dip or swell around the wrap.
            double edge = rms(held.Take(2048).Concat(held.Skip(held.Length - 2048)).ToArray());
            Assert.That(edge, Is.EqualTo(rms(held)).Within(rms(held) * 0.15));
        }

        [Test]
        public void SilenceStaysSilent()
        {
            var held = SpectralFreeze.Render(new float[SpectralFreeze.INPUT_SAMPLES], rate, 1);
            Assert.That(held.Max(Math.Abs), Is.EqualTo(0));
        }

        [Test]
        public void ShortInputIsPadded()
        {
            var held = SpectralFreeze.Render(sine(440, 0.05, 0.3f), rate, 1);
            Assert.That(frequency(held), Is.EqualTo(440).Within(10));
        }
    }
}
