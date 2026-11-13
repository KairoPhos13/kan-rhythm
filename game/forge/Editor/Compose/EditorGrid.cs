// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Screens.Edit;
using Kan.Core.Score;
using Kan.Core.Tracks;

namespace Kan.Forge.Editor.Compose
{
    /// <summary>
    /// The editor's tempo grid, for repairs (<see cref="TimelineRepair.IGrid"/>).
    /// It holds the editor's beatmap, never the screen it was made in: a
    /// repair step's history outlives the compose screen, and undoing from
    /// the track tab must bring the grid back too (2026-11-12 audit B11: a
    /// closed compose screen left the tempo grid behind). The beatmap belongs
    /// to the editor and lives as long as the editor does; a restore after
    /// the editor itself is gone is a quiet no-op on a detached beatmap.
    /// </summary>
    public sealed class EditorGrid : TimelineRepair.IGrid
    {
        private readonly EditorBeatmap beatmap;

        public EditorGrid(EditorBeatmap beatmap)
        {
            this.beatmap = beatmap;
        }

        private sealed record Point(double Time, double BeatLength, osu.Game.Beatmaps.Timing.TimeSignature Signature);

        public object Capture() => beatmap.ControlPointInfo.TimingPoints.Select(p => new Point(p.Time, p.BeatLength, p.TimeSignature)).ToList();

        public void Restore(object state) => set((List<Point>)state);

        public void Apply(TimelineEdit levelEdit)
        {
            set(((List<Point>)Capture()).Where(p => !levelEdit.Removes(p.Time))
                                        .Select(p => p with { Time = Math.Round(levelEdit.Map(p.Time), 1) })
                                        .ToList());
        }

        public IReadOnlyList<GridPoint> Points
            => beatmap.ControlPointInfo.TimingPoints.Select(p => new GridPoint { TimeMs = p.Time, BeatMs = p.BeatLength }).ToList();

        private void set(List<Point> points)
        {
            var cpi = beatmap.ControlPointInfo;
            beatmap.BeginChange();

            foreach (var tp in cpi.TimingPoints.ToList())
            {
                var group = cpi.GroupAt(tp.Time);
                group?.Remove(tp);
                if (group != null && group.ControlPoints.Count == 0) cpi.RemoveGroup(group);
            }

            foreach (var p in points)
                cpi.Add(p.Time, new TimingControlPoint { BeatLength = p.BeatLength, TimeSignature = p.Signature });

            beatmap.EndChange();
        }
    }
}
