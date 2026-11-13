// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using Kan.Core.Native;

namespace Kan.Core.Tracks
{
    /// <summary>
    /// Content analysis of a track (owner requirements 2026-10-13): one
    /// decode pass produces the data behind the **waveform preview** and the
    /// **"no meaningful audio" marking**. After separation, stems that seem
    /// to carry no meaningful sound are tagged and moved to the end of the
    /// list so the user is not misled into thinking they hold content; they
    /// stay selectable for the cases where the user knows better.
    /// </summary>
    public static class TrackAnalysis
    {
        /// <summary>Buckets of the waveform preview.</summary>
        public const int BUCKETS = 96;

        /// <summary>Below this whole-file peak (≈ −54 dBFS) a track is likely empty.</summary>
        public const float LIKELY_EMPTY_PEAK = 0.002f;

        /// <summary>Below this RMS a track is likely empty.</summary>
        public const float LIKELY_EMPTY_RMS = 0.0005f;

        /// <summary>
        /// "Meaningful audio" rule: both peak and RMS must clear the noise
        /// floor. Conservative on purpose: a flagged track is only demoted
        /// and labelled, never removed.
        /// </summary>
        public static bool HasMeaningfulAudio(float peak, float rms)
            => peak >= LIKELY_EMPTY_PEAK && rms >= LIKELY_EMPTY_RMS;

        /// <summary>
        /// What one analysis pass measured. It is handed to the track on the
        /// update thread (<see cref="ApplyTo"/>): the set is read by the UI
        /// every frame and only written on the update thread
        /// (<see cref="ForgeTrackSet.Tracks"/>' rule; 2026-11-12 audit B8).
        /// </summary>
        public sealed record Result(float[]? Peaks, float PeakLevel, float RmsLevel, bool LikelyEmpty, bool Failed)
        {
            /// <summary>Take the measurements onto a track (update thread).</summary>
            public void ApplyTo(ForgeTrack track)
            {
                track.Peaks = Peaks;
                track.AnalysisFailed = Failed;
                track.PeakLevel = PeakLevel;
                track.RmsLevel = RmsLevel;
                track.LikelyEmpty = LikelyEmpty;
            }
        }

        /// <summary>
        /// Analyse a track's file (worker thread: it decodes the file) and
        /// return the measurements; the track itself is never touched here.
        /// </summary>
        public static Result Analyze(ForgeTrack track)
        {
            var peaks = new float[BUCKETS];
            var err = new byte[512];

            KanForgeStatus st = KanForgeNative.AnalyzeTrack(track.Path, peaks, BUCKETS,
                                                            out float peak, out float rms,
                                                            err, err.Length);
            if (st != KanForgeStatus.Ok)
            {
                // Unanalysed tracks stay unjudged (I2): no "empty" flag, no
                // preview, but the failure is visible, not an endless wait.
                return new Result(null, 0, 0, false, true);
            }

            return new Result(peaks, peak, rms, !HasMeaningfulAudio(peak, rms), false);
        }
    }
}
