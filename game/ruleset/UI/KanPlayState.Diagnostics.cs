// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Game.Replays;
using osu.Game.Screens.Play;
using Kan.Core.Native;
using Kan.Forge.Play;
using Kan.Forge.Tracks;
using Kan.Ruleset.Beatmaps;
using Kan.Ruleset.Objects;
using Kan.Ruleset.Replays;
using Kan.Core.Play;

namespace Kan.Ruleset.UI
{
    /// <summary>
    /// Diagnostics: the play's trace, how the level's clock kept to real time, and the summary
    /// logged when the play ends (the player's timing habit included).
    /// </summary>
    public partial class KanPlayState : Component
    {
        // Timing diagnostics for the play's summary line.
        private double lagSum, lagMin = double.MaxValue, lagMax = double.MinValue;

        private int lagFrames, hitsHeard, hitsInFuture;

        /// <summary>The play, note by note (<see cref="KanPlayTrace"/>); null in replays.</summary>
        internal KanPlayTrace? Trace { get; private set; }

        internal double TraceRealMs => listeningFor.Elapsed.TotalMilliseconds;

        private long lastLagRow;

        private long lastHeartbeat;

        private int judgedCount;

        private bool summaryLogged;

        /// <summary>How late the notes of the last play on the microphone landed (real ms, median), with the player timing it was played with.</summary>
        public readonly record struct LastPlayTiming(double MedianMs, int Notes, double PlayerTimingMs)
        {
            public const int MIN_NOTES = 10;
        }

        /// <summary>The last play on the microphone (owner 2026-10-17: offer to correct the mic delay by it, as osu! does).</summary>
        public static LastPlayTiming? LastPlay { get; private set; }

        /// <summary>One line for the log: how the play went (diagnostics and bug reports).</summary>
        private void logSummary()
        {
            if (summaryLogged) return;

            summaryLogged = true;
            int judged = 0, perfect = 0, good = 0, meh = 0, notHeard = 0, wrong = 0, unclear = 0;
            double offsets = 0;
            var timings = new System.Collections.Generic.List<double>();

            foreach (var v in Verdicts)
            {
                if (v is not { } verdict) continue;

                judged++;

                switch (verdict.Result)
                {
                    case LiveResult.Perfect: perfect++; break;
                    case LiveResult.Good: good++; break;
                    case LiveResult.Meh: meh++; break;
                }

                if (verdict.Reason == LiveReason.NotHeard) notHeard++;
                if (verdict.Reason == LiveReason.WrongNote) wrong++;
                if (verdict.Reason == LiveReason.PitchUnclear) unclear++;
                if (verdict.Result != LiveResult.Miss) offsets += verdict.OffsetMs;
                if (verdict.Result != LiveResult.Miss && verdict.Reason == LiveReason.Played) timings.Add(verdict.OffsetMs / (rate > 0 ? rate : 1));
            }

            // How late the player's notes landed, for "correct by the last play" (the
            // microphone, timed by the level: a wait mode's resumes are timed by itself).
            if (micPlay && !WaitForPlayer && timings.Count >= LastPlayTiming.MIN_NOTES)
            {
                timings.Sort();
                LastPlay = new LastPlayTiming(timings[timings.Count / 2], timings.Count, timeline.PlayerTimingMs);
            }

            if (judged == 0)
            {
                summaryLogged = false;
                return;
            }

            int hits = perfect + good + meh;
            osu.Framework.Logging.Logger.Log($"kan! play: {judged} of {Notes.Count} notes judged: perfect {perfect}, good {good}, meh {meh}, "
                                             + $"wrong note {wrong}, not heard {notHeard}, pitch unclear {unclear}; "
                                             + $"mean offset {(hits > 0 ? offsets / hits : 0):0} ms; instrument tuning {tuning.Cents:+0;-0;0} cents; "
                                             + $"clock vs listening {(lagFrames > 0 ? lagSum / lagFrames : 0):+0;-0;0} ms ({(lagFrames > 0 ? lagMin : 0):+0;-0;0}..{(lagFrames > 0 ? lagMax : 0):+0;-0;0}), "
                                             + $"hits placed after now {hitsInFuture} of {hitsHeard}, mic delay {micDelayMs:0} ms, player timing {timeline.PlayerTimingMs:+0;-0;0} ms; {(failed ? Status.Value : "listening")}");
        }
    }
}
