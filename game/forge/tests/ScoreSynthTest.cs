// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Linq;
using NUnit.Framework;
using Kan.Forge.Score;

namespace Kan.Forge.Tests
{
    /// <summary>Hearing the score (owner 2026-10-14): known notes in, known sound out.</summary>
    [TestFixture]
    public class ScoreSynthTest
    {
        private const int sr = ScoreSynth.SAMPLE_RATE;

        private static float peak(float[] s, double fromMs, double toMs)
            => s.Skip((int)(fromMs / 1000 * sr)).Take((int)((toMs - fromMs) / 1000 * sr)).Select(Math.Abs).DefaultIfEmpty(0).Max();

        /// <summary>The pitch heard in a window, from its upward zero crossings.</summary>
        private static double pitchHz(float[] s, double fromMs, double toMs)
        {
            int a = (int)(fromMs / 1000 * sr), b = (int)(toMs / 1000 * sr);
            int first = -1, last = -1, crossings = 0;

            for (int i = a + 1; i < b; i++)
            {
                if (s[i - 1] < 0 && s[i] >= 0)
                {
                    if (first < 0) first = i;
                    last = i;
                    crossings++;
                }
            }

            return (crossings - 1) / ((double)(last - first) / sr);
        }

        [Test]
        public void NotesSoundAtTheirTimeAndPitch()
        {
            var notes = new[]
            {
                new ScoreNote { StartMs = 500, DurationMs = 400, Midi = 69, Intensity = 1 },  // A4
                new ScoreNote { StartMs = 1500, DurationMs = 400, Midi = 57, Intensity = 1 }, // A3
            };

            float[] s = ScoreSynth.Render(notes);

            Assert.That(peak(s, 0, 495), Is.EqualTo(0), "silent before the first note");
            Assert.That(peak(s, 520, 880), Is.GreaterThan(0.1f));
            Assert.That(peak(s, 960, 1495), Is.EqualTo(0), "silent between the notes (after the release)");
            Assert.That(pitchHz(s, 550, 850), Is.EqualTo(440).Within(2));
            Assert.That(pitchHz(s, 1550, 1850), Is.EqualTo(220).Within(1));
            Assert.That(s.Length, Is.EqualTo((int)Math.Ceiling(2.9 * sr)).Within(1), "a second of tail after the last note");
        }

        [Test]
        public void IntensityAndUnknownPitch()
        {
            var loud = ScoreSynth.Render(new[] { new ScoreNote { StartMs = 0, DurationMs = 300, Midi = 69, Intensity = 1 } });
            var soft = ScoreSynth.Render(new[] { new ScoreNote { StartMs = 0, DurationMs = 300, Midi = 69, Intensity = 0 } });
            Assert.That(peak(soft, 50, 250), Is.LessThan(peak(loud, 50, 250) / 2));

            var unknown = ScoreSynth.Render(new[] { new ScoreNote { StartMs = 0, DurationMs = 300, Midi = 69, PitchUnknown = true } });
            Assert.That(peak(unknown, 0, 1000), Is.EqualTo(0), "a guessed pitch is not played");
        }

        [Test]
        public void ChordsStayWithinFullScale()
        {
            var chord = Enumerable.Range(0, 8).Select(i => new ScoreNote { StartMs = 0, DurationMs = 500, Midi = 60 + i * 2, Intensity = 1 }).ToArray();
            Assert.That(ScoreSynth.Render(chord).Max(Math.Abs), Is.LessThan(1));
        }
    }
}
