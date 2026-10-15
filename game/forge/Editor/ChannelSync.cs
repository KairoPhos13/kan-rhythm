// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using osu.Framework.Audio.Track;

namespace Kan.Forge.Editor
{
    /// <summary>
    /// Keeps one audio channel in step with the editor's timeline without
    /// audible corrections. A BASS channel reports its decode position, which
    /// runs a constant buffer's length ahead of what is heard (50–150 ms), so
    /// comparing it with the timeline directly makes every channel look "out
    /// of sync" forever — and re-seeking it each frame pops (seen 2026-10-14:
    /// up to 61 resyncs per second). Instead, after each start/seek the
    /// channel's own steady offset is measured once it settles, and only real
    /// drift away from it is corrected — at most twice a second.
    /// </summary>
    public class ChannelSync
    {
        /// <summary>Real drift (beyond the channel's steady offset) that is corrected.</summary>
        private const double max_drift_ms = 50;

        /// <summary>Time for a (re)started channel's reported position to settle before measuring its offset.</summary>
        private const double settle_ms = 250;

        /// <summary>Beyond this the timeline was seeked (not drift): follow at once.</summary>
        private const double seek_jump_ms = 400;

        /// <summary>Never correct the same channel more often than this.</summary>
        private const double min_interval_ms = 500;

        private double startedAt = double.NegativeInfinity;
        private double? baseline;

        /// <summary>
        /// The offset the channel should keep, when known instead of measured:
        /// 0 when following another playing channel (its position runs the
        /// same buffer ahead), so a late start is corrected rather than kept.
        /// </summary>
        public double? ExpectedOffsetMs;

        /// <summary>With a known offset, tighter: two channels of one song a beat apart sound wrong.</summary>
        private const double expected_drift_ms = 15;

        /// <summary>How far ahead to seek so the channel lands in place (a seek takes effect a moment late); learned.</summary>
        private double seekLeadMs;

        /// <summary>Corrections made (each is audible) — for diagnostics.</summary>
        public int Resyncs { get; private set; }

        /// <summary>
        /// Drive <paramref name="track"/> towards <paramref name="positionMs"/>
        /// (its own time). <paramref name="nowMs"/> is a steadily increasing
        /// clock (the drawable's time) used for settling and rate limiting.
        /// </summary>
        public void Update(Track track, double positionMs, bool shouldPlay, double nowMs)
        {
            bool inside = positionMs >= 0 && positionMs < track.Length;

            if (!shouldPlay || !inside)
            {
                if (track.IsRunning) track.Stop();

                // Keep a stopped channel cued, so playback starts in place.
                if (inside && Math.Abs(track.CurrentTime - positionMs) > 1)
                    track.Seek(positionMs);

                baseline = null;
                return;
            }

            if (!track.IsRunning)
            {
                // Seek again once started: a channel's first start can drop a seek
                // made while it was stopped and play from 0 (owner 2026-10-14: the
                // reference began at its start the first time it was switched on).
                double lead = ExpectedOffsetMs != null ? seekLeadMs : 0;
                track.Seek(positionMs + lead);
                track.Start();
                track.Seek(positionMs + lead);
                startedAt = nowMs;
                baseline = null;
                return;
            }

            if (nowMs - startedAt < settle_ms) return;

            double offset = track.CurrentTime - positionMs;

            // The channel's own steady offset (its buffer), measured once settled.
            // A buffer is never this large: the channel is somewhere else, so
            // put it in place instead of keeping the error as its offset.
            if (baseline == null && Math.Abs(offset) > seek_jump_ms)
            {
                track.Seek(positionMs);
                startedAt = nowMs;
                Resyncs++;
                return;
            }

            baseline ??= ExpectedOffsetMs ?? offset;

            double drift = Math.Abs(offset - baseline.Value);

            // A big jump is the user seeking the timeline: follow it at once.
            // Smaller drift is corrected, but never more than twice a second.
            double tolerance = ExpectedOffsetMs != null ? expected_drift_ms : max_drift_ms;

            if (drift > seek_jump_ms || (drift > tolerance && nowMs - startedAt > min_interval_ms))
            {
                // A channel that lands late again is seeked further ahead next time.
                if (ExpectedOffsetMs != null && drift <= seek_jump_ms)
                    seekLeadMs = Math.Clamp(seekLeadMs + baseline.Value - offset, -150, 150);

                track.Seek(positionMs + baseline.Value + (ExpectedOffsetMs != null ? seekLeadMs : 0));
                startedAt = nowMs;
                Resyncs++;
            }
        }

        /// <summary>A seek of the timeline: start over (the next update re-cues the channel).</summary>
        public void Reset()
        {
            baseline = null;
            startedAt = double.NegativeInfinity;
        }

        public int TakeResyncs()
        {
            int r = Resyncs;
            Resyncs = 0;
            return r;
        }
    }
}
