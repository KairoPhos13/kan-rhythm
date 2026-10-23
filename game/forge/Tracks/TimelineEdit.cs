// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using Kan.Forge.Design;
using Kan.Forge.Score;

namespace Kan.Forge.Tracks
{
    public enum TimelineEditKind
    {
        /// <summary>Silence pushed in at a point: what follows plays later.</summary>
        InsertSilence,

        /// <summary>A span taken out: what follows closes the gap.</summary>
        Cut,

        /// <summary>A span played faster or slower to a new length, its pitch kept.</summary>
        Stretch,
    }

    /// <summary>
    /// A repair of the timeline's timing (owner 2026-10-22: a cover played
    /// without a click drifts — "the companion speeds up, the solo rushes it" —
    /// and a level made from it cannot be learnt). One edit applies to the
    /// whole level at once: every track of the set (companion and reference,
    /// so the level preview and autoplay hear the repaired reference), the
    /// score's notes and the design's sections. The tempo grid stays: the
    /// edits bring the audio to it.
    ///
    /// One shape for the three kinds: the span [<see cref="FromMs"/>,
    /// <see cref="ToMs"/>) becomes <see cref="NewLengthMs"/> long. Inserting
    /// silence is an empty span that becomes the silence's length; a cut is a
    /// span that becomes nothing. Times are on one timeline (the caller's:
    /// level time for the score, set time for the tracks — see <see cref="Shifted"/>).
    /// </summary>
    public sealed record TimelineEdit(TimelineEditKind Kind, double FromMs, double ToMs, double NewLengthMs)
    {
        public static TimelineEdit InsertSilence(double atMs, double lengthMs) => new TimelineEdit(TimelineEditKind.InsertSilence, atMs, atMs, Math.Max(0, lengthMs));

        public static TimelineEdit Cut(double fromMs, double toMs) => new TimelineEdit(TimelineEditKind.Cut, Math.Min(fromMs, toMs), Math.Max(fromMs, toMs), 0);

        public static TimelineEdit Stretch(double fromMs, double toMs, double newLengthMs)
            => new TimelineEdit(TimelineEditKind.Stretch, Math.Min(fromMs, toMs), Math.Max(fromMs, toMs), Math.Max(0, newLengthMs));

        /// <summary>
        /// The edit that puts a tempo change at <paramref name="nextMs"/> back on
        /// the beats of the tempo before it (starting at <paramref name="tempoMs"/>,
        /// <paramref name="beatMs"/> a beat): silence put in at the change when
        /// the music after it came early, the bit just before it cut when late
        /// — whichever is smaller. Null when it is already on a beat.
        /// </summary>
        public static TimelineEdit? SyncNext(double tempoMs, double beatMs, double nextMs)
        {
            if (beatMs <= 0 || nextMs <= tempoMs) return null;

            double beats = (nextMs - tempoMs) / beatMs;
            double early = Math.Round((Math.Ceiling(beats) - beats) * beatMs, 1);
            double late = Math.Round((beats - Math.Floor(beats)) * beatMs, 1);

            if (Math.Min(early, late) < 1) return null;

            return early <= late ? InsertSilence(nextMs, early) : Cut(nextMs - late, nextMs);
        }

        public double LengthMs => ToMs - FromMs;

        /// <summary>How much later everything after the span plays (negative: earlier).</summary>
        public double DeltaMs => NewLengthMs - LengthMs;

        /// <summary>Whether the edit changes anything.</summary>
        public bool IsEmpty => Math.Abs(DeltaMs) < 0.5 || (Kind == TimelineEditKind.Stretch && LengthMs < 1);

        /// <summary>The same edit on a timeline that starts <paramref name="byMs"/> later (level time → set time: + the level audio's start).</summary>
        public TimelineEdit Shifted(double byMs) => this with { FromMs = FromMs + byMs, ToMs = ToMs + byMs };

        /// <summary>
        /// Where time <paramref name="ms"/> lands after the edit. Before the
        /// span: unchanged; after it: moved by <see cref="DeltaMs"/> (so what
        /// sits right at an inserted silence's point is pushed after it); inside:
        /// scaled into the new length (a cut sends it to the cut's point).
        /// </summary>
        public double Map(double ms)
        {
            if (ms < FromMs) return ms;
            if (ms >= ToMs) return ms + DeltaMs;

            return FromMs + (ms - FromMs) * (NewLengthMs / LengthMs);
        }

