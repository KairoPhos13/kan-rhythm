// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Threading;
using Kan.Forge.Native;
using Kan.Forge.Tracks;
using Kan.Forge.Transcription;

namespace Kan.Forge.Pipeline
{
    /// <summary>Progress report as it leaves the native layer (worker thread).</summary>
    public readonly record struct ForgeProgress(string Phase, int Percent);

    /// <summary>
    /// A first-class failure of a pipeline job (missing model, missing
    /// separator, unsupported codec, failed download…) — human-readable and
    /// recoverable, per the editor prompt §3.
    /// </summary>
    public class ForgeException : Exception
    {
        public ForgeException(string message)
            : base(message)
        {
        }
    }

    /// <summary>
    /// Background execution of the libkan offline APIs.
    ///
    /// **Dispatch decision (documented per the editor prompt §3):** the native
    /// progress callback fires on the worker thread that runs the libkan job.
    /// This runner marshals every report to the osu!framework update thread
    /// through a captured <see cref="Scheduler"/> (the UI-facing callbacks are
    /// plain Actions scheduled via <c>Scheduler.Add</c>). If no scheduler is
    /// supplied, reports are delivered inline on the worker thread (headless
    /// consumers, tests).
    ///
    /// Jobs are **serialized** (one at a time): the libkan progress contract
    /// is process-global state, and model downloads/separations must not
    /// overlap anyway.
    /// </summary>
    public static class ForgeJobRunner
    {
        private static readonly SemaphoreSlim gate = new(1, 1);

        private static Action<ForgeProgress>? current_sink;

        // Kept alive for the process lifetime: the native side holds the pointer.
        private static readonly KanForgeProgressFn progress_thunk = on_native_progress;

        static ForgeJobRunner()
        {
            KanForgeNative.SetProgress(progress_thunk, IntPtr.Zero);
        }

        private static void on_native_progress(IntPtr user, IntPtr phaseUtf8, int percent)
        {
            string phase = Marshal.PtrToStringAnsi(phaseUtf8) ?? string.Empty;
            current_sink?.Invoke(new ForgeProgress(phase, percent));
        }

        /// <summary>
        /// Run one pipeline job on a worker thread. Progress reports are
        /// marshalled to <paramref name="uiScheduler"/> when provided; the
        /// task completes with the job's result or a <see cref="ForgeException"/>.
        /// </summary>
        public static async Task<T> RunAsync<T>(Func<Action<ForgeProgress>, T> work,
                                                Action<ForgeProgress>? onProgress = null,
                                                Scheduler? uiScheduler = null)
        {
            await gate.WaitAsync().ConfigureAwait(false);

            Action<ForgeProgress> sink = report =>
            {
                if (onProgress == null) return;

                if (uiScheduler != null)
                    uiScheduler.Add(() => onProgress(report));
                else
                    onProgress(report);
            };

            current_sink = sink;

            try
            {
                return await Task.Run(() => work(sink)).ConfigureAwait(false);
            }
            finally
            {
                current_sink = null;
                gate.Release();
            }
        }
    }

    /// <summary>The libkan offline jobs as used by the editor.</summary>
    public static class ForgeJobs
    {
        private const int err_cap = 512;

        /// <summary>Instrument profile names accepted by the bridge (I10 families).</summary>
        public static string[] Profiles { get; } = { @"keyboard", @"wind_bowed", @"plucked", @"ocarina", @"flute", @"recorder", @"guitar" };

        /// <summary>Fetch audio from a URL to a WAV file (reports "download" progress).</summary>
        public static Task<string> FetchAudioAsync(string url, string outWav, Action<ForgeProgress>? onProgress = null, Scheduler? uiScheduler = null)
            => ForgeJobRunner.RunAsync(sink =>
            {
                var err = new byte[err_cap];
                KanForgeStatus st = KanForgeNative.FetchAudio(url, outWav, err, err_cap);
                if (st != KanForgeStatus.Ok)
                    throw new ForgeException(KanForgeNative.ErrMessage(err));
                return outWav;
            }, onProgress, uiScheduler);

