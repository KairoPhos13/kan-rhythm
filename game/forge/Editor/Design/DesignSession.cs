// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using osu.Game.Beatmaps;
using osu.Game.Extensions;
using Kan.Core.Design;
using Kan.Core.Score;
using Kan.Core;

namespace Kan.Forge.Editor.Design
{
    /// <summary>
    /// The open level's design, kept like the score
    /// (<see cref="ScoreSession"/>): the source of truth is the file inside
    /// the level (<see cref="KanDesign.FILE_NAME"/>), written when the user
    /// saves or leaves the design tab; every change is also autosaved to the
    /// level's work folder, and on opening the newer copy wins. A level
    /// without a design starts from <see cref="AutoDesign"/>.
    /// </summary>
    public static class DesignSession
    {
        private const int max_undo = 100;

        public static KanDesign Design { get; private set; } = new KanDesign();

        public static string? LevelKey { get; private set; }

        private static string? savedToLevel;
        private static readonly List<KanDesign> undo = new List<KanDesign>();
        private static readonly List<KanDesign> redo = new List<KanDesign>();

        private static string autosavePath => ForgeTrackSession.WorkPath(@"design.json");

        /// <summary>
        /// Open the level's design (after the track session opened the level).
        /// Automatic sections are generated again from the current notes each
        /// time, so they follow the score.
        /// </summary>
        public static void Open(WorkingBeatmap working, string levelKey, IReadOnlyCollection<ScoreNote> notes, double levelLengthMs)
        {
            if (LevelKey != levelKey)
            {
                LevelKey = levelKey;
                undo.Clear();
                redo.Clear();

                var inLevel = LevelFile.Read(working, KanDesign.FILE_NAME) is string json ? KanDesign.FromJson(json) : null;
                var autosaved = KanDesign.Load(autosavePath);

                Design = LevelDocument.Newer(inLevel, autosaved) ?? AutoDesign.Generate(notes, levelLengthMs);

                savedToLevel = inLevel?.ContentKey();
            }

            Design = AutoDesign.Refresh(Design, notes, levelLengthMs);

            // The stable id this level's work folder goes by (audit B6),
            // stamped from first open so it travels inside the level.
            if (string.IsNullOrEmpty(Design.LevelId))
                Design.LevelId = levelKey;
        }

        /// <summary>Whether the level's design file matches the design being edited.</summary>
        public static bool SavedInLevel => savedToLevel == Design.ContentKey();

        /// <summary>Replace the design (a timeline repair, or its undo).</summary>
        public static void Replace(KanDesign design) => Design = design;

        /// <summary>Call before every change (undo snapshot).</summary>
        public static void Record()
        {
            undo.Add(Design.Clone());
            if (undo.Count > max_undo) undo.RemoveAt(0);
            redo.Clear();
        }

        public static bool CanUndo => undo.Count > 0;

        public static bool CanRedo => redo.Count > 0;

        public static void Undo()
        {
            if (undo.Count == 0) return;

            redo.Add(Design);
            Design = undo[^1];
            undo.RemoveAt(undo.Count - 1);
        }

        public static void Redo()
        {
            if (redo.Count == 0) return;

            undo.Add(Design);
            Design = redo[^1];
            redo.RemoveAt(redo.Count - 1);
        }

        /// <summary>Generate the sections again from the notes, keeping the level's settings (undoable).</summary>
        public static void Regenerate(IReadOnlyCollection<ScoreNote> notes, double levelLengthMs)
        {
            Record();
            Design.Generated = true;
            Design = AutoDesign.Refresh(Design, notes, levelLengthMs);
        }

        public static void AutoSave()
        {
            if (LevelKey == null) return;

            try
            {
                Design.Save(autosavePath);
            }
            catch
            {
                // The level copy is written on save; the autosave is a safety net only.
            }
        }

        /// <summary>Write the design into the level's files. Returns an error message, or null.</summary>
        public static string? SaveToLevel(WorkingBeatmap working, BeatmapManager beatmaps)
        {
            if (LevelKey == null) return null;

            // A guarded save, like the score's (audit B4).
            try
            {
                if (string.IsNullOrEmpty(Design.LevelId))
                    Design.LevelId = LevelKey;

                string? error = LevelFile.Write(working, beatmaps, KanDesign.FILE_NAME, Design.ToJsonStamped());
                if (error != null) return error;

                savedToLevel = Design.ContentKey();
                AutoSave();
                return null;
            }
            catch (Exception e)
            {
                return e.Message;
            }
        }
    }
}