        /// <summary>Whether something starting at <paramref name="ms"/> is cut away.</summary>
        public bool Removes(double ms) => Kind == TimelineEditKind.Cut && ms >= FromMs && ms < ToMs;

        /// <summary>
        /// Apply to the score (level time): notes after the span move, notes in
        /// a stretched span stretch with it, notes starting in a cut go (one
        /// reaching into it is shortened). Returns how many notes were removed.
        /// </summary>
        public int ApplyTo(KanScore score)
        {
            int removed = score.Notes.RemoveAll(n => Removes(n.StartMs));

            foreach (var n in score.Notes)
            {
                double start = Map(n.StartMs);
                double end = Map(n.EndMs);
                n.StartMs = start;
                n.DurationMs = Math.Max(KanScore.MIN_DURATION_MS, end - start);
            }

            score.Sort();
            return removed;
        }

        /// <summary>Apply to the design's sections (level time): they keep covering the level; a section cut away entirely goes.</summary>
        public void ApplyTo(KanDesign design)
        {
            foreach (var s in design.Sections)
            {
                s.StartMs = Map(s.StartMs);
                s.EndMs = Map(s.EndMs);
            }

            design.Sections.RemoveAll(s => s.EndMs - s.StartMs < 1);
        }

        /// <summary>
        /// What the edit does to one track (set time): where it starts and what
        /// plays of it afterwards, and the part of its file to render again
        /// (null: the file is untouched, the track only moves).
        /// </summary>
        public TrackChange PlanFor(ForgeTrack track, double fileLengthMs)
        {
            double offset = track.OffsetMs;
            double newOffset = Map(offset);

            // File time → file time after the edit (the new file starts where the old start landed).
            double mapFile(double fileMs) => Map(offset + fileMs) - newOffset;

            // The edit in the file's own time, cut to the file.
            double from = FromMs - offset, to = ToMs - offset;
            TimelineEdit? render = null;

            bool inside = Kind == TimelineEditKind.InsertSilence
                ? from > 0 && from < fileLengthMs
                : to > 0 && from < fileLengthMs;

            if (inside && !IsEmpty)
            {
                double a = Math.Max(0, from), b = Math.Min(fileLengthMs, to);
                render = Kind switch
                {
                    TimelineEditKind.InsertSilence => InsertSilence(a, NewLengthMs),
                    TimelineEditKind.Cut => Cut(a, b),
                    _ => Stretch(a, b, (b - a) * (NewLengthMs / LengthMs)),
                };
            }

            var edits = track.Edits.Clone();

            foreach (var r in edits.Ranges)
            {
                r.FromMs = mapFile(r.FromMs);
                r.ToMs = mapFile(r.ToMs);
            }

            edits.Ranges.RemoveAll(r => r.ToMs - r.FromMs < 1);

            double clipStart = track.ClipStartMs > 0 ? Math.Max(0, mapFile(track.ClipStartMs)) : 0;
            double clipEnd = track.ClipEndMs > 0 ? Math.Max(0, mapFile(track.ClipEndMs)) : 0;

            return new TrackChange(newOffset, clipStart, clipEnd, edits, render);
        }
    }

    /// <summary>
    /// A track after a <see cref="TimelineEdit"/>: its new start, side trims
    /// and pending edits (all moved with the audio), and the edit to render
    /// into a new file, in the file's own time (null: same file).
    /// </summary>
    public sealed record TrackChange(double OffsetMs, double ClipStartMs, double ClipEndMs, TrackEdits Edits, TimelineEdit? Render)
    {
        /// <summary>Apply to the track (its file path is the caller's, after rendering).</summary>
        public void ApplyTo(ForgeTrack track)
        {
            track.OffsetMs = Math.Max(track.OffsetMs, 0); // trim before position: each bounds the other
            track.ClipStartMs = ClipStartMs;
            track.OffsetMs = OffsetMs;
            track.ClipEndMs = ClipEndMs;
            track.Edits = Edits;
        }
    }
}
