// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Runtime.InteropServices;

namespace Kan.Forge.Native
{
    /// <summary>Status codes of the kan_forge_api C ABI (kan_forge_api.h).</summary>
    public enum KanForgeStatus
    {
        Ok = 0,
        ErrArgs = 1,
        ErrFetch = 2,
        ErrSeparator = 3,
        ErrDecode = 4,
        ErrTranscribe = 5,
        ErrCapacity = 6,
    }

    /// <summary>
    /// One transcribed note as it crosses the C ABI. Fixed-size POD (the ABI
    /// contract: no allocations across the boundary). Booleans are 32-bit.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct KanForgeNote
    {
        public float OnsetMs;
        public float EndMs;
        public float DurMs;
        public int Midi;
        public float Cents;
        public float DynDb;
        public float VibRate;
        public float VibDepth;
        public int EndFaded;
        public int Transition;
    }

    /// <summary>
    /// Progress callback as seen by the native side. IMPORTANT: it fires on
    /// the worker thread running the libkan job — dispatch to the UI thread
    /// is the caller's responsibility (see Kan.Forge.Pipeline.ForgeJobRunner).
    /// </summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void KanForgeProgressFn(IntPtr user, IntPtr phaseUtf8, int percent);

    /// <summary>Track-edit modes of the kan_forge_api C ABI (non-destructive).</summary>
    public enum KanForgeEditMode
    {
        /// <summary>Keep [start, end), silence the rest (timing preserved).</summary>
        Keep = 0,

        /// <summary>Silence [start, end), keep the rest (timing preserved).</summary>
        Mute = 1,

        /// <summary>Crop to [start, end) — tight file, offset lost.</summary>
        Trim = 2,
    }

    /// <summary>
    /// P/Invoke surface of libkan_forge_native — the thin adapter that keeps
    /// libkan invocation out of the rest of the game code (minimal fork
    /// divergence rule: everything behind this class).
    /// </summary>
    public static class KanForgeNative
    {
        public const string LIBRARY_NAME = @"kan_forge_native";

        [DllImport(LIBRARY_NAME, EntryPoint = @"kan_forge_set_progress", CallingConvention = CallingConvention.Cdecl)]
        public static extern void SetProgress(KanForgeProgressFn? fn, IntPtr user);

        [DllImport(LIBRARY_NAME, EntryPoint = @"kan_forge_fetch_audio", CallingConvention = CallingConvention.Cdecl)]
        public static extern KanForgeStatus FetchAudio(string url, string outWav, byte[] err, int errCap);

        [DllImport(LIBRARY_NAME, EntryPoint = @"kan_forge_separate_stems", CallingConvention = CallingConvention.Cdecl)]
        public static extern KanForgeStatus SeparateStems(string audioPath, string outDir, byte[] err, int errCap);

        [DllImport(LIBRARY_NAME, EntryPoint = @"kan_forge_transcribe_file", CallingConvention = CallingConvention.Cdecl)]
        public static extern KanForgeStatus TranscribeFile(string audioPath, string profile,
                                                          KanForgeNote[] outNotes, int cap,
                                                          out int outCount, byte[] err, int errCap);

        [DllImport(LIBRARY_NAME, EntryPoint = @"kan_forge_version", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr versionPtr();

        [DllImport(LIBRARY_NAME, EntryPoint = @"kan_forge_has_separator", CallingConvention = CallingConvention.Cdecl)]
        public static extern bool HasSeparator();

        [DllImport(LIBRARY_NAME, EntryPoint = @"kan_forge_has_downloader", CallingConvention = CallingConvention.Cdecl)]
        public static extern bool HasDownloader();

        [DllImport(LIBRARY_NAME, EntryPoint = @"kan_forge_mix_tracks", CallingConvention = CallingConvention.Cdecl)]
        public static extern KanForgeStatus MixTracks([In, MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPStr)] string[] paths,
                                                     float[] gains, int count, string outWav,
                                                     byte[] err, int errCap);

        [DllImport(LIBRARY_NAME, EntryPoint = @"kan_forge_edit_track", CallingConvention = CallingConvention.Cdecl)]
        public static extern KanForgeStatus EditTrack(string audioPath, float startMs, float endMs,
                                                     int mode, string outWav, byte[] err, int errCap);

        [DllImport(LIBRARY_NAME, EntryPoint = @"kan_forge_analyze_track", CallingConvention = CallingConvention.Cdecl)]
        public static extern KanForgeStatus AnalyzeTrack(string audioPath,
                                                        [Out] float[] peaks, int bucketCount,
                                                        out float outPeak, out float outRms,
                                                        byte[] err, int errCap);

        public static string Version()
        {
            IntPtr ptr = versionPtr();
            return ptr == IntPtr.Zero ? @"(unknown)" : Marshal.PtrToStringAnsi(ptr) ?? @"(unknown)";
        }

        /// <summary>Decode the native err buffer into a human-readable message.</summary>
        public static string ErrMessage(byte[] err)
        {
            int len = Array.IndexOf(err, (byte)0);
            if (len < 0) len = err.Length;
            return System.Text.Encoding.UTF8.GetString(err, 0, len);
        }
    }
}
