// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using Kan.Forge.Native;

namespace Kan.Forge.Tracks
{
    /// <summary>
    /// Content analysis of a track (owner requirements 2026-10-13): one
    /// decode pass produces the data behind the **waveform preview** and the
    /// **"no meaningful audio" marking**. After separation, stems that seem
    /// to carry no meaningful sound are tagged and moved to the end of the
    /// list so the user is not misled into thinking they hold content — they
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
        /// floor. Conservative on purpose — a flagged track is only demoted
        /// and labelled, never removed.
        /// </summary>
        public static bool HasMeaningfulAudio(float peak, float rms)
            => peak >= LIKELY_EMPTY_PEAK && rms >= LIKELY_EMPTY_RMS;

        /// <summary>Analyse a track in place (worker thread: it decodes the file).</summary>
        public static void Analyze(ForgeTrack track)
        {
            var peaks = new float[BUCKETS];
            var err = new byte[512];

            KanForgeStatus st = KanForgeNative.AnalyzeTrack(track.Path, peaks, BUCKETS,
                                                            out float peak, out float rms,
                                                            err, err.Length);
            if (st != KanForgeStatus.Ok)
                return; // unanalysed tracks stay unjudged (I2): no flag, no preview

            track.Peaks = peaks;
            track.PeakLevel = peak;
            track.RmsLevel = rms;
            track.LikelyEmpty = !HasMeaningfulAudio(peak, rms);
        }
    }
}
