// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using osu.Game.Beatmaps;
using osu.Game.Extensions;
using osu.Game.Screens.Edit;
using Kan.Core.Score;
using Kan.Core;

namespace Kan.Forge.Editor
{
    /// <summary>
    /// The open level's score (owner decision 2026-10-14): the source of truth
    /// is the score file inside the level (<see cref="KanScore.FILE_NAME"/>),
    /// written when the user saves or leaves the compose tab. Every edit is
    /// also autosaved to the level's work folder, so nothing is lost if the
    /// game closes first; on opening, the newer of the two copies wins.
    /// </summary>
    public static class ScoreSession
    {
        public static KanScore Score { get; private set; } = new KanScore();

        public static ScoreHistory History { get; } = new ScoreHistory();

        /// <summary>The level whose score is open.</summary>
        public static string? LevelKey { get; private set; }

        /// <summary>
        /// Values the last open had to clamp or drop (a hand-edited or
        /// machine-made file, audit B4; 0 = none): shown by compose, like
        /// <see cref="RemovedBrokenNotes"/>.
        /// </summary>
        public static int RepairedOnLoad { get; set; }

        private static string? savedToLevel;

        private static string autosavePath => ForgeTrackSession.WorkPath(@"score.json");

        /// <summary>Open the level's score (no-op when it is already open). Call after the track session opened the level.</summary>
        public static void Open(WorkingBeatmap working, string levelKey)
        {
            if (LevelKey == levelKey) return;

            LevelKey = levelKey;
            History.Clear();
            sameNotes.Clear();

            int repaired = 0;

            var inLevel = LevelFile.Read(working, KanScore.FILE_NAME) is string json ? KanScore.FromJson(json, out repaired) : null;
            var autosaved = KanScore.Load(autosavePath, out int fromAutosave);
            repaired += fromAutosave;

            Score = LevelDocument.Newer(inLevel, autosaved) ?? new KanScore();
            RepairedOnLoad = repaired;

            // The stable id this level's work folder goes by (audit B6),
            // stamped on the document from first open so it travels inside
            // the level and survives a re-import.
            if (string.IsNullOrEmpty(Score.LevelId))
                Score.LevelId = levelKey;

            savedToLevel = inLevel?.ContentKey();

            // One instrument per level, and the score file is what travels with
            // it: a level with a score says what it is played on; a new score
            // takes the choice made so far in this computer's work folder.
            if (inLevel != null || autosaved != null)
                ForgeTrackSession.Instrument = Score.Instrument;
            else
                Score.Instrument = ForgeTrackSession.Instrument;

            // Before the 2026-10-14 transcription fix, notes crossed into C# as
            // zeros: C-1 at 0 ms with the minimum length. No real note looks
            // like that: drop them so the score can start over.
            int broken = Score.Notes.RemoveAll(IsBrokenNote);
            if (broken > 0)
                RemovedBrokenNotes = broken;
        }

        // Grids reached by fitting the grid to the music (compose's "the beat
        // falls here"): between them the notes stay where they sound, also when
        // the change is undone in the timing tab. Grids joined by such changes
        // share a root.
        private static readonly Dictionary<string, string> sameNotes = new Dictionary<string, string>();

        private static string root(string key)
        {
            while (sameNotes.TryGetValue(key, out string? parent) && parent != key)
                key = parent;
            return key;
        }

        /// <summary>The grid changed from <paramref name="before"/> to <paramref name="after"/> (<see cref="KanScore.GridKey"/>) to fit the music: the notes stay.</summary>
        public static void GridChangeKeepsNotes(string before, string after)
        {
            string a = root(before), b = root(after);
            sameNotes[a] = a;
            if (a != b) sameNotes[b] = a;
        }

        /// <summary>Whether going from one grid to the other keeps the notes where they are.</summary>
        public static bool KeepsNotes(string from, string to) => from == to || root(from) == root(to);

        /// <summary>Notes dropped on opening because the transcription bug had zeroed them (0 = none).</summary>
        public static int RemovedBrokenNotes { get; set; }

        public static bool IsBrokenNote(ScoreNote n) => n.Midi == 0 && n.StartMs == 0 && n.DurationMs <= KanScore.MIN_DURATION_MS;

        /// <summary>Replace the score (undo/redo, starting from a transcription).</summary>
        public static void Replace(KanScore score) => Score = score;

        /// <summary>Whether the level's score file matches the score being edited.</summary>
        public static bool SavedInLevel => savedToLevel == Score.ContentKey();

        public static void AutoSave()
        {
            if (LevelKey == null) return;

            try
            {
                Score.Save(autosavePath);
            }
            catch
            {
                // The level copy is written on save; the autosave is a safety net only.
            }
        }

        /// <summary>
        /// Write the score into the level's files. Returns an error message,
        /// or null. The level's osu! file gets the score's marks at the same
        /// time (<see cref="LevelOsuMarks"/>), so its length follows the
        /// notes without any further step.
        /// </summary>
        /// <param name="editor">The editor's beatmap when the level is open in the editor, kept in step with the marks.</param>
        public static string? SaveToLevel(WorkingBeatmap working, BeatmapManager beatmaps, EditorBeatmap? editor = null)
        {
            if (LevelKey == null) return null;

            // A guarded save (audit B4): this runs on the exit path of the
            // compose tab, and no failure may escape it. The score's own
            // write clamps what the file cannot hold (<see cref="KanScore.Sanitize"/>).
            try
            {
                if (string.IsNullOrEmpty(Score.LevelId))
                    Score.LevelId = LevelKey;

                string? error = LevelFile.Write(working, beatmaps, KanScore.FILE_NAME, Score.ToJsonStamped());
                if (error != null) return $"Could not save the score into the level: {error}";

                savedToLevel = Score.ContentKey();
                AutoSave();

                // The marks travel with the score: a score without notes keeps
                // the level's own objects, so a level being built from an
                // imported song loses nothing here.
                return LevelOsuMarks.Sync(working, beatmaps, Score, editor) is string marks
                    ? $"The score is saved, but the level's osu! file could not keep its marks: {marks}"
                    : null;
            }
            catch (Exception e)
            {
                return $"Could not save the score into the level: {e.Message}";
            }
        }
    }
}
