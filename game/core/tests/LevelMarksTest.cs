// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System.Collections.Generic;
using System.Linq;
using Kan.Core.Score;
using NUnit.Framework;

namespace Kan.Core.Tests
{
    /// <summary>
    /// The marks a level's osu!-format file carries: one per note's start and
    /// one at the level's end (game/core/Score/LevelMarks.cs).
    /// </summary>
    [TestFixture]
    public class LevelMarksTest
    {
        [Test]
        public void AScoreWithoutNotesCarriesNoMarks()
        {
            Assert.That(LevelMarks.TimesOf(new List<ScoreNote>()), Is.Empty);
            Assert.That(LevelMarks.TimesOf(null!), Is.Empty);
        }

        [Test]
        public void EveryNoteGetsAMarkAndTheLevelGetsAnEnd()
        {
            var notes = new List<ScoreNote>
            {
                new ScoreNote { StartMs = 1000, DurationMs = 500 },
                new ScoreNote { StartMs = 2000, DurationMs = 500 },
                new ScoreNote { StartMs = 3000, DurationMs = 1500 },
            };

            var marks = LevelMarks.TimesOf(notes);

            Assert.That(marks, Has.Count.EqualTo(notes.Count + 1), "one mark per note, plus the level's end");
            Assert.That(marks.Take(3), Is.EqualTo(new[] { 1000.0, 2000.0, 3000.0 }), "the notes' starts, in time order");
            Assert.That(marks[3], Is.EqualTo(4500.0), "the level ends where its last note ends");
        }

        [Test]
        public void TheEndMarkIsAtTheLastNoteToFinishNotTheLastToStart()
        {
            // A long note held under a later short one: the level ends when
            // the held note ends.
            var notes = new List<ScoreNote>
            {
                new ScoreNote { StartMs = 0, DurationMs = 10000 },
                new ScoreNote { StartMs = 500, DurationMs = 100 },
                new ScoreNote { StartMs = 9000, DurationMs = 100 },
            };

            var marks = LevelMarks.TimesOf(notes);

            Assert.That(marks.Last(), Is.EqualTo(10000.0));
            Assert.That(marks, Is.Ordered, "the marks are in time order");
        }

        [Test]
        public void NotesOutOfOrderStillGetMarksInOrder()
        {
            var notes = new List<ScoreNote>
            {
                new ScoreNote { StartMs = 3000, DurationMs = 100 },
                new ScoreNote { StartMs = 1000, DurationMs = 100 },
            };

            Assert.That(LevelMarks.TimesOf(notes), Is.EqualTo(new[] { 1000.0, 3000.0, 3100.0 }));
        }
    }
}
