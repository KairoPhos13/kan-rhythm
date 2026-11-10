// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System.Linq;
using NUnit.Framework;
using Kan.Core.Transcription;

namespace Kan.Core.Tests
{
    /// <summary>
    /// Offline tests for the hard requirement of the editor prompt §2.2:
    /// temporal edits propagate to the transcription consistently
    /// (shift / split / remove). Known input, expected output. The edit model
    /// is physically faithful to the audio edit: notes split at every segment
    /// boundary they cross.
    /// </summary>
    [TestFixture]
    public class TranscriptionDraftTest
    {
        private static TranscriptionDraft makeDraft()
        {
            // Timeline (ms): A[0,100) B[100,200) C[200,300) D[400,500) E[500,600)
            return new TranscriptionDraft(new[]
            {
                note(0, 100), note(100, 200), note(200, 300), note(400, 500), note(500, 600),
            });
        }

        private static KanDraftNote note(float onset, float end) => new KanDraftNote
        {
            OnsetMs = onset,
            EndMs = end,
            DurMs = end - onset,
            Midi = 60,
        };

        [Test]
        public void TestDeleteRangeRemovesInsidePiecesAndSplitsStraddlers()
        {
            var draft = makeDraft();

            // Delete [150, 450):
            //   B[100,200) crosses the left edge -> keeps [100,150);
            //   C[200,300) is fully inside -> removed;
            //   D[400,500) starts inside but crosses the right edge -> keeps [450,500);
            //   A and E are untouched.
            draft.DeleteRange(150, 450);

            Assert.That(draft.Notes.Select(n => (n.OnsetMs, n.EndMs)),
                        Is.EqualTo(new[] { (0f, 100f), (100f, 150f), (450f, 500f), (500f, 600f) }));

            Assert.That(draft.Notes[1].EndFaded, Is.True, "B's kept piece ends at a cut edge (inferred)");
            Assert.That(draft.Notes[1].FragmentedByEdit, Is.True);
            Assert.That(draft.Notes[2].FragmentedByEdit, Is.True, "D's kept piece starts at a cut edge");
            Assert.That(draft.Notes[2].EndFaded, Is.False, "D's kept piece ends where the note ended");
            Assert.That(draft.Notes[0].FragmentedByEdit, Is.False, "untouched notes are not flagged");
            Assert.That(draft.Notes[3].FragmentedByEdit, Is.False);

            // The gap stays (silence): E keeps its position.
            Assert.That(draft.Notes[3].OnsetMs, Is.EqualTo(500f));
        }

        [Test]
        public void TestCutRangeClosesTheGap()
        {
            var draft = makeDraft();

            // Cut [100, 400): B and C are fully inside -> removed; A is
            // untouched (it ends exactly at the edge); D and E shift back by
            // 300 ms.
            draft.CutRange(100, 400);

            Assert.That(draft.Notes.Select(n => (n.OnsetMs, n.EndMs)),
                        Is.EqualTo(new[] { (0f, 100f), (100f, 200f), (200f, 300f) }));

            Assert.That(draft.Notes[0].EndFaded, Is.False, "A ends exactly at the cut edge: untouched");
            Assert.That(draft.Notes[1].FragmentedByEdit, Is.False, "whole notes that shift stay clean");
        }

        [Test]
        public void TestMoveRangeSplitsStraddlersAndTravelsWithTheSegment()
        {
            var draft = makeDraft();

            // Move [150, 450) to start at 600 (delta +450):
            //   B splits at 150: [100,150) stays, [150,200) travels;
            //   C[200,300) travels whole;
            //   D splits at 450: [400,450) travels, [450,500) stays;
            //   destination [600,900) is overwritten first (was empty here).
            draft.MoveRange(150, 450, 600);

            Assert.That(draft.Notes.Select(n => (n.OnsetMs, n.EndMs)),
                        Is.EqualTo(new[]
                        {
                            (0f, 100f), (100f, 150f), (450f, 500f), (500f, 600f),
                            (600f, 650f), (650f, 750f), (850f, 900f),
                        }));

            Assert.That(draft.Notes[1].FragmentedByEdit, Is.True, "B was split at the source edge");
            Assert.That(draft.Notes[2].FragmentedByEdit, Is.True, "D was split at the source edge");
            Assert.That(draft.Notes[3].FragmentedByEdit, Is.False, "E was not touched");
            Assert.That(draft.Notes[4].FragmentedByEdit, Is.True, "B's travelling piece is a fragment");
            Assert.That(draft.Notes[5].FragmentedByEdit, Is.False, "C travelled whole, not a fragment");
            Assert.That(draft.Notes[6].EndFaded, Is.True, "D's travelling piece ends at a cut edge");
        }

        [Test]
        public void TestMoveRangeOverwritesDestination()
        {
            var draft = makeDraft();

            // Move [0, 100) (note A) to start at 450. The destination
            // [450, 550) is overwritten first (DeleteRange rules): D[400,500)
            // keeps [400,450), E[500,600) keeps [550,600). Then A lands whole
            // at [450, 550).
            draft.MoveRange(0, 100, 450);

            Assert.That(draft.Notes.Select(n => (n.OnsetMs, n.EndMs)),
                        Is.EqualTo(new[]
                        {
                            (100f, 200f), (200f, 300f),
                            (400f, 450f), (450f, 550f), (550f, 600f),
                        }));

            // Only D's and E's kept pieces are fragments; A travelled whole.
            Assert.That(draft.Notes.Count(n => n.FragmentedByEdit), Is.EqualTo(2));
            Assert.That(draft.Notes.Single(n => n.OnsetMs == 450).FragmentedByEdit, Is.False);
        }

        [Test]
        public void TestShiftOnOrAfter()
        {
            var draft = makeDraft();
            draft.ShiftOnOrAfter(200, 50);

            Assert.That(draft.Notes.Select(n => n.OnsetMs),
                        Is.EqualTo(new[] { 0f, 100f, 250f, 450f, 550f }));
        }

        [Test]
        public void TestDraftClonesInputAndStaysSorted()
        {
            var source = new[] { note(200, 300), note(0, 100) };
            var draft = new TranscriptionDraft(source);

            Assert.That(draft.Notes.Select(n => n.OnsetMs), Is.EqualTo(new[] { 0f, 200f }));

            // Editing the draft must not mutate the caller's notes.
            draft.DeleteRange(0, 1000);
            Assert.That(draft.Notes, Is.Empty);
            Assert.That(source.Length, Is.EqualTo(2));
        }

        [Test]
        public void TestFactStatesFollowTheUnjudgedRule()
        {
            var unresolved = new KanDraftNote { Midi = -1 };
            Assert.That(unresolved.PitchState(), Is.EqualTo(KanFactState.Unjudged),
                        "unresolvable pitch is unjudged, never wrong");

            var measured = new KanDraftNote { Midi = 60, Cents = -3, DynDb = -6 };
            Assert.That(measured.PitchState(), Is.EqualTo(KanFactState.Measured));
            Assert.That(measured.ExpressionState(), Is.EqualTo(KanFactState.Measured));

            measured.PitchEdited = true;
            Assert.That(measured.PitchState(), Is.EqualTo(KanFactState.HumanEdited));

            // Comparison metadata not produced by the offline transcriber
            // yet: null = unjudged.
            Assert.That(measured.Presence, Is.Null);
            Assert.That(measured.PitchSaturated, Is.Null);
        }
    }
}
