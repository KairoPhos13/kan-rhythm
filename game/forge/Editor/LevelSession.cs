// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System.Collections.Generic;
using osu.Framework.Platform;
using osu.Game.Beatmaps;
using Kan.Forge.Editor.Design;
using Kan.Forge.Tracks;
using Kan.Core.Tracks;

namespace Kan.Forge.Editor
{
    /// <summary>
    /// The level open in the editor, as every tab sees it: its tracks (and work
    /// folder), its score and its design, always opened together and in that
    /// order — each builds on the one before (the score's autosave lives in the
    /// work folder; the design's automatic sections follow the notes). Every
    /// tab opens it when it loads; it is a no-op for a level already open.
    /// </summary>
    public static class LevelSession
    {
        /// <summary>The key a level's sessions and work folder go by.</summary>
        public static string KeyOf(WorkingBeatmap working) => working.BeatmapSetInfo.ID.ToString("N");

        /// <summary>The level audio's length (0 while it is not loaded).</summary>
        public static double LengthOf(WorkingBeatmap working) => working.TrackLoaded && working.Track.Length > 0 ? working.Track.Length : 0;

        /// <summary>
        /// Open the level. Returns the tracks loaded from its saved set, to be
        /// analysed again (waveforms are not stored); none when it was open.
        /// </summary>
        public static IReadOnlyList<ForgeTrack> Open(Storage storage, WorkingBeatmap working)
        {
            string key = KeyOf(working);
            var loaded = ForgeTrackSession.Open(storage.GetFullPath(@"kan-forge"), key);
            ScoreSession.Open(working, key);
            DesignSession.Open(working, key, ScoreSession.Score.Notes, LengthOf(working));
            return loaded;
        }
    }
}
