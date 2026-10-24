// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Threading;
using Kan.Core.Native;
using Kan.Forge.Tracks;
using Kan.Core.Transcription;
using Kan.Core.Tracks;

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

        /// <summary>A plain-language name for a profile (what the user picks).</summary>
        public static string ProfileLabel(string profile) => profile switch
        {
            @"keyboard" => @"piano / keyboard",
            @"wind_bowed" => @"other wind or bowed (violin, sax, voice…)",
            @"plucked" => @"other plucked (harp, ukulele…)",
            _ => profile,
        };

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

        /// <summary>
        /// Offline transcription (reports "transcribe" progress). The editor's scores come
        /// from the neural transcriber (Basic Pitch: owner 2026-10-15, measured in
        /// docs/transcription-quality.md); <paramref name="engine"/> =
        /// <see cref="KanForgeNative.ENGINE_DESCRIBER"/> hears as the live judge does.
        /// </summary>
        public static Task<TranscriptionDraft> TranscribeAsync(string audioPath, string profile,
                                                               Action<ForgeProgress>? onProgress = null,
                                                               Scheduler? uiScheduler = null,
                                                               string engine = KanForgeNative.ENGINE_NEURAL)
            => ForgeJobRunner.RunAsync(sink =>
            {
                var notes = new KanForgeNote[65536];
                var err = new byte[err_cap];
                KanForgeStatus st = KanForgeNative.TranscribeFileWith(audioPath, profile, engine, notes, notes.Length,
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
        /// mixed with their gains into a single WAV, each placed at its start
        /// position on the set's timeline (<see cref="ForgeTrack.OffsetMs"/>).
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
                var offsets = list.Select(t => t.OffsetMs).ToArray();

                var err = new byte[err_cap];
                var temps = new List<string>();
                string workDir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(outWav))!;

                // What is heard is what renders: tracks with edits, filters
                // or side trims are prepared first, then placed where their
                // audio sits on the timeline.
                for (int i = 0; i < list.Count; i++)
                {
                    if (!list[i].IsShaped) continue;

                    var (path, offset) = prepare(list[i], workDir, temps);
                    paths[i] = path;
                    offsets[i] = offset;
                }

                KanForgeStatus st = KanForgeNative.MixTracksAt(paths, gains, offsets, list.Count, outWav, err, err_cap);

                deleteAll(temps);

                if (st != KanForgeStatus.Ok)
                    throw new ForgeException(KanForgeNative.ErrMessage(err));
                return outWav;
            }, onProgress, uiScheduler);
        /// <summary>
        /// Render one track the way it is heard — pending edits, filters and
        /// side trims applied — into <paramref name="outWav"/> ("save as new
        /// track"). Returns where the result starts on the timeline.
        /// </summary>
        public static Task<double> RenderTrackAsync(ForgeTrack track, string outWav, Scheduler? uiScheduler = null)
            => ForgeJobRunner.RunAsync(sink =>
            {
                var temps = new List<string>();

                try
                {
                    var (path, offset) = prepare(track, System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(outWav))!, temps);
                    System.IO.File.Copy(path, outWav, true);
                    return offset;
                }
                finally
                {
                    deleteAll(temps);
                }
            }, null, uiScheduler);

        /// <summary>
        /// The track as heard, as a file: range edits through the native
        /// editor (keeps' gaps and silences first, one trim last), then the
        /// filters via BASS — the same biquads the preview runs — then the
        /// pitch shift (BASS_FX, as the preview's). Worker
        /// thread. Intermediate files are added to <paramref name="temps"/>.
        /// </summary>
        private static (string Path, double OffsetMs) prepare(ForgeTrack track, string workDir, List<string> temps)
        {
            var edits = track.EffectiveEdits();

            if (!edits.IsRenderable)
                throw new ForgeException($"'{track.Name}': these edits leave nothing to hear");

            var err = new byte[err_cap];
            string current = track.Path;

            string next()
            {
                string p = System.IO.Path.Combine(workDir, $"prep-{Guid.NewGuid().ToString("N")[..8]}.wav");
                temps.Add(p);
                return p;
            }

            foreach (var op in edits.RenderPlan())
            {
                string output = next();
                KanForgeStatus st = KanForgeNative.EditTrack(current, (float)op.FromMs, (float)op.ToMs, (int)op.Mode, output, err, err_cap);
                if (st != KanForgeStatus.Ok)
                    throw new ForgeException(KanForgeNative.ErrMessage(err));

                current = output;
            }

            if (edits.HasFilters)
            {
                string output = next();
                if (!Media.MediaAudioExtractor.TryFilterToWav(current, output, edits.LowCutHz, edits.HighCutHz, out string error))
                    throw new ForgeException($"filter: {error}");

                current = output;
            }

            if (edits.HasPitch)
            {
                string output = next();
                if (!Media.MediaAudioExtractor.TryPitchShiftToWav(current, output, edits.PitchSemitones, out string error))
                    throw new ForgeException($"pitch: {error}");

                current = output;
            }

            return (current, edits.ResultOffsetMs(track.OffsetMs));
        }

        private static void deleteAll(IEnumerable<string> files)
        {
            foreach (string f in files)
            {
                try { System.IO.File.Delete(f); }
                catch { /* best effort */ }
            }
        }

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

        /// <summary>A file's loudness (LUFS; -∞ for silence) and sample peak, or null when it cannot be read.</summary>
        public static (double Lufs, float Peak)? MeasureLoudness(string audioPath)
        {
            var err = new byte[err_cap];
            return KanForgeNative.MeasureLoudness(audioPath, out float lufs, out float peak, err, err_cap) == KanForgeStatus.Ok
                ? (lufs, peak)
                : null;
        }

        /// <summary>Level a file to <paramref name="targetLufs"/>, its peak under <see cref="LevelLoudness.CEILING_DBFS"/>. Returns its loudness before and after.</summary>
        public static Task<(double Before, double After)> NormalizeLoudnessAsync(string audioPath, double targetLufs, string outWav,
                                                                                Action<ForgeProgress>? onProgress = null,
                                                                                Scheduler? uiScheduler = null)
            => ForgeJobRunner.RunAsync(sink =>
            {
                var err = new byte[err_cap];
                KanForgeStatus st = KanForgeNative.NormalizeLoudness(audioPath, outWav, (float)targetLufs, (float)LevelLoudness.CEILING_DBFS,
                    out float before, out float after, err, err_cap);
                if (st != KanForgeStatus.Ok)
                    throw new ForgeException(KanForgeNative.ErrMessage(err));
                return ((double)before, (double)after);
            }, onProgress, uiScheduler);
    }
}
