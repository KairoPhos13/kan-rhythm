// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System.Collections.Generic;
using Kan.Core.Score;
using NUnit.Framework;

namespace Kan.Core.Tests
{
    /// <summary>
    /// The star rating of a level: pace and reach, computed the same way on
    /// every side of kan! (game, server, tools). The expected numbers here
    /// are the formula's contract; the server's mirror of them lives in
    /// server/app/test/level-difficulty.spec.ts.
    /// </summary>
    [TestFixture]
    public class LevelDifficultyTest
    {
        [Test]
        public void ALevelWithoutNotesHasNoDifficulty()
        {
            var none = LevelDifficulty.Of(new List<ScoreNote>());

            Assert.That(none.Stars, Is.EqualTo(0));
            Assert.That(none.NoteCount, Is.EqualTo(0));
            Assert.That(none.NotesPerSecond, Is.EqualTo(0));
        }

        [Test]
        public void OneShortNoteReadsCalmNotFrantic()
        {
            // The span of a lone note is measured as at least one second of
            // music (LevelDifficulty.MIN_SPAN_MS), so its pace is 1 note per
            // second whatever its duration.
            var one = LevelDifficulty.Of(new List<ScoreNote>
            {
                new ScoreNote { StartMs = 0, DurationMs = 250, Midi = 60 },
            });

            Assert.That(one.NotesPerSecond, Is.EqualTo(1).Within(1e-9));
            Assert.That(one.PitchSpanSemitones, Is.EqualTo(0));
            Assert.That(one.Stars, Is.EqualTo(LevelDifficulty.BASE_STARS + LevelDifficulty.PACE_STARS).Within(1e-9));
        }

        [Test]
        public void PaceAndReachEachAddTheirShare()
        {
            // Two notes two seconds apart, an octave apart in pitch:
            // 2 notes over 2.75 s = 0.7273 notes per second, one octave.
            var notes = new List<ScoreNote>
            {
                new ScoreNote { StartMs = 0, DurationMs = 750, Midi = 60 },
                new ScoreNote { StartMs = 2000, DurationMs = 750, Midi = 72 },
            };

            var difficulty = LevelDifficulty.Of(notes);

            Assert.That(difficulty.NoteCount, Is.EqualTo(2));
            Assert.That(difficulty.NotesPerSecond, Is.EqualTo(2000.0 / 2750.0).Within(1e-9));
            Assert.That(difficulty.PitchSpanSemitones, Is.EqualTo(12).Within(1e-9));
            Assert.That(difficulty.Octaves, Is.EqualTo(1).Within(1e-9));
            Assert.That(difficulty.Stars, Is.EqualTo(0.2 + 1.2 * (2000.0 / 2750.0) + 0.8).Within(1e-9));
        }

        [Test]
        public void OnlyKnownPitchesMeasureTheReach()
        {
            var notes = new List<ScoreNote>
            {
                new ScoreNote { StartMs = 0, DurationMs = 100, Midi = 60 },
                new ScoreNote { StartMs = 1000, DurationMs = 100, Midi = 84, PitchUnknown = true },
                new ScoreNote { StartMs = 2000, DurationMs = 100, Midi = 65 },
            };

            Assert.That(LevelDifficulty.Of(notes).PitchSpanSemitones, Is.EqualTo(5).Within(1e-9),
                "a guessed pitch says nothing about the level's reach");

            notes[1].PitchUnknown = false;
            Assert.That(LevelDifficulty.Of(notes).PitchSpanSemitones, Is.EqualTo(24).Within(1e-9));
        }

        [Test]
        public void CentsCountTowardsTheReach()
        {
            var notes = new List<ScoreNote>
            {
                new ScoreNote { StartMs = 0, DurationMs = 100, Midi = 60, Cents = -50 },
                new ScoreNote { StartMs = 1000, DurationMs = 100, Midi = 60, Cents = 50 },
            };

            Assert.That(LevelDifficulty.Of(notes).PitchSpanSemitones, Is.EqualTo(1).Within(1e-9));
        }

        [Test]
        public void ALevelOfManyFastNotesReadsHard()
        {
            // 30 notes over 10 seconds, two octaves: 3 notes per second.
            var notes = new List<ScoreNote>();
            for (int i = 0; i < 30; i++)
                notes.Add(new ScoreNote { StartMs = i * 333.0, DurationMs = 200, Midi = 60 + (i % 2) * 24 });

            var difficulty = LevelDifficulty.Of(notes);

            Assert.That(difficulty.NotesPerSecond, Is.EqualTo(3).Within(0.05));
            Assert.That(difficulty.Stars, Is.GreaterThan(4).And.LessThan(6), "a busy level is not a calm one");
        }

        [Test]
        public void TheInputsVersionComputesTheSameStars()
        {
            var fromInputs = LevelDifficulty.From(76, 76960.0, 12);

            Assert.That(fromInputs.NoteCount, Is.EqualTo(76));
            Assert.That(fromInputs.NotesPerSecond, Is.EqualTo(76 * 1000.0 / 76960.0).Within(1e-9));
            Assert.That(fromInputs.Stars, Is.EqualTo(0.2 + 1.2 * (76 * 1000.0 / 76960.0) + 0.8).Within(1e-9));

            Assert.That(LevelDifficulty.From(0, 1000, 12).Stars, Is.EqualTo(0), "no notes, no difficulty");
        }
    }
}
