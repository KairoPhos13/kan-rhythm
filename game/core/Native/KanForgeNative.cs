// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Runtime.InteropServices;

namespace Kan.Core.Native
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

    /// <summary>One live fact (kan_forge_live_event): measurements only, the game judges.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct KanForgeLiveEvent
    {
        public uint NoteId;
        public int Kind;
        public int Stage;
        public float TMs;
        public float OnsetMs;
        public float Midi;
        public float Cents;
        public float EndMs;
        public float Level;
        public float Presence;
        public float Similarity;
        public uint Flags;
        public uint Group;

        public const int HIT = 0, PITCH = 1, CLOSE = 2, FRAME = 3, CANDIDATE = 4;
        public const int PROVISIONAL = 0, FINAL = 1, UNRESOLVED = 2;
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
        public static extern KanForgeStatus FetchAudio(string url, string outWav, [Out] byte[] err, int errCap);

        [DllImport(LIBRARY_NAME, EntryPoint = @"kan_forge_separate_stems", CallingConvention = CallingConvention.Cdecl)]
        public static extern KanForgeStatus SeparateStems(string audioPath, string outDir, [Out] byte[] err, int errCap);

        [DllImport(LIBRARY_NAME, EntryPoint = @"kan_forge_transcribe_file", CallingConvention = CallingConvention.Cdecl)]
        public static extern KanForgeStatus TranscribeFile(string audioPath, string profile,
                                                          // [In, Out]: the native side writes the notes; without it the
                                                          // marshaller does not copy them back (every note read as 0).
                                                          [In, Out] KanForgeNote[] outNotes, int cap,
                                                          out int outCount, [Out] byte[] err, int errCap);

        /// <summary>
        /// Like <see cref="TranscribeFile"/>, choosing the transcriber: <see cref="ENGINE_NEURAL"/>
        /// (Basic Pitch, owner 2026-10-15) or <see cref="ENGINE_DESCRIBER"/> (the live describer).
        /// </summary>
        [DllImport(LIBRARY_NAME, EntryPoint = @"kan_forge_transcribe_file_with", CallingConvention = CallingConvention.Cdecl)]
        public static extern KanForgeStatus TranscribeFileWith(string audioPath, string profile, string engine,
                                                              [In, Out] KanForgeNote[] outNotes, int cap,
                                                              out int outCount, [Out] byte[] err, int errCap);

        public const string ENGINE_NEURAL = @"neural";
        public const string ENGINE_DESCRIBER = @"describer";

        [DllImport(LIBRARY_NAME, EntryPoint = @"kan_forge_version", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr versionPtr();

        [DllImport(LIBRARY_NAME, EntryPoint = @"kan_forge_has_separator", CallingConvention = CallingConvention.Cdecl)]
        public static extern bool HasSeparator();

        [DllImport(LIBRARY_NAME, EntryPoint = @"kan_forge_has_downloader", CallingConvention = CallingConvention.Cdecl)]
        public static extern bool HasDownloader();

        [DllImport(LIBRARY_NAME, EntryPoint = @"kan_forge_mix_tracks", CallingConvention = CallingConvention.Cdecl)]
        public static extern KanForgeStatus MixTracks([In, MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPStr)] string[] paths,
                                                     float[] gains, int count, string outWav,
                                                     [Out] byte[] err, int errCap);

        [DllImport(LIBRARY_NAME, EntryPoint = @"kan_forge_mix_tracks_at", CallingConvention = CallingConvention.Cdecl)]
        public static extern KanForgeStatus MixTracksAt([In, MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPStr)] string[] paths,
                                                       float[] gains, double[] offsetsMs, int count, string outWav,
                                                       [Out] byte[] err, int errCap);

        [DllImport(LIBRARY_NAME, EntryPoint = @"kan_forge_edit_track", CallingConvention = CallingConvention.Cdecl)]
        public static extern KanForgeStatus EditTrack(string audioPath, float startMs, float endMs,
                                                     int mode, string outWav, [Out] byte[] err, int errCap);

        [DllImport(LIBRARY_NAME, EntryPoint = @"kan_forge_analyze_track", CallingConvention = CallingConvention.Cdecl)]
        public static extern KanForgeStatus AnalyzeTrack(string audioPath,
                                                        [Out] float[] peaks, int bucketCount,
                                                        out float outPeak, out float outRms,
                                                        [Out] byte[] err, int errCap);

        [DllImport(LIBRARY_NAME, EntryPoint = @"kan_forge_measure_loudness", CallingConvention = CallingConvention.Cdecl)]
        public static extern KanForgeStatus MeasureLoudness(string audioPath, out float outLufs, out float outPeak,
                                                           [Out] byte[] err, int errCap);

        [DllImport(LIBRARY_NAME, EntryPoint = @"kan_forge_normalize_loudness", CallingConvention = CallingConvention.Cdecl)]
        public static extern KanForgeStatus NormalizeLoudness(string audioPath, string outWav, float targetLufs, float ceilingDbfs,
                                                             out float outBefore, out float outAfter,
                                                             [Out] byte[] err, int errCap);

        [DllImport(LIBRARY_NAME, EntryPoint = @"kan_forge_live_open", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr LiveOpen(string profile, float sampleRate);

        [DllImport(LIBRARY_NAME, EntryPoint = @"kan_forge_live_process", CallingConvention = CallingConvention.Cdecl)]
        public static extern int LiveProcess(IntPtr live, [In] float[] samples, int count, [Out] KanForgeLiveEvent[] events, int cap);

        [DllImport(LIBRARY_NAME, EntryPoint = @"kan_forge_live_expect", CallingConvention = CallingConvention.Cdecl)]
        public static extern int LiveExpect(IntPtr live, [In] float[] midis, int count);

        [DllImport(LIBRARY_NAME, EntryPoint = @"kan_forge_live_close", CallingConvention = CallingConvention.Cdecl)]
        public static extern void LiveClose(IntPtr live);

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
