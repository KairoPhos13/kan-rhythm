// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;

namespace Kan.Core
{
    /// <summary>
    /// A kan! file that travels inside a level (its score, its design). The
    /// editor also autosaves it to the level's work folder, so nothing is
    /// lost if the game closes before the level is saved.
    /// </summary>
    public interface ILevelDocument
    {
        /// <summary>When this copy was written.</summary>
        DateTime SavedAtUtc { get; }
    }

    public static class LevelDocument
    {
        /// <summary>
        /// The copy to open: the level's, or the editor's autosave when it is
        /// newer (edits made since the level was last saved). The editor and
        /// the game follow this one rule.
        /// </summary>
        public static T? Newer<T>(T? inLevel, T? autosaved)
            where T : class, ILevelDocument
            => autosaved != null && (inLevel == null || autosaved.SavedAtUtc > inLevel.SavedAtUtc) ? autosaved : inLevel;

        /// <summary>
        /// The key a level's work folder and autosaves go by: the stable id
        /// its kan! files carry (the score's, else the design's; stamped on
        /// first save into the level), else the set's id (the key levels had
        /// before there was a stable id, so old work folders keep working).
        /// The stable id travels inside the level: a re-import gives the level
        /// a new set id, and the work folder must still be found
        /// (2026-11-12 audit B6).
        /// </summary>
        public static string KeyOf(string? scoreLevelId, string? designLevelId, string setKey)
        {
            if (!string.IsNullOrEmpty(scoreLevelId)) return scoreLevelId!;
            if (!string.IsNullOrEmpty(designLevelId)) return designLevelId!;
            return setKey;
        }
    }
}
