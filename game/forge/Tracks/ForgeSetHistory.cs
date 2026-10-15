// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Kan.Forge.Tracks
{
    /// <summary>
    /// Undo/redo for the track tab (owner decision 2026-10-14: the tab's own
    /// history; the beatmap's undo is untouched). Snapshots cover every
    /// track's state (roles, volume, mute, start, pending edits, flags) and
    /// the list order.
    ///
    /// Undo never removes tracks imported elsewhere (Resources): only tracks
    /// created by the tab's own actions (<see cref="MarkCreated"/>) — edits,
    /// saves, stems — come and go with the history.
    /// </summary>
    public class ForgeSetHistory
    {
        private const int max_steps = 100;

        /// <summary>Changes with the same key within this window merge into one step (slider drags, typing).</summary>
        private static readonly TimeSpan coalesce_window = TimeSpan.FromSeconds(1);

        private readonly ForgeTrackSet set;
        private readonly List<Snapshot> undo = new List<Snapshot>();
        private readonly List<Snapshot> redo = new List<Snapshot>();
        private readonly ConditionalWeakTable<ForgeTrack, object> created = new ConditionalWeakTable<ForgeTrack, object>();

        private string? lastKey;
        private DateTime lastRecord;

        /// <summary>Clock for coalescing (replaceable in tests).</summary>
        public Func<DateTime> Now { get; set; } = () => DateTime.UtcNow;

        public ForgeSetHistory(ForgeTrackSet set)
        {
            this.set = set;
        }

        public bool CanUndo => undo.Count > 0;

        public bool CanRedo => redo.Count > 0;

        /// <summary>
        /// Call BEFORE a change. A non-null <paramref name="coalesceKey"/>
        /// merges repeated changes (same key, within a second) into one step.
        /// </summary>
        public void Record(string? coalesceKey = null)
        {
            var now = Now();

            if (coalesceKey != null && coalesceKey == lastKey && now - lastRecord < coalesce_window)
            {
                lastRecord = now;
                return;
            }

            lastKey = coalesceKey;
            lastRecord = now;

            undo.Add(Snapshot.Of(set));
            if (undo.Count > max_steps) undo.RemoveAt(0);
            redo.Clear();
        }

        /// <summary>The track was created by a track-tab action (undo may remove it again).</summary>
        public void MarkCreated(ForgeTrack track) => created.AddOrUpdate(track, track);

        public bool Undo() => step(undo, redo);

        public bool Redo() => step(redo, undo);

        public void Clear()
        {
            undo.Clear();
            redo.Clear();
            lastKey = null;
        }

        private bool step(List<Snapshot> from, List<Snapshot> to)
        {
            if (from.Count == 0) return false;

            var target = from[^1];
            from.RemoveAt(from.Count - 1);
            to.Add(Snapshot.Of(set));
            target.Restore(set, t => created.TryGetValue(t, out _));
            lastKey = null;
            return true;
        }

        private sealed class Snapshot
        {
            private readonly List<(ForgeTrack Track, ForgeTrack State)> tracks;
            private readonly double songStart;
            private readonly double songEnd;

            private Snapshot(List<(ForgeTrack, ForgeTrack)> tracks, double songStart, double songEnd)
            {
                this.tracks = tracks;
                this.songStart = songStart;
                this.songEnd = songEnd;
            }

            public static Snapshot Of(ForgeTrackSet set)
                => new Snapshot(set.Tracks.Select(t => (t, t.Clone())).ToList(), set.AudioStartMs, set.AudioEndMs);

            public void Restore(ForgeTrackSet set, Func<ForgeTrack, bool> createdByTab)
            {
                var inSnapshot = new HashSet<ForgeTrack>(tracks.Select(t => t.Track));

                // Tracks imported elsewhere since the snapshot stay (appended).
                var kept = set.Tracks.Where(t => !inSnapshot.Contains(t) && !createdByTab(t)).ToList();

                set.Tracks.Clear();

                foreach (var (track, state) in tracks)
                {
                    apply(state, track);
                    set.Tracks.Add(track);
                }

                set.Tracks.AddRange(kept);
                set.AudioStartMs = songStart;
                set.AudioEndMs = songEnd;
            }

            private static void apply(ForgeTrack from, ForgeTrack to)
            {
                to.Name = from.Name;
                to.Gain = from.Gain;
                to.Muted = from.Muted;
                // Trim before position: each bounds the other.
                to.OffsetMs = Math.Max(to.OffsetMs, 0);
                to.ClipStartMs = from.ClipStartMs;
                to.OffsetMs = from.OffsetMs;
                to.ClipEndMs = from.ClipEndMs;
                to.Edits = from.Edits.Clone();
                to.IsReference = from.IsReference;
                to.IsCompanion = from.IsCompanion;
                to.DemuxPending = from.DemuxPending;
                to.IsSeparated = from.IsSeparated;
                to.IsMixdown = from.IsMixdown;
            }
        }
    }
}
