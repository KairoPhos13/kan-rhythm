// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Objects;
using osu.Game.Screens.Edit;
using Kan.Core.Score;

namespace Kan.Forge.Editor
{
    /// <summary>
    /// Keeps the level's osu!-format file in step with its score, so the
    /// game and the server can read the level's length and object count from
    /// the .osu (game/core/Score/LevelMarks.cs and
    /// docs/architecture/level-metadata.md). The marks are plain circles at
    /// the notes' starts and at the level's end: the ruleset replaces them
    /// with the real notes when the level is played, and they survive the
    /// editor's own saves untouched.
    /// </summary>
    public static class LevelOsuMarks
    {
        /// <summary>The mark objects for a score (one per note, one at the level's end).</summary>
        public static IReadOnlyList<HitObject> ObjectsFor(IReadOnlyList<ScoreNote> notes)
            => LevelMarks.TimesOf(notes).Select(EditorObject).ToList();

        /// <summary>
        /// The ruleset's own object for a mark, for the editor and for the
        /// level's .osu alike. The editor holds the ruleset's objects (a
        /// <c>Beatmap&lt;KanHitObject&gt;</c>), which refuses a plain
        /// <see cref="HitObject"/>: saving from compose failed with "is not of
        /// type KanHitObject" (owner 2026-11-16). And the .osu encoder reads a
        /// position from every object of a level claiming legacy ruleset id
        /// 0, which a plain <see cref="HitObject"/> lacks (issue 42 step 2).
        /// The ruleset sets this when it starts (forge cannot see its types);
        /// plain marks otherwise.
        /// </summary>
        public static Func<double, HitObject> EditorObject { get; set; } = time => new HitObject { StartTime = time };

        /// <summary>Whether a level's .osu already carries the score's marks (within half a ms).</summary>
        public static bool Matches(IBeatmap beatmap, IReadOnlyList<ScoreNote> notes)
        {
            var marks = LevelMarks.TimesOf(notes);
            return beatmap.HitObjects.Count == marks.Count
                   && beatmap.HitObjects.Zip(marks, (o, mark) => Math.Abs(o.StartTime - mark) <= 0.5).All(m => m);
        }

        /// <summary>
        /// Write the score's marks into the level's .osu. This is the one
        /// supported way to change a level's osu! file
        /// (<see cref="BeatmapManager.Save"/>): it keeps the file's checksums
        /// right and reprocesses the level, so the length, BPM and star
        /// rating shown in song select follow at once. A level without notes
        /// keeps whatever its .osu carries (a level being built from an
        /// imported song may still rely on its original objects). Returns an
        /// error message, or null.
        /// </summary>
        /// <param name="editor">
        /// The editor's beatmap, when the level is open in kan!forge: its
        /// objects are replaced by the marks too, so the editor's own save
        /// writes the same marks.
        /// </param>
        public static string? Sync(WorkingBeatmap working, BeatmapManager beatmaps, KanScore score, EditorBeatmap? editor = null)
        {
            if (score.Notes.Count == 0)
                return null;

            var marks = ObjectsFor(score.Notes);

            try
            {
                if (editor != null)
                {
                    editor.Clear();
                    editor.AddRange(marks.Select(m => EditorObject(m.StartTime)).ToList());
                }

                if (working.Beatmap is not Beatmap beatmap)
                    return "the level's osu! file could not be read";

                if (Matches(beatmap, score.Notes))
                    return null;

                beatmap.HitObjects = marks.ToList();
                beatmaps.Save(working.BeatmapInfo, beatmap, working.Skin, working.Storyboard);
                return null;
            }
            catch (Exception e)
            {
                return e.Message;
            }
        }
    }
}
