// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using Kan.Core.Native;

namespace Kan.Core.Tracks
{
    /// <summary>One pending range operation, in the source track's own time (ms).</summary>
    public class PendingRange
    {
        public KanForgeEditMode Mode { get; set; }

        public double FromMs { get; set; }

        public double ToMs { get; set; }

        public PendingRange()
        {
        }

        public PendingRange(KanForgeEditMode mode, double fromMs, double toMs)
        {
            Mode = mode;
            FromMs = Math.Max(0, Math.Min(fromMs, toMs));
            ToMs = Math.Max(fromMs, toMs);
        }

        public bool Contains(double ms) => ms >= FromMs && ms < ToMs;

        public PendingRange Clone() => new PendingRange(Mode, FromMs, ToMs);
    }

    /// <summary>
    /// A track's pending edits (owner 2026-10-14: "hear before saving"):
    /// range operations and filters that are heard live in the track tab
    /// and only become a file when the user saves them as a NEW track; the
    /// source is never modified.
    ///
    /// Preview and render agree on one rule set, all in
    /// the track's own time so the order of the edits doesn't matter:
    /// - <b>keep this</b>: kept parts stay audible, everything outside every
    ///   kept part is muted (several keeps add up); timing is preserved;
    /// - <b>silence this</b>: the part goes silent (wins over a keep);
    /// - <b>trim to this</b>: the saved file is cut down to the part all
    ///   trims share, and starts that much later on the timeline.
    /// </summary>
    public class TrackEdits
    {
        public List<PendingRange> Ranges { get; set; } = new List<PendingRange>();

        /// <summary>High-pass cutoff ("low cut") in Hz; 0 = off.</summary>
        public double LowCutHz { get; set; }

        /// <summary>Low-pass cutoff ("high cut") in Hz; 0 = off.</summary>
        public double HighCutHz { get; set; }

        public bool HasFilters => LowCutHz > 0 || HighCutHz > 0;

        /// <summary>
        /// Pitch shift in semitones, speed unchanged (owner 2026-10-19: a piano
        /// companion written for another range is moved to the ocarina's,
        /// about an octave either way); 0 = off.
        /// </summary>
        public int PitchSemitones { get; set; }

        public const int MAX_PITCH_SEMITONES = 12;

        public bool HasPitch => PitchSemitones != 0;

        public bool IsEmpty => Ranges.Count == 0 && !HasFilters && !HasPitch;

        public TrackEdits Clone() => new TrackEdits
        {
            Ranges = Ranges.Select(r => r.Clone()).ToList(),
            LowCutHz = LowCutHz,
            HighCutHz = HighCutHz,
            PitchSemitones = PitchSemitones,
        };

        private IEnumerable<PendingRange> of(KanForgeEditMode mode) => Ranges.Where(r => r.Mode == mode);

        /// <summary>Whether source time <paramref name="sourceMs"/> is heard after the range edits (live preview).</summary>
        public bool IsAudibleAt(double sourceMs)
        {
            if (of(KanForgeEditMode.Mute).Any(r => r.Contains(sourceMs)))
                return false;

            var keeps = of(KanForgeEditMode.Keep).ToList();
            if (keeps.Count > 0 && !keeps.Any(r => r.Contains(sourceMs)))
                return false;

            return TrimSpan is not { } t || (sourceMs >= t.FromMs && sourceMs < t.ToMs);
        }

        /// <summary>The span all trims agree on, or null when there is no trim.</summary>
        public (double FromMs, double ToMs)? TrimSpan
        {
            get
            {
                var trims = of(KanForgeEditMode.Trim).ToList();
                if (trims.Count == 0) return null;

                return (trims.Max(t => t.FromMs), trims.Min(t => t.ToMs));
            }
        }

        /// <summary>Whether saving would produce audio (the trims don't cancel each other out).</summary>
        public bool IsRenderable => (TrimSpan is not { } t || t.ToMs > t.FromMs) && hasAudiblePart();

        /// <summary>The kept parts merged into disjoint, sorted spans.</summary>
        private List<(double From, double To)> mergedKeeps()
        {
            var merged = new List<(double From, double To)>();

            foreach (var r in of(KanForgeEditMode.Keep).OrderBy(r => r.FromMs))
            {
                if (merged.Count > 0 && r.FromMs <= merged[^1].To)
                    merged[^1] = (merged[^1].From, Math.Max(merged[^1].To, r.ToMs));
                else
                    merged.Add((r.FromMs, r.ToMs));
            }

            return merged;
        }

