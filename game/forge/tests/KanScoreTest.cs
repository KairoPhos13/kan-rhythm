// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Kan.Forge.Score;
using Kan.Forge.Transcription;

namespace Kan.Forge.Tests
{
    /// <summary>The level's score — the notes gameplay judges (compose tab, owner 2026-10-14).</summary>
    [TestFixture]
    public class KanScoreTest
    {
        private static KanScore three() => new KanScore
        {
            Notes =
            {
                new ScoreNote { StartMs = 1000, DurationMs = 500, Midi = 67 }, // G4
                new ScoreNote { StartMs = 1500, DurationMs = 500, Midi = 48 }, // C3
                new ScoreNote { StartMs = 2000, DurationMs = 500, Midi = 81 }, // A5
            },
        };

        [Test]
        public void TestNoteNames()
        {
            Assert.That(KanScore.NoteName(60), Is.EqualTo("C4"));
            Assert.That(KanScore.NoteName(67), Is.EqualTo("G4"));
            Assert.That(KanScore.NoteName(117), Is.EqualTo("A8"));

            Assert.That(KanScore.TryParseNote("G4", out int g4) && g4 == 67, Is.True);
            Assert.That(KanScore.TryParseNote("a8", out int a8) && a8 == 117, Is.True);
            Assert.That(KanScore.TryParseNote("Bb3", out int bb3) && bb3 == 58, Is.True);
            Assert.That(KanScore.TryParseNote("C#-1", out int cs) && cs == 1, Is.True);
            Assert.That(KanScore.TryParseNote("H2", out _), Is.False);
            Assert.That(KanScore.TryParseNote("G", out _), Is.False, "an octave is required");
        }

        [Test]
        public void TestGroupMoveKeepsItsShape()
        {
            var score = three();
            var group = score.Notes.Take(2).ToList();

            score.Move(group, -5000, 0);
            Assert.That(group[0].StartMs, Is.EqualTo(0), "the group stops at 0 ms…");
            Assert.That(group[1].StartMs, Is.EqualTo(500), "…without deforming");

            score.Move(group, 0, 100);
            Assert.That(group.Max(n => n.Midi), Is.EqualTo(127), "pitch stops at the MIDI range");
            Assert.That(group[0].Midi - group[1].Midi, Is.EqualTo(67 - 48), "intervals are kept");

            score.Resize(group, -10000);
            Assert.That(group.All(n => n.DurationMs == KanScore.MIN_DURATION_MS), Is.True);
        }

        [Test]
        public void TestKeepRangeReturnsWhatItRemoved()
        {
            var score = three();
            Assert.That(KanScore.TryParseNote("G4", out int low), Is.True);
            Assert.That(KanScore.TryParseNote("A8", out int high), Is.True);

            var removed = score.KeepRange(low, high);

            Assert.That(score.Notes.Select(n => n.Midi), Is.EqualTo(new[] { 67, 81 }), "the right hand stays");
            Assert.That(removed.Single().Midi, Is.EqualTo(48), "the left-hand note can be offered back");
        }

        [Test]
        public void TestReplaceSection()
        {
            var score = three();
            score.ReplaceSection(1400, 2100, new[]
            {
                new ScoreNote { StartMs = 1450, Midi = 60 },
                new ScoreNote { StartMs = 3000, Midi = 62 }, // outside the section: dropped
            });

            Assert.That(score.Notes.Select(n => (n.StartMs, n.Midi)), Is.EqualTo(new[] { (1000.0, 67), (1450.0, 60) }));
        }

        [Test]
        public void TestFromDraftAndFileRoundTrip()
        {
            var draft = new TranscriptionDraft(new[]
            {
                new KanDraftNote { OnsetMs = 100, DurMs = 400, Midi = 72, Cents = -12, DynDb = -30, Transition = true },
                new KanDraftNote { OnsetMs = 600, DurMs = 200, Midi = -1 }, // unresolved: kept, marked
            });

            var score = KanScore.FromDraft(draft, "flute", offsetMs: 1000);
            Assert.That(score.Notes.Count, Is.EqualTo(2), "a note with an unresolved pitch is kept (its timing is real)");
            var unknown = score.Notes[1];
            Assert.That(unknown.PitchUnknown, Is.True);
            Assert.That(unknown.Midi, Is.EqualTo(72), "its pitch starts as its neighbour's");
            score.Move(new[] { unknown }, 0, 2);
            Assert.That(unknown.PitchUnknown, Is.False, "setting the pitch by hand clears the mark");
            score.Notes.Remove(unknown);

            var note = score.Notes.Single();
            Assert.That(note.StartMs, Is.EqualTo(1100), "placed where the transcribed track sits");
            Assert.That(note.Articulation, Is.EqualTo(Articulation.Legato));
            Assert.That(note.Intensity, Is.EqualTo(0.5f).Within(0.001f));

            string path = Path.Combine(Path.GetTempPath(), "kan-forge-tests", Guid.NewGuid().ToString("N")[..8], KanScore.FILE_NAME);
            score.Save(path);
            var back = KanScore.Load(path)!;

            Assert.That(back.Instrument, Is.EqualTo("flute"));
            Assert.That(back.Notes.Single().Cents, Is.EqualTo(-12));
            Assert.That(File.ReadAllText(path), Does.Contain("\"Legato\""), "enums are readable in the file");
            Assert.That(KanScore.FromJson("{ \"Version\": 99 }"), Is.Null, "newer versions are refused, not misread");
        }

