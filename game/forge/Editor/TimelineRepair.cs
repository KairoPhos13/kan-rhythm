// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kan.Core.Design;
using Kan.Forge.Editor.Design;
using Kan.Forge.Media;
using Kan.Forge.Pipeline;
using Kan.Core.Score;
using Kan.Forge.Tracks;
using Kan.Core.Tracks;

namespace Kan.Forge.Editor
{
    /// <summary>
    /// A timeline repair (<see cref="TimelineEdit"/>) applied to the open
    /// level: every track of the set rendered again where the edit reaches it,
    /// then, in one step of the track set's history, linked to the score's
    /// (<see cref="ScoreHistory.UndoLink"/>), the tracks, the score and the
    /// design. Undoing it in either tab brings all of them back.
    /// </summary>
    public static class TimelineRepair
    {
        /// <summary>A track's new file, rendered off the update thread.</summary>
        public sealed record Rendered(ForgeTrack Track, TrackChange Change, string? NewPath, ForgeTrack? Analysed);

        /// <summary>
        /// The level's tempo grid, as the editor holds it: the timing points
        /// after a repair move with the music (they mark where it is, owner
        /// 2026-10-22: "the metronome was already fitted in the timing tab"),
        /// and the step brings them back on undo.
        /// </summary>
        public interface IGrid
        {
            object Capture();

            void Restore(object state);

            /// <summary>Move the grid with the edit (level time).</summary>
            void Apply(TimelineEdit levelEdit);

            /// <summary>The grid now, for the score (<see cref="KanScore.Grid"/>).</summary>
            IReadOnlyList<GridPoint> Points { get; }
        }

        /// <summary>Raised (update thread) when a repair is undone or redone, in either tab.</summary>
        public static event Action? Restored;

        /// <summary>
        /// Render what the edit (level time) does to each track. Slow (it
        /// decodes and writes every track it reaches): call off the update
        /// thread. <paramref name="tracks"/>: the tracks with their new work
        /// paths, picked on the update thread.
        /// </summary>
        public static List<Rendered> Render(TimelineEdit setEdit, IReadOnlyList<(ForgeTrack Track, string OutPath)> tracks)
        {
            var result = new List<Rendered>();

            foreach (var (track, outPath) in tracks)
            {
                double length = File.Exists(track.Path) ? MediaAudioExtractor.LengthMs(track.Path) : 0;
                var change = setEdit.PlanFor(track, length);

                if (change.Render is not { } r)
                {
                    result.Add(new Rendered(track, change, null, null));
                    continue;
                }

                if (!MediaAudioExtractor.TryRetimeToWav(track.Path, outPath, r.FromMs, r.ToMs, r.NewLengthMs, out string error))
                    throw new ForgeException($"could not repair \"{track.Name}\": {error}");

                var analysed = new ForgeTrack(track.Name, outPath);
                TrackAnalysis.Analyze(analysed);
                result.Add(new Rendered(track, change, outPath, analysed));
            }

            return result;
        }

        /// <summary>
        /// Apply rendered tracks and the edit (level time) to the level, as one
        /// undo step. Update thread. Returns how many notes the edit removed.
        /// </summary>
        public static int Apply(TimelineEdit levelEdit, IReadOnlyList<Rendered> rendered, IGrid? grid = null)
        {
            var step = new Step(grid);
            ForgeTrackSession.History.Record(null, step);
            ScoreSession.History.Record(ScoreSession.Score, null, link: step);

            foreach (var r in rendered)
            {
                if (r.NewPath != null && r.Analysed != null)
                {
                    r.Track.Path = r.NewPath;
                    r.Track.Peaks = r.Analysed.Peaks;
                    r.Track.AnalysisFailed = r.Analysed.AnalysisFailed;
                    r.Track.PeakLevel = r.Analysed.PeakLevel;
                    r.Track.RmsLevel = r.Analysed.RmsLevel;
                    r.Track.LikelyEmpty = r.Analysed.LikelyEmpty;
                }

                r.Change.ApplyTo(r.Track);
            }

            // The level audio's window ends where its end went (its start stays: the edits are on the level's timeline).
            var set = ForgeTrackSession.Tracks;
            if (set.AudioEndMs > 0)
                set.AudioEndMs = levelEdit.Shifted(set.AudioStartMs).Map(set.AudioEndMs);

            int removed = levelEdit.ApplyTo(ScoreSession.Score);

            // The tempo changes after the edit follow the music; the notes already moved with it.
            if (grid != null)
            {
                grid.Apply(levelEdit);
                ScoreSession.Score.FollowGrid(grid.Points, move: false);
            }

            if (DesignSession.LevelKey == ScoreSession.LevelKey)
            {
                levelEdit.ApplyTo(DesignSession.Design);
                DesignSession.AutoSave();
            }

            ForgeTrackSession.Save();
            ScoreSession.AutoSave();
            return removed;
        }

        /// <summary>The score and design a repair step carries (<see cref="ForgeSetHistory.IStepState"/>).</summary>
        private sealed class Step : ForgeSetHistory.IStepState
        {
            private readonly IGrid? grid;

            public Step(IGrid? grid)
            {
                this.grid = grid;
            }

            public object Capture() => (ScoreSession.Score.Clone(), DesignSession.LevelKey == ScoreSession.LevelKey ? DesignSession.Design.Clone() : null, grid?.Capture());

            public void Restore(object state)
            {
                var (score, design, gridState) = ((KanScore, KanDesign?, object?))state;

                // The grid first: the score coming back already knows it (its Grid), so no note moves.
                if (grid != null && gridState != null) grid.Restore(gridState);

                // Keep the score's own history in step: its linked entry moves too.
                if (ScoreSession.History.UndoLink == this)
                    ScoreSession.History.Undo(ScoreSession.Score);
                else if (ScoreSession.History.RedoLink == this)
                    ScoreSession.History.Redo(ScoreSession.Score);

                ScoreSession.Replace(score.Clone());
                ScoreSession.AutoSave();

                if (design != null && DesignSession.LevelKey == ScoreSession.LevelKey)
                {
                    DesignSession.Replace(design.Clone());
                    DesignSession.AutoSave();
                }

                ForgeTrackSession.Save();
                Restored?.Invoke();
            }
        }
    }
}
