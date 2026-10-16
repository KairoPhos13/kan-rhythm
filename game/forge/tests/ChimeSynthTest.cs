// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Linq;
using NUnit.Framework;
using Kan.Forge.Score;

namespace Kan.Forge.Tests
{
    /// <summary>The perfect chime: the played note an octave up, short, soft, clean (owner 2026-10-15).</summary>
    [TestFixture]
    public class ChimeSynthTest
    {
        private static double frequency(float[] x, int from, int to)
        {
            int crossings = 0;
            for (int i = from + 1; i < to; i++)
                if (x[i - 1] < 0 && x[i] >= 0) crossings++;
            return crossings / ((double)(to - from) / ChimeSynth.SAMPLE_RATE);
        }

        [Test]
        public void SoundsTheNoteAnOctaveUp()
        {
            Assert.That(ChimeSynth.PitchFor(69), Is.EqualTo(81));
            Assert.That(ChimeSynth.PitchFor(90), Is.EqualTo(90), "never above C7: an octave down instead");

            // A5 (880 Hz), read after the brighter partials have rung out.
            var chime = ChimeSynth.Render(81);
            int from = ChimeSynth.SAMPLE_RATE / 10, to = ChimeSynth.SAMPLE_RATE / 4;
            Assert.That(frequency(chime, from, to), Is.EqualTo(880).Within(10));
        }

        [Test]
        public void ShortSoftAndClean()
        {
            var chime = ChimeSynth.Render(84);

            Assert.That(chime.Length / (double)ChimeSynth.SAMPLE_RATE * 1000, Is.EqualTo(ChimeSynth.LENGTH_MS).Within(1));
            Assert.That(chime.Max(Math.Abs), Is.LessThanOrEqualTo(0.55f), "soft: it accompanies, never covers");
            Assert.That(Math.Abs(chime[0]), Is.LessThan(0.01f), "no click at the start");
            Assert.That(Math.Abs(chime[^1]), Is.LessThan(0.001f), "nor at the end");

            // It rings out: the last tenth is far quieter than the first.
            double head = chime.Take(chime.Length / 10).Max(Math.Abs);
            double tail = chime.Skip(chime.Length * 9 / 10).Max(Math.Abs);
            Assert.That(tail, Is.LessThan(head * 0.05));
        }
    }
}
