// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.IO;
using System.Text;
using osu.Game.Beatmaps;
using osu.Game.Extensions;

namespace Kan.Forge.Editor
{
    /// <summary>A file inside the level open in the editor (kan!'s score and design travel this way).</summary>
    public static class LevelFile
    {
        /// <summary>The file's text, or null when the level has none or it cannot be read.</summary>
        public static string? Read(WorkingBeatmap working, string fileName)
        {
            try
            {
                var file = working.BeatmapSetInfo.GetFile(fileName);
                if (file == null) return null;

                using var stream = working.GetStream(file.File.GetStoragePath());
                if (stream == null) return null;

                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        /// <summary>Write the file into the level. Returns an error message, or null.</summary>
        public static string? Write(WorkingBeatmap working, BeatmapManager beatmaps, string fileName, string text)
        {
            try
            {
                using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
                beatmaps.AddFile(working.BeatmapSetInfo, stream, fileName);
                return null;
            }
            catch (Exception e)
            {
                return e.Message;
            }
        }
    }
}
