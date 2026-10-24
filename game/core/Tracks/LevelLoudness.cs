// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;

namespace Kan.Core.Tracks
{
    /// <summary>
    /// How loud a level's audio is rendered: every level at one reference
    /// loudness, so moving from one to the next never needs the volume
    /// touched (owner 2026-10-23: "when I open the game my ears burst, then
    /// I have to turn it up because I can barely hear the songs" — eight
    /// levels spread over 12 dB, from -16.7 to -28.5 LUFS). The reference is
    /// 0 dB; the creator may set a level louder or softer from it.
    /// </summary>
    public static class LevelLoudness
    {
        /// <summary>
        /// The reference (LUFS, ITU-R BS.1770). Rendered mixes peak near full
        /// scale at about -20 LUFS, so this lines them all up without a
        /// limiter colouring the sound (the owner's eight levels: within ±1 dB).
        /// </summary>
        public const double REFERENCE_LUFS = -20;

        /// <summary>No sample above this (dBFS) after levelling: a quiet level with sharp peaks comes up only as far as they allow.</summary>
        public const double CEILING_DBFS = -1;

        /// <summary>How far the creator may set a level from the reference (dB, either way).</summary>
        public const double MAX_OFFSET_DB = 12;

        /// <summary>Bumped when the levelling changes: level audio rendered before is rendered again.</summary>
        public const int VERSION = 1;

        /// <summary>The loudness a level is rendered to, at the creator's offset from the reference.</summary>
        public static double TargetLufs(double offsetDb) => REFERENCE_LUFS + ClampOffset(offsetDb);

        public static double ClampOffset(double offsetDb) => Math.Clamp(double.IsFinite(offsetDb) ? offsetDb : 0, -MAX_OFFSET_DB, MAX_OFFSET_DB);

        /// <summary>Where a measured loudness sits against the reference (dB; positive = louder); null for silence.</summary>
        public static double? FromReference(double lufs) => double.IsFinite(lufs) ? lufs - REFERENCE_LUFS : null;
    }
}