        [Test]
        public void TestUndoRedo()
        {
            var score = three();
            var history = new ScoreHistory();

            history.Record(score);
            score.Notes.RemoveAt(0);

            var undone = history.Undo(score)!;
            Assert.That(undone.Notes.Count, Is.EqualTo(3));

            var redone = history.Redo(undone)!;
            Assert.That(redone.Notes.Count, Is.EqualTo(2));
        }

        [Test]
        public void NotesWithoutAReleaseEndAtTheNextNote()
        {
            // The transcriber found no release for the first two: they run to the end of the audio.
            var draft = new TranscriptionDraft(new[]
            {
                new KanDraftNote { OnsetMs = 1000, EndMs = 60000, DurMs = 59000, Midi = 72 },
                new KanDraftNote { OnsetMs = 1500, EndMs = 60000, DurMs = 58500, Midi = 76 },
                new KanDraftNote { OnsetMs = 2000, EndMs = 2600, DurMs = 600, Midi = 79 },
                new KanDraftNote { OnsetMs = 2400, EndMs = 3000, DurMs = 600, Midi = 60 },
                new KanDraftNote { OnsetMs = 59000, EndMs = 60000, DurMs = 1000, Midi = 74 },
            });

            var piano = KanScore.FromDraft(draft, "keyboard").Notes;
            Assert.That(piano[0].DurationMs, Is.EqualTo(500), "open: ends at the next onset");
            Assert.That(piano[1].DurationMs, Is.EqualTo(500));
            Assert.That(piano[2].DurationMs, Is.EqualTo(600), "a piano note with a real release may overlap the next");
            Assert.That(piano[4].DurationMs, Is.EqualTo(1000), "the last note keeps its end");

            var ocarina = KanScore.FromDraft(draft, "ocarina").Notes;
            Assert.That(ocarina[2].DurationMs, Is.EqualTo(400), "one note at a time: ends where the next starts");
            Assert.That(ocarina[0].DurationMs, Is.EqualTo(500));
        }

        [Test]
        public void NotesHeardTwiceAreKeptOnce()
        {
            // As seen in an ocarina transcription: byte-identical twins, a near twin
            // with a slightly different end, and an unknown-pitch copy of a known note.
            var draft = new TranscriptionDraft(new[]
            {
                new KanDraftNote { OnsetMs = 1000, EndMs = 1500, DurMs = 290, Midi = 79 },
                new KanDraftNote { OnsetMs = 1000, EndMs = 1500, DurMs = 290, Midi = 79 },
                new KanDraftNote { OnsetMs = 2000, EndMs = 2450, DurMs = 240, Midi = 79 },
                new KanDraftNote { OnsetMs = 2000, EndMs = 2470, DurMs = 258, Midi = 79 },
                new KanDraftNote { OnsetMs = 3000, EndMs = 3400, DurMs = 190, Midi = -1 },
                new KanDraftNote { OnsetMs = 3010, EndMs = 3400, DurMs = 180, Midi = 81 },
                new KanDraftNote { OnsetMs = 4000, EndMs = 4400, DurMs = 190, Midi = 76 },
                new KanDraftNote { OnsetMs = 4000, EndMs = 4400, DurMs = 190, Midi = 79 },
                new KanDraftNote { OnsetMs = 5000, EndMs = 5400, DurMs = 190, Midi = 76 },
                new KanDraftNote { OnsetMs = 5100, EndMs = 5400, DurMs = 190, Midi = 76 },
            });

            var ocarina = KanScore.FromDraft(draft, "ocarina").Notes;
            Assert.That(ocarina.Select(n => (n.StartMs, n.Midi)), Is.EqualTo(new[] { (1000.0, 79), (2000.0, 79), (3010.0, 81), (4000.0, 76), (5000.0, 76), (5100.0, 76) }));
            Assert.That(ocarina[1].DurationMs, Is.EqualTo(258), "the longer copy stays");
            Assert.That(ocarina.Any(n => n.PitchUnknown), Is.False, "the known pitch wins");

            // A piano plays chords: two pitches at once stay, and so does the unknown one.
            var piano = KanScore.FromDraft(draft, "keyboard").Notes;
            Assert.That(piano, Has.Count.EqualTo(8));
            Assert.That(piano.Count(n => n.StartMs == 4000), Is.EqualTo(2));

            var score = new KanScore { Notes = piano };
            Assert.That(score.FindRepeats(false), Is.Empty, "nothing left to remove");
        }
    }
}
