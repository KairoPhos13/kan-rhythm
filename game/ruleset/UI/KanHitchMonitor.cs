// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Framework.Threading;

namespace Kan.Ruleset.UI
{
    /// <summary>
    /// Finds what a stutter during play was (owner 2026-10-19: "lag in the
    /// normal modes ruins the immersion", cause unknown): a slow frame is
    /// logged with which thread took long (update, draw, audio), the garbage
    /// collector's pauses since the last frame, and how hard the note
    /// describer worked — its slowest 10 ms block and the most audio it found
    /// waiting (a lot means it fell behind, so judgements came late). The
    /// play's totals are logged when it ends. Cheap: a few clock reads per frame.
    /// </summary>
    public partial class KanHitchMonitor : Component
    {
        /// <summary>A frame this long is a stutter the player sees (about 3 frames at 60 Hz).</summary>
        private const double hitch_ms = 50;

        /// <summary>The describer is behind when this much audio waits for it.</summary>
        private const double backlog_ms = 60;

        private readonly KanPlayState state;

        private GameThread? updateThread;
        private GameThread? drawThread;
        private GameThread? audioThread;

        private int[] collections = new int[3];
        private TimeSpan pauses;

        private int hitches;
        private double worstUpdate, worstDraw, worstAudio, worstChunk, worstBacklog;
        private TimeSpan pausesAtStart;
        private int[] collectionsAtStart = new int[3];
        private long lastLog;

        public KanHitchMonitor(KanPlayState state)
        {
            this.state = state;
        }

        [BackgroundDependencyLoader]
        private void load(GameHost host)
        {
            // The threads' own clocks: this component's clock is the level's.
            updateThread = host.UpdateThread;
            drawThread = host.DrawThread;
            audioThread = host.AudioThread;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            collections = gcCounts();
            collectionsAtStart = gcCounts();
            pauses = pausesAtStart = GC.GetTotalPauseDuration();
        }

        protected override void Update()
        {
            base.Update();

            double update = updateThread?.Clock.ElapsedFrameTime ?? 0;
            double draw = drawThread?.Clock.ElapsedFrameTime ?? 0;
            double audio = audioThread?.Clock.ElapsedFrameTime ?? 0;
            var (chunk, backlog) = state.Session?.TakeLoad() ?? (0, 0);

            var nowCollections = gcCounts();
            var nowPauses = GC.GetTotalPauseDuration();
            double gcMs = (nowPauses - pauses).TotalMilliseconds;

            worstUpdate = Math.Max(worstUpdate, update);
            worstDraw = Math.Max(worstDraw, draw);
            worstAudio = Math.Max(worstAudio, audio);
            worstChunk = Math.Max(worstChunk, chunk);
            worstBacklog = Math.Max(worstBacklog, backlog);

            if (update > hitch_ms || draw > hitch_ms || audio > hitch_ms || backlog > backlog_ms)
            {
                hitches++;

                // At most a few lines a second, however bad it gets.
                if (Environment.TickCount64 - lastLog > 250)
                {
                    lastLog = Environment.TickCount64;
                    Logger.Log($"kan!: hitch at {state.ClockMs / 1000:0.0} s — update {update:0} ms, draw {draw:0} ms, audio {audio:0} ms; "
                               + $"GC {gcMs:0} ms (gen0 +{nowCollections[0] - collections[0]}, gen1 +{nowCollections[1] - collections[1]}, gen2 +{nowCollections[2] - collections[2]}); "
                               + $"describer {chunk:0.0} ms per 10 ms, {backlog:0} ms waiting");
                }
            }

            collections = nowCollections;
            pauses = nowPauses;
        }

        private static int[] gcCounts() => new[] { GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2) };

        protected override void Dispose(bool isDisposing)
        {
            if (IsLoaded)
            {
                var nowCollections = gcCounts();
                Logger.Log($"kan!: play frames — {hitches} hitches over {hitch_ms:0} ms; worst update {worstUpdate:0} ms, draw {worstDraw:0} ms, audio {worstAudio:0} ms; "
                           + $"GC paused {(GC.GetTotalPauseDuration() - pausesAtStart).TotalMilliseconds:0} ms in all (gen0 {nowCollections[0] - collectionsAtStart[0]}, gen1 {nowCollections[1] - collectionsAtStart[1]}, gen2 {nowCollections[2] - collectionsAtStart[2]}); "
                           + $"describer worst {worstChunk:0.0} ms per 10 ms, {worstBacklog:0} ms waiting at most",
                    level: LogLevel.Important);
            }

            base.Dispose(isDisposing);
        }
    }
}
