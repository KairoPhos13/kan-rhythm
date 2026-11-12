// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Framework.Platform;
using osu.Framework.Screens;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Extensions;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Overlays;
using osu.Game.Screens.Edit;
using osuTK;
using osuTK.Input;
using Kan.Core.Score;
using Kan.Forge.Tracks;

namespace Kan.Forge.Editor.Compose
{
    /// <summary>
    /// Changes to the score: the undo history, the notes following the tempo grid, and saving into the level.
    /// </summary>
    public partial class KanComposeScreen : EditorScreen, IKeyBindingHandler<PlatformAction>, SectionPanel.ITools
    {
        private void record(string? key) => ScoreSession.History.Record(ScoreSession.Score, key);

        /// <summary>
        /// The notes stay on their beats when the timing tab (or the tempo bar)
        /// changes the grid, as osu!'s "adjust existing objects on timing
        /// changes" does for its own objects (<see cref="KanScore.FollowGrid"/>).
        /// Not an undo step: undoing the timing change brings the notes back.
        /// </summary>
        private void followGrid()
        {
            var points = EditorBeatmap.ControlPointInfo.TimingPoints;
            var known = ScoreSession.Score.Grid;

            if (known != null && known.Count == points.Count)
            {
                bool same = true;

                for (int i = 0; i < points.Count && same; i++)
                    same = known[i].TimeMs == points[i].Time && known[i].BeatMs == points[i].BeatLength;

                if (same) return;
            }

            var grid = points.Select(p => new GridPoint { TimeMs = p.Time, BeatMs = p.BeatLength }).ToList();

            // The grid fitted to the music (the tempo bar's "the beat falls here"), or that undone: the notes stay.
            bool fitted = known != null && ScoreSession.KeepsNotes(KanScore.GridKey(known), KanScore.GridKey(grid));
            bool moved = ScoreSession.Score.FollowGrid(grid, !fitted && config.Get<bool>(OsuSetting.EditorAdjustExistingObjectsOnTimingChanges));

            if (moved) roll.Invalidate();
            changed();
        }

        private void changed()
        {
            dirty = true;
            autosaveCountdown = 60;
            inspector.Invalidate();
            monitor.NotesChanged();
        }

        private void undo()
        {
            // A timeline repair is one step with the tracks: undo it there, all together.
            if (ScoreSession.History.UndoLink is { } link)
            {
                if (ForgeTrackSession.History.UndoLink == link)
                    ForgeTrackSession.History.Undo();
                else
                    ForgeNotify.Info(notifications, "Undo the track tab's later changes first: this step repaired the audio too.", FontAwesome.Solid.Undo);
                return;
            }

            applyHistory(ScoreSession.History.Undo(ScoreSession.Score));
        }

        private void redo()
        {
            if (ScoreSession.History.RedoLink is { } link)
            {
                if (ForgeTrackSession.History.RedoLink == link)
                    ForgeTrackSession.History.Redo();
                else
                    ForgeNotify.Info(notifications, "Redo the track tab's changes first: this step repaired the audio too.", FontAwesome.Solid.Redo);
                return;
            }

            applyHistory(ScoreSession.History.Redo(ScoreSession.Score));
        }

        private void applyHistory(KanScore? score)
        {
            if (score == null) return;

            ScoreSession.Replace(score);
            roll.Selected.Clear();
            roll.Invalidate();
            changed();
        }

        private void saveToLevel(bool notify)
        {
            if (ScoreSession.SavedInLevel && !notify) return;

            string? error = ScoreSession.SaveToLevel(working.Value, beatmaps, EditorBeatmap);

            if (error != null)
                ForgeNotify.Error(notifications, error);
            else if (notify)
                ForgeNotify.Info(notifications, $"Saved the score into the level ({ForgeFormat.Plural(ScoreSession.Score.Notes.Count, "note", "notes")}).", FontAwesome.Solid.Save);
        }
    }
}
