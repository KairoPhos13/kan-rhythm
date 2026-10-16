// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Audio.Track;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Platform;
using osu.Game.Beatmaps;
using osu.Game.Extensions;
using Kan.Forge.Editor;
using Kan.Forge.Tracks;

namespace Kan.Forge.Play
{
    /// <summary>
    /// "Wait for me" without a hard stop (owner 2026-10-15): while the level
    /// waits for a note, the last beat before it keeps playing in a loop —
    /// softer and a little muffled — instead of silence. The music still
    /// sounds better played through than waited on, but the step between
    /// them is gentler. One or two beats (from the timing tab's grid), faded
    /// at the seams; silent while the pause menu is open.
    /// </summary>
    public partial class WaitLoop : Component
    {
        private const float loop_volume = 0.55f;
        private const double fade_in_ms = 40;
        private const double fade_out_ms = 70;
        private const double min_loop_ms = 450, max_loop_ms = 1600;

        private readonly Func<double?> waitingAt;
        private readonly Func<bool> silenced;

        [Resolved]
        private AudioManager audio { get; set; } = null!;

        [Resolved]
        private IBindable<WorkingBeatmap> working { get; set; } = null!;

        [Resolved]
        private Storage storage { get; set; } = null!;

        private SetMonitor.Channel? channel;
        private bool active;
        private double loopStart, loopEnd;
        private float level; // the loop's fade (0..1)
        private ITrack? rateSource;

        /// <param name="waitingAt">Where the level stands still (gameplay ms), or null while it plays.</param>
        /// <param name="silenced">The pause menu is open: no loop.</param>
        public WaitLoop(Func<double?> waitingAt, Func<bool> silenced)
        {
            this.waitingAt = waitingAt;
            this.silenced = silenced;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            // The level audio's own file, on a channel of its own.
            var beatmap = working.Value;
            var file = beatmap.BeatmapSetInfo.GetFile(beatmap.Metadata.AudioFile ?? string.Empty);
            if (file == null) return;

            string path = storage.GetStorageForDirectory(@"files").GetFullPath(file.File.GetStoragePath());
            channel = SetMonitor.Channel.Open(audio, new ForgeTrack(@"wait loop", path));

            // Softer and muffled: waiting is not playing.
            channel?.ApplyFilters(new TrackEdits { HighCutHz = 2800 });
        }

        protected override void Update()
        {
            base.Update();

            if (channel == null || !channel.Track.IsLoaded) return;

            var t = channel.Track;
            double? at = silenced() ? null : waitingAt();

            followRate();

            if (at is double stop)
            {
                if (!active || Math.Abs(stop - loopEnd) > 1)
                    begin(stop);

                // Around the loop: fade out before its end, back in after its start.
                double pos = t.CurrentTime;

                if (pos >= loopEnd || pos < loopStart - 50)
                {
                    t.Seek(loopStart);
                    pos = loopStart;
                }

                double envelope = Math.Min(Math.Min(1, (pos - loopStart) / fade_in_ms), Math.Min(1, (loopEnd - pos) / fade_out_ms));
                level = Math.Min(1, level + (float)(Time.Elapsed / 120));
                t.Volume.Value = loop_volume * level * Math.Clamp(envelope, 0, 1);
                return;
            }

            if (!active) return;

            // The level moves on (or the menu opened): let the loop go quickly.
            level = Math.Max(0, level - (float)(Time.Elapsed / 80));
            t.Volume.Value = loop_volume * level;

            if (level <= 0)
            {
                t.Stop();
                active = false;
            }
        }

        private void begin(double stop)
        {
            loopEnd = stop;

            double beat = working.Value.Beatmap.ControlPointInfo.TimingPointAt(stop).BeatLength;
            double length = beat > 0 ? beat : 600;
            while (length < min_loop_ms) length *= 2;
            while (length > max_loop_ms) length /= 2;

            loopStart = Math.Max(0, stop - length);
            level = active ? level : 0;
            active = true;

            osu.Framework.Logging.Logger.Log($"kan!: waiting at {stop:0} ms, looping {loopStart:0}-{stop:0} ms");
            channel!.Track.Seek(loopStart);
            if (!channel.Track.IsRunning) channel.Track.Start();
        }

        /// <summary>Rate mods change the level's speed (and Nightcore/Daycore its pitch): so does the loop.</summary>
        private void followRate()
        {
            var source = working.Value.TrackLoaded ? working.Value.Track : null;
            if (ReferenceEquals(source, rateSource) || channel == null) return;

            if (rateSource != null)
            {
                channel.Track.RemoveAdjustment(AdjustableProperty.Tempo, rateSource.AggregateTempo);
                channel.Track.RemoveAdjustment(AdjustableProperty.Frequency, rateSource.AggregateFrequency);
            }

            rateSource = source;

            if (source != null)
            {
                channel.Track.AddAdjustment(AdjustableProperty.Tempo, source.AggregateTempo);
                channel.Track.AddAdjustment(AdjustableProperty.Frequency, source.AggregateFrequency);
            }
        }

        protected override void Dispose(bool isDisposing)
        {
            channel?.Dispose();
            channel = null;
            base.Dispose(isDisposing);
        }
    }
}