        /// <summary>Source separation (reports "separate" progress). Returns the stem directory.</summary>
        public static Task<string> SeparateStemsAsync(string audioPath, string outDir, Action<ForgeProgress>? onProgress = null, Scheduler? uiScheduler = null)
            => ForgeJobRunner.RunAsync(sink =>
            {
                var err = new byte[err_cap];
                KanForgeStatus st = KanForgeNative.SeparateStems(audioPath, outDir, err, err_cap);
                if (st != KanForgeStatus.Ok)
                    throw new ForgeException(KanForgeNative.ErrMessage(err));
                return System.IO.Path.Combine(outDir, "roformer");
            }, onProgress, uiScheduler);

        /// <summary>Offline transcription (reports "transcribe" progress).</summary>
        public static Task<TranscriptionDraft> TranscribeAsync(string audioPath, string profile,
                                                               Action<ForgeProgress>? onProgress = null,
                                                               Scheduler? uiScheduler = null)
            => ForgeJobRunner.RunAsync(sink =>
            {
                var notes = new KanForgeNote[65536];
                var err = new byte[err_cap];
                KanForgeStatus st = KanForgeNative.TranscribeFile(audioPath, profile, notes, notes.Length,
                                                                 out int count, err, err_cap);

                if (st == KanForgeStatus.ErrCapacity)
                    throw new ForgeException($"transcription produced {count} notes, more than the editor buffer — report this");

                if (st != KanForgeStatus.Ok)
                    throw new ForgeException(KanForgeNative.ErrMessage(err));

                var draft = new TranscriptionDraft();
                for (int i = 0; i < count; i++)
                {
                    KanForgeNote n = notes[i];
                    draft.Notes.Add(new KanDraftNote
                    {
                        OnsetMs = n.OnsetMs,
                        EndMs = n.EndMs,
                        DurMs = n.DurMs,
                        Midi = n.Midi,
                        Cents = n.Cents,
                        DynDb = n.DynDb,
                        VibRate = n.VibRate,
                        VibDepth = n.VibDepth,
                        EndFaded = n.EndFaded != 0,
                        Transition = n.Transition != 0,
                    });
                }

                draft.Sort();
                return draft;
            }, onProgress, uiScheduler);

        /// <summary>
        /// Render one playable track from the track set (the editor's final
        /// "render a single track for playback" step): audible tracks are
        /// mixed with their gains into a single WAV.
        /// </summary>
        public static Task<string> RenderMixAsync(IEnumerable<ForgeTrack> tracks, string outWav,
                                                  Action<ForgeProgress>? onProgress = null,
                                                  Scheduler? uiScheduler = null)
            => ForgeJobRunner.RunAsync(sink =>
            {
                // Muted tracks are excluded from the render entirely (they
                // must not extend the rendered mix either).
                var list = tracks.Where(t => !t.Muted).ToList();
                if (list.Count == 0)
                    throw new ForgeException("render: no audible tracks — unmute or add tracks first");

                var paths = list.Select(t => t.Path).ToArray();
                var gains = list.Select(t => t.Gain).ToArray();

                var err = new byte[err_cap];
                KanForgeStatus st = KanForgeNative.MixTracks(paths, gains, list.Count, outWav, err, err_cap);
                if (st != KanForgeStatus.Ok)
                    throw new ForgeException(KanForgeNative.ErrMessage(err));
                return outWav;
            }, onProgress, uiScheduler);
        /// <summary>
        /// Track editor (non-destructive): produce a NEW track from a time
        /// range of an existing one — KEEP/MUTE/TRIM semantics of
        /// <see cref="KanForgeEditMode"/>. The source is never modified; the
        /// caller adds the result to its track list under a user-chosen name.
        /// </summary>
        public static Task<string> EditTrackAsync(string audioPath, KanForgeEditMode mode,
                                                  float startMs, float endMs, string outWav,
                                                  Action<ForgeProgress>? onProgress = null,
                                                  Scheduler? uiScheduler = null)
            => ForgeJobRunner.RunAsync(sink =>
            {
                var err = new byte[err_cap];
                KanForgeStatus st = KanForgeNative.EditTrack(audioPath, startMs, endMs, (int)mode,
                                                             outWav, err, err_cap);
                if (st != KanForgeStatus.Ok)
                    throw new ForgeException(KanForgeNative.ErrMessage(err));
                return outWav;
            }, onProgress, uiScheduler);
    }
}