        /// <summary>Some part survives all the edits (checked between every edit boundary).</summary>
        private bool hasAudiblePart()
        {
            var cuts = Ranges.SelectMany(r => new[] { r.FromMs, r.ToMs }).Append(0).Distinct().OrderBy(c => c).ToList();

            for (int i = 0; i < cuts.Count; i++)
            {
                double probe = i + 1 < cuts.Count ? (cuts[i] + cuts[i + 1]) / 2 : cuts[i] + 1;
                if (IsAudibleAt(probe)) return true;
            }

            return false;
        }

        /// <summary>
        /// The native operations that render the ranges, in order: the gaps
        /// between kept parts and the silenced parts are muted first (timing
        /// preserved, so source time stays valid), then one trim to the
        /// trims' common span (it shifts time).
        /// </summary>
        public IReadOnlyList<PendingRange> RenderPlan()
        {
            var plan = new List<PendingRange>();
            var keeps = mergedKeeps();

            if (keeps.Count > 0)
            {
                double at = 0;

                foreach (var (from, to) in keeps)
                {
                    if (from > at) plan.Add(new PendingRange(KanForgeEditMode.Mute, at, from));
                    at = to;
                }

                plan.Add(new PendingRange(KanForgeEditMode.Mute, at, ForgeTrack.TO_THE_END_MS));
            }

            plan.AddRange(of(KanForgeEditMode.Mute).Select(r => r.Clone()));

            if (TrimSpan is { } t)
                plan.Add(new PendingRange(KanForgeEditMode.Trim, t.FromMs, t.ToMs));

            return plan;
        }

        /// <summary>
        /// Combinations that make no sense: errors make
        /// saving impossible; warnings flag edits that have no effect.
        /// </summary>
        public IReadOnlyList<EditIssue> Validate()
        {
            var issues = new List<EditIssue>();

            if (TrimSpan is { } span && span.ToMs <= span.FromMs)
                issues.Add(EditIssue.Error("The trims don't overlap: nothing would be left to save. Remove one of them."));
            else if (Ranges.Count > 0 && !hasAudiblePart())
                issues.Add(EditIssue.Error("Every part is silenced by these edits: nothing would be heard."));

            var trim = TrimSpan;
            var keeps = of(KanForgeEditMode.Keep).ToList();

            foreach (var r in Ranges)
            {
                string what = $"'{verb(r.Mode)} {fmt(r.FromMs)}-{fmt(r.ToMs)}'";

                if (r.Mode != KanForgeEditMode.Trim && trim is { } t && (r.ToMs <= t.FromMs || r.FromMs >= t.ToMs))
                {
                    issues.Add(EditIssue.Warning(r.Mode == KanForgeEditMode.Keep
                        ? $"{what} is outside the trim: it won't be in the saved track."
                        : $"{what} is outside the trim: it has no effect."));
                    continue;
                }

                if (r.Mode == KanForgeEditMode.Mute && keeps.Count > 0 && !keeps.Any(k => k.FromMs < r.ToMs && r.FromMs < k.ToMs))
                    issues.Add(EditIssue.Warning($"{what} is already silent (it's outside every kept part)."));

                if (r.Mode == KanForgeEditMode.Keep && of(KanForgeEditMode.Mute).Any(m => m.FromMs <= r.FromMs && m.ToMs >= r.ToMs))
                    issues.Add(EditIssue.Warning($"{what} is fully silenced by a 'silence this': it won't be heard."));
            }

            return issues;
        }

        private static string verb(KanForgeEditMode mode) => mode switch
        {
            KanForgeEditMode.Keep => "keep",
            KanForgeEditMode.Mute => "silence",
            _ => "trim to",
        };

        private static string fmt(double ms)
        {
            double s = ms / 1000;
            int m = (int)(s / 60);
            return $"{m}:{s - m * 60:00.0}";
        }

        /// <summary>Where the saved result starts on the set's timeline.</summary>
        public double ResultOffsetMs(double sourceOffsetMs)
            => sourceOffsetMs + (TrimSpan is { } t ? t.FromMs : 0);
    }

    /// <summary>A problem with a combination of edits.</summary>
    public readonly record struct EditIssue(bool IsError, string Message)
    {
        public static EditIssue Error(string message) => new EditIssue(true, message);

        public static EditIssue Warning(string message) => new EditIssue(false, message);
    }
}
