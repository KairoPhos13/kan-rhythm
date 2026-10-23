// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using osu.Game.Beatmaps;
using osu.Game.Extensions;
using Kan.Forge.Score;

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

        private static string? savedToLevel;

        private static string autosavePath => ForgeTrackSession.WorkPath(@"score.json");

        /// <summary>Open the level's score (no-op when it is already open). Call after the track session opened the level.</summary>
        public static void Open(WorkingBeatmap working, string levelKey)
        {
            if (LevelKey == levelKey) return;

            LevelKey = levelKey;
            History.Clear();
            sameNotes.Clear();

            var inLevel = loadFromLevel(working);
            var autosaved = KanScore.Load(autosavePath);

            Score = autosaved != null && (inLevel == null || autosaved.SavedAtUtc > inLevel.SavedAtUtc)
                ? autosaved
                : inLevel ?? new KanScore();

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
            // like that — drop them so the score can start over.
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

        /// <summary>Write the score into the level's files. Returns an error message, or null.</summary>
        public static string? SaveToLevel(WorkingBeatmap working, BeatmapManager beatmaps)
        {
            if (LevelKey == null) return null;

            try
            {
                string json = Score.ToJsonStamped();
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                    beatmaps.AddFile(working.BeatmapSetInfo, stream, KanScore.FILE_NAME);

                savedToLevel = Score.ContentKey();
                AutoSave();
                return null;
            }
            catch (Exception e)
            {
                return e.Message;
            }
        }

        private static KanScore? loadFromLevel(WorkingBeatmap working)
        {
            try
            {
                var file = working.BeatmapSetInfo.GetFile(KanScore.FILE_NAME);
                if (file == null) return null;

                using var stream = working.GetStream(file.File.GetStoragePath());
                if (stream == null) return null;

                using var reader = new StreamReader(stream);
                return KanScore.FromJson(reader.ReadToEnd());
            }
            catch
            {
                return null;
            }
        }
    }
}
