// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using Kan.Forge.Tracks;

namespace Kan.Forge.Editor
{
    /// <summary>
    /// The editor session's shared track set (owner model 2026-10-13): one
    /// unified list of resources — imported in Resources, arranged/edited in
    /// the "tracks" tab. Both surfaces read and write this set.
    ///
    /// v1 note: the set lives for the process lifetime (the editor screens
    /// share it); persisting the resources into the level store is tracked
    /// in game/TODO.md.
    /// </summary>
    public static class ForgeTrackSession
    {
        public static ForgeTrackSet Tracks { get; } = new();
    }
}
