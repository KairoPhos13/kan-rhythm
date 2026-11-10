// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;

namespace Kan.Core.Play
{
    /// <summary>
    /// Where on the level's timeline a moment of the listening stream falls.
    /// The stream counts real milliseconds since listening began; the level's
    /// clock runs at the play's rate, may stand still (the wait mode), and
    /// drifts a little from real time. So the level's time is remembered at
    /// each frame, and a heard moment is placed between the frames that saw it
    /// (a fixed anchor drifted 0-40 ms from the clock, and two notes played
    /// alike could grade differently).
    /// </summary>
    public sealed class ListeningTimeline
    {
        private const int size = 4096;

        /// <summary>Frames this far apart saw the level stand still between them: carry on from the later one.</summary>
        private const double still_gap_ms = 100;

        private readonly double[] seenReal = new double[size];
        private readonly double[] seenLevel = new double[size];
        private int head, count;

        /// <summary>The level's time when listening began.</summary>
        public double AnchorMs { get; private set; }

        /// <summary>The level's pace against real time (rate mods); 1 for a source paced by the level itself.</summary>
        public double Rate { get; private set; } = 1;

        /// <summary>The microphone's round trip: a sound heard now was played this long ago.</summary>
        public double MicDelayMs { get; private set; }

        /// <summary>The player's habit (negative when ahead of the line): their notes are taken as meant.</summary>
        public double PlayerTimingMs { get; private set; }

        /// <summary>A source paced by the level's clock (a file played as the level runs): the anchor alone places it.</summary>
        public bool PacedByLevel { get; private set; }

        /// <summary>Real time the level stood still while listening (the wait mode), taken off the anchored placing.</summary>
        public double FrozenMs { get; private set; }

        /// <summary>Start over: listening begins now, at <paramref name="anchorMs"/> on the level.</summary>
        public void Begin(double anchorMs, double rate, double micDelayMs, double playerTimingMs, bool pacedByLevel)
        {
            AnchorMs = anchorMs;
            Rate = rate > 0 && !pacedByLevel ? rate : 1;
            MicDelayMs = micDelayMs;
            PlayerTimingMs = playerTimingMs;
            PacedByLevel = pacedByLevel;
            FrozenMs = 0;
            count = 0;
        }

        /// <summary>The level stood still for this much more real time.</summary>
        public void Freeze(double realMs) => FrozenMs += realMs;

        /// <summary>What the level's clock showed at a real moment since listening began (call every frame).</summary>
        public void Remember(double realMs, double levelMs)
        {
            if (count > 0 && realMs <= seenReal[(head - 1 + size) % size]) return;

            seenReal[head] = realMs;
            seenLevel[head] = levelMs;
            head = (head + 1) % size;
            count = Math.Min(count + 1, size);
        }

        /// <summary>A moment of the stream (real ms since listening began) on the level's timeline, the mic delay and the player's habit taken off.</summary>
        public double Place(double streamMs)
        {
            double habit = -PlayerTimingMs * Rate;

            if (PacedByLevel || count < 2)
                return AnchorMs + (streamMs - FrozenMs - MicDelayMs) * Rate + habit;

            return LevelAt(streamMs - MicDelayMs) + habit;
        }

        /// <summary>
        /// The level's time at a real moment since listening began: between the
        /// frames that saw it, else carried on at the rate from the nearest one
        /// (a sound newer than the last frame, or across a stop).
        /// </summary>
        public double LevelAt(double realMs)
        {
            if (count == 0) return AnchorMs + (realMs - FrozenMs) * Rate;

            int oldest = (head - count + size) % size;
            int newest = (head - 1 + size) % size;

            if (realMs >= seenReal[newest])
                return seenLevel[newest] + (realMs - seenReal[newest]) * Rate;
            if (realMs <= seenReal[oldest])
                return seenLevel[oldest] + (realMs - seenReal[oldest]) * Rate;

            // Binary search over the ring, in age order.
            int lo = 0, hi = count - 1;

            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (seenReal[(oldest + mid) % size] <= realMs) lo = mid;
                else hi = mid;
            }

            int a = (oldest + lo) % size, b = (oldest + hi) % size;
            double span = seenReal[b] - seenReal[a];

            if (span > still_gap_ms)
                return seenLevel[b] + (realMs - seenReal[b]) * Rate;

            return seenLevel[a] + (seenLevel[b] - seenLevel[a]) * (realMs - seenReal[a]) / span;
        }
    }
}
