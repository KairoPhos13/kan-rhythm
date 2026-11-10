// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Kan.Core.Transcription
{
    /// <summary>
    /// The transcribed draft as an editable, time-consistent set of notes.
    ///
    /// This type owns the **hard requirement** of the editor prompt: temporal
    /// edits on the timeline propagate to the transcription. The transcript is
    /// anchored to audio time; when audio segments are cut, moved or deleted,
    /// the notes that fall in them must be shifted, split or removed so the
    /// draft stays consistent with what will actually be heard.
    ///
    /// V1 edit semantics, **physically faithful to the audio edit**:
    /// a note is split at every segment boundary it crosses; the piece inside
    /// the segment is removed or travels with it; the pieces outside stay.
    /// A piece whose end was cut is flagged <see cref="KanDraftNote.EndFaded"/>
    /// (the end is inferred, not a release); every piece touched by an edit is
    /// flagged <see cref="KanDraftNote.FragmentedByEdit"/> so the UI can show
    /// it as a human-fixable proposal rather than a clean measurement.
    ///
    /// - <b>DeleteRange</b>: pieces inside the range are removed; the gap
    ///   stays (silence); outside pieces keep their positions.
    /// - <b>CutRange</b>: like DeleteRange, then the gap closes; everything
    ///   on or after the range end shifts back by its length.
    /// - <b>MoveRange</b>: inside pieces travel with the segment (delta =
    ///   newStart − t0); the source gap stays open; the destination range is
    ///   overwritten with the same rules as DeleteRange before landing.
    /// </summary>
    public class TranscriptionDraft
    {
        public List<KanDraftNote> Notes { get; }

        public TranscriptionDraft(IEnumerable<KanDraftNote>? notes = null)
        {
            Notes = (notes ?? Enumerable.Empty<KanDraftNote>()).Select(n => n.Clone()).ToList();
            Sort();
        }

        public void Sort() => Notes.Sort((a, b) => a.OnsetMs.CompareTo(b.OnsetMs));

        /// <summary>Remove the notes in [t0, t1), leaving a gap.</summary>
        public void DeleteRange(float t0, float t1)
        {
            if (t1 <= t0) return;
            removeRange(t0, t1);
            Sort();
        }

        /// <summary>Remove the notes in [t0, t1) and close the gap.</summary>
        public void CutRange(float t0, float t1)
        {
            if (t1 <= t0) return;
            removeRange(t0, t1);
            shiftOnOrAfter(t1, t0 - t1);
            Sort();
        }

        /// <summary>
        /// Move the audio segment [t0, t1) so it starts at <paramref name="newStart"/>.
        /// Notes travel with their segment; straddlers split; the destination
        /// range is overwritten.
        /// </summary>
        public void MoveRange(float t0, float t1, float newStart)
        {
            if (t1 <= t0) return;
            float delta = newStart - t0;
            float length = t1 - t0;

            var captured = new List<KanDraftNote>();
            var leftBehind = new List<KanDraftNote>();

            foreach (var note in Notes)
            {
                float onset = note.OnsetMs, end = note.EndMs;

                if (end <= t0 || onset >= t1)
                {
                    leftBehind.Add(note); // no overlap with the segment
                    continue;
                }

                // Piece before the segment: stays (its end is now inferred).
                if (onset < t0)
                    leftBehind.Add(split(note, onset, t0, cutEnd: true));

                // Piece inside the segment: travels with it.
                float insideStart = Math.Max(onset, t0);
                float insideEnd = Math.Min(end, t1);
                captured.Add(split(note, insideStart, insideEnd, cutEnd: end > t1));

                // Piece after the segment: stays where the audio stays.
                if (end > t1)
                    leftBehind.Add(split(note, t1, end, cutEnd: false));
            }

            // Source: the captured pieces leave; the gap stays open.
            Notes.Clear();
            Notes.AddRange(leftBehind);

            // Destination: overwrite [newStart, newStart + length).
            removeRange(newStart, newStart + length);

            // Land the captured pieces at their new position.
            foreach (var note in captured)
            {
                note.OnsetMs += delta;
                note.EndMs += delta;
                Notes.Add(note);
            }

            Sort();
        }

        /// <summary>Shift every note starting at or after <paramref name="fromMs"/>.</summary>
        public void ShiftOnOrAfter(float fromMs, float deltaMs)
        {
            shiftOnOrAfter(fromMs, deltaMs);
            Sort();
        }

        private void removeRange(float t0, float t1)
        {
            var kept = new List<KanDraftNote>();

            foreach (var note in Notes)
            {
                float onset = note.OnsetMs, end = note.EndMs;

                if (end <= t0 || onset >= t1)
                {
                    kept.Add(note); // no overlap: untouched (not even flagged)
                    continue;
                }

                if (onset < t0)
                    kept.Add(split(note, onset, t0, cutEnd: true));

                if (end > t1)
                    kept.Add(split(note, t1, end, cutEnd: false));

                // The inside piece is removed with the audio.
            }

            Notes.Clear();
            Notes.AddRange(kept);
        }

        private void shiftOnOrAfter(float fromMs, float deltaMs)
        {
            foreach (var note in Notes)
            {
                if (note.OnsetMs < fromMs) continue;
                note.OnsetMs += deltaMs;
                note.EndMs += deltaMs;
            }
        }

        /// <summary>
        /// A piece of <paramref name="note"/> spanning [newOnsetMs, newEndMs).
        /// Flags: FragmentedByEdit always (an edit created the piece);
        /// EndFaded when the piece's end was cut at a boundary.
        /// </summary>
        private static KanDraftNote split(KanDraftNote note, float newOnsetMs, float newEndMs, bool cutEnd)
        {
            bool fullNote = newOnsetMs == note.OnsetMs && newEndMs == note.EndMs;

            var n = note.Clone();
            n.OnsetMs = newOnsetMs;
            n.EndMs = newEndMs;
            n.DurMs = Math.Max(0.0f, newEndMs - newOnsetMs);
            if (cutEnd) n.EndFaded = true;
            n.FragmentedByEdit = !fullNote || note.FragmentedByEdit;
            return n;
        }
    }
}
