// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Audio.Mixing;
using osu.Framework.Audio.Track;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.IO.Stores;
using ManagedBass.Fx;
using osu.Game.Audio.Effects;
using osu.Game.Beatmaps;
using osu.Game.Screens.Edit;
using Kan.Forge.Tracks;
using Kan.Forge.Audio;

namespace Kan.Forge.Editor
{
    /// <summary>
    /// Plays the set while the track tab is open (owner decision
    /// 2026-10-14): the editor's own transport — the play bar at the bottom,
    /// seeking, playback speed — drives a live mix of every track, each on
    /// its own mixer at its start position, following volume, mute and solo
    /// as they change.
    ///
    /// The transport spans the whole set (max(start + length)), not the
    /// level's music: while active, the editor clock runs on a silent track
    /// of that length (<see cref="EditorClock.ChangeSource"/> — no fork
    /// edit), re-sized as tracks move, and the level's music is restored on
    /// deactivation. It also owns the lanes' shared time scale.
    ///
    /// It reads the set, never writes it.
    /// </summary>
    /// <summary>Why a track is (not) heard in the monitor right now.</summary>
    internal enum Hearing
    {
        Heard,
        Soloed,
        Muted,
        SilencedBySolo,
        NotInLevelAudio,
    }

    internal partial class SetMonitor : Component
    {
        /// <summary>Drift (ms) after which a running channel is re-seeked to the editor clock.</summary>
        private const double max_drift_ms = 40;

        /// <summary>Whether the set is being monitored (the track tab is visible).</summary>
        public readonly BindableBool Active = new BindableBool();

        private readonly ForgeTrackSet set;
        private readonly Dictionary<ForgeTrack, AudioChannel> channels = new Dictionary<ForgeTrack, AudioChannel>();
        private readonly Dictionary<ForgeTrack, string> openedPaths = new Dictionary<ForgeTrack, string>();
        private readonly HashSet<ForgeTrack> soloed = new HashSet<ForgeTrack>();

        private readonly BindableDouble silenceLevelMusic = new BindableDouble(0);
        private Track? silencedTrack;

        /// <summary>The silent track the editor clock runs on while active (length = the set's span).</summary>
        private Track? transport;

        private double lastTransportSwap = double.MinValue;
        private int scaleFreezes;

        [Resolved]
        private AudioManager audio { get; set; } = null!;

        [Resolved]
        private EditorClock clock { get; set; } = null!;

        [Resolved]
        private IBindable<WorkingBeatmap> working { get; set; } = null!;

        public SetMonitor(ForgeTrackSet set)
        {
            this.set = set;
        }

        // ------------------------------------------------------------------
        // Listening: solo and mute (owner 2026-10-14: one intuitive rule set)
        //
        // - mute is part of the mix: a muted track is left out of the level
        //   audio too (saved, undoable);
        // - solo is only for listening: "hear only this" (not saved);
        // - a track is never muted and soloed at once: soloing a muted track
        //   unmutes it, muting a soloed track drops its solo;
        // - click solos just this track; Ctrl+click adds it to the solo.
        // ------------------------------------------------------------------

        public bool IsSoloed(ForgeTrack track) => soloed.Contains(track);

        public bool AnySoloed => soloed.Count > 0;

        public int SoloCount => soloed.Count;

        /// <summary>The solo button: hear only this track (or add it to the solo with <paramref name="additive"/>).</summary>
        public void Solo(ForgeTrack track, bool additive)
        {
            if (soloed.Contains(track))
            {
                soloed.Remove(track);
                return;
            }

            if (!additive)
                soloed.Clear();

            soloed.Add(track);

            if (track.Muted)
            {
                ForgeTrackSession.History.Record();
                track.Muted = false;
            }
        }

        /// <summary>The mute button: leave the track out of the mix (and of its solo).</summary>
        public void Mute(ForgeTrack track)
        {
            ForgeTrackSession.History.Record();
            track.Muted = !track.Muted;

            if (track.Muted)
                soloed.Remove(track);
        }

        public void ClearSolo() => soloed.Clear();

        /// <summary>Why a track is (not) heard right now.</summary>
        public Hearing HearingOf(ForgeTrack track)
        {
            if (track.Muted) return Hearing.Muted;
            if (PreviewLevelAudio) return track.IsCompanion ? Hearing.Heard : Hearing.NotInLevelAudio;
            if (AnySoloed && !soloed.Contains(track)) return Hearing.SilencedBySolo;

            return soloed.Contains(track) ? Hearing.Soloed : Hearing.Heard;
        }

        /// <summary>
        /// Hear only the level audio: the unmuted companion tracks within the
        /// level audio window — exactly what the render produces.
        /// </summary>
        public bool PreviewLevelAudio { get; set; }

        /// <summary>Whether the track is heard in the monitor mix right now (level audio preview, solo and mute applied).</summary>
        public bool IsAudible(ForgeTrack track)
        {
            if (PreviewLevelAudio)
                return track.IsCompanion && !track.Muted;

            return AnySoloed ? soloed.Contains(track) : !track.Muted;
        }

        /// <summary>While previewing the level audio, only its window is heard (exactly what the render keeps).</summary>
        private bool inLevelAudioWindow(double timelineMs)
            => !PreviewLevelAudio || (timelineMs >= set.AudioStartMs && (set.AudioEndMs <= 0 || timelineMs < set.AudioEndMs));

        /// <summary>Loudest track output right now (0..1), for the master meter.</summary>
        public float MasterLevel
        {
            get
            {
                float max = 0;

                foreach (var (track, _) in channels)
                {
                    if (IsAudible(track))
                        max = Math.Max(max, LevelOf(track) * track.Gain);
                }

                return max;
            }
        }

        /// <summary>Current output peak of a track (0..1), for the row meters.</summary>
        public float LevelOf(ForgeTrack track)
            => Active.Value && levels.TryGetValue(track, out float level) ? level : 0;

        // ------------------------------------------------------------------
        // Timeline
        // ------------------------------------------------------------------

        /// <summary>Length of a track in ms (the loaded channel's, else the file headers'), or 0 while unknown.</summary>
        public double LengthOf(ForgeTrack track)
        {
            if (channels.TryGetValue(track, out var ch) && ch.Track.IsLoaded && ch.Track.Length > 0)
                return ch.Track.Length;

            return TrackDurations.Get(track.Path) is double seconds ? seconds * 1000 : 0;
        }

        /// <summary>The set's span on the timeline: max(start + length), in ms.</summary>
        public double SpanMs { get; private set; }

        /// <summary>
        /// The lanes' shared scale (ms across the full lane width): the span
        /// plus room to move tracks later. Frozen while a clip is dragged so
        /// the lanes don't rescale under the pointer.
        /// </summary>
        public double ScaleMs { get; private set; } = 10000;

        // ---- shared view (zoom / pan) -------------------------------------

        /// <summary>First visible ms of the lanes.</summary>
        public double ViewStartMs { get; private set; }

        /// <summary>Visible length of the lanes in ms (= <see cref="ScaleMs"/> when not zoomed).</summary>
        public double ViewLengthMs { get; private set; } = 10000;

        /// <summary>While zoomed and playing, page the view to keep the playhead visible.</summary>
        public bool FollowPlayhead { get; set; } = true;

        public bool IsZoomed { get; private set; }

        /// <summary>Shortest visible span (ms) — the most detail the lanes show.</summary>
        public const double MIN_VIEW_MS = 1000;

        /// <summary>Position of a timeline time in the view (0 = left edge, 1 = right edge).</summary>
        public double ToView(double ms) => (ms - ViewStartMs) / Math.Max(1, ViewLengthMs);

        /// <summary>Zoom by <paramref name="factor"/> (&lt; 1 = closer), keeping <paramref name="anchorMs"/> in place.</summary>
        public void ZoomBy(double factor, double anchorMs)
        {
            double length = Math.Clamp(ViewLengthMs * factor, MIN_VIEW_MS, ScaleMs);
            double start = anchorMs - (anchorMs - ViewStartMs) * (length / ViewLengthMs);

            IsZoomed = length < ScaleMs - 1;
            ViewLengthMs = length;
            ViewStartMs = start;
            clampView();
        }

        public void PanBy(double ms)
        {
            if (!IsZoomed) return;

            ViewStartMs += ms;
            clampView();
        }

        /// <summary>Show the whole set again.</summary>
        public void Fit()
        {
            IsZoomed = false;
            ViewStartMs = 0;
            ViewLengthMs = ScaleMs;
        }

        private void clampView()
        {
            ViewLengthMs = Math.Clamp(ViewLengthMs, MIN_VIEW_MS, Math.Max(MIN_VIEW_MS, ScaleMs));
            ViewStartMs = Math.Clamp(ViewStartMs, 0, Math.Max(0, ScaleMs - ViewLengthMs));
        }

        private void followPlayhead()
        {
            if (!IsZoomed || !FollowPlayhead || !clock.IsRunning) return;

            double t = clock.CurrentTime;
            if (t < ViewStartMs || t > ViewStartMs + ViewLengthMs * 0.95)
            {
                ViewStartMs = t - ViewLengthMs * 0.05;
                clampView();
            }
        }

        public void FreezeScale() => scaleFreezes++;

        public void ReleaseScale() => scaleFreezes = Math.Max(0, scaleFreezes - 1);

        private void updateSpan(ForgeTrack[] tracks)
        {
            double span = 0;

            foreach (var t in tracks)
            {
                if (t == null) continue;

                span = Math.Max(span, t.OffsetMs + t.ClipEndOr(LengthOf(t)));
            }

            SpanMs = span;

            if (scaleFreezes == 0)
                ScaleMs = Math.Max(10000, span + Math.Max(5000, span * 0.1));

            if (!IsZoomed)
            {
                ViewStartMs = 0;
                ViewLengthMs = ScaleMs;
            }
            else
                clampView();
        }

        // ------------------------------------------------------------------
        // Sync
        // ------------------------------------------------------------------

        protected override void LoadComplete()
        {
            base.LoadComplete();
            // Applied immediately: a hidden tab may not get further updates.
            Active.BindValueChanged(v =>
            {
                if (!v.NewValue)
                {
                    foreach (var ch in channels.Values)
                        ch.Track.Stop();

                    releaseTransport();
                }

                updateLevelMusicSilence();
            }, true);
        }

        protected override void Update()
        {
            base.Update();

            var tracks = reconcileChannels();
            updateSpan(tracks);
            updateLevelMusicSilence();

            if (Active.Value)
            {
                updateTransport();
                followPlayhead();
            }

            bool running = Active.Value && clock.IsRunning;

            // Sync to the raw position of the track driving the clock. The
            // editor clock adds the platform, global and level offsets; syncing
            // to it would keep every channel "out of sync" by that amount and
            // re-seek it each frame — audible pops.
            double timeline = transport != null && ReferenceEquals(clock.Source, transport) ? transport.CurrentTime : clock.CurrentTime;

            foreach (var (track, ch) in channels)
            {
                var t = ch.Track;
                if (!t.IsLoaded) continue;

                // Each channel plays at its start position on the timeline.
                double now = timeline - track.OffsetMs;
                bool inside = now >= 0 && now < t.Length;

                // Pending edits are heard before they are saved: ranges gate
                // the volume, filters run on the track's own mixer.
                double filePos = t.IsRunning ? t.CurrentTime : now;
                bool heard = IsAudible(track) && track.InClip(filePos) && track.Edits.IsAudibleAt(filePos) && inLevelAudioWindow(timeline);
                // Up to 0 dB through the track volume; boosts above that (up to
                // +20 dB) through a gain stage on the track's own mixer.
                t.Volume.Value = heard ? Math.Clamp(track.Gain, 0, 1) : 0;
                ch.ApplyBoost(track.Gain);
                ch.ApplyFilters(track.Edits);

                if (Active.Value)
                    ch.Sync.Update(t, now, running, Time.Current);
                else if (t.IsRunning)
                    t.Stop();

                resyncs += ch.Sync.TakeResyncs();

                // One level reading per channel per frame, however many meters show it.
                levels[track] = t.IsRunning ? t.CurrentAmplitudes.Maximum : 0;
            }

            // A diagnostic for audio glitches: resyncs are audible, so they should be rare.
            if (Time.Current - resyncWindowStart > 1000)
            {
                if (resyncs > 2)
                    osu.Framework.Logging.Logger.Log($"kan!forge: {resyncs} track resyncs in the last second (each is audible)", level: osu.Framework.Logging.LogLevel.Verbose);

                resyncs = 0;
                resyncWindowStart = Time.Current;
            }
        }

        private readonly Dictionary<ForgeTrack, float> levels = new Dictionary<ForgeTrack, float>();
        private int resyncs;
        private double resyncWindowStart;

        /// <summary>Make the editor clock run on a silent track spanning the set (re-sized as tracks move).</summary>
        private void updateTransport()
        {
            if (SpanMs <= 0) return; // nothing to play yet: leave the editor's own source

            bool lost = transport == null || !ReferenceEquals(clock.Source, transport);
            bool resized = transport != null && Math.Abs(transport.Length - SpanMs) > 1;

            if (!lost && !resized) return;

            // Re-size at most a few times a second while tracks are being dragged.
            if (!lost && Time.Current - lastTransportSwap < 250) return;

            lastTransportSwap = Time.Current;

            if (transport == null || resized)
            {
                var old = transport;
                transport = audio.Tracks.GetVirtual(SpanMs, @"kan-forge-set");
                switchClockTo(transport);
                old?.Dispose();
            }
            else
                switchClockTo(transport);
        }

        /// <summary>Hand the editor clock back to the level's music.</summary>
        private void releaseTransport()
        {
            if (transport == null) return;

            if (ReferenceEquals(clock.Source, transport) && working.Value.TrackLoaded)
                switchClockTo(working.Value.Track);

            transport.Dispose();
            transport = null;
        }

        /// <summary>Change the clock's source keeping its position and play state.</summary>
        private void switchClockTo(Track source)
        {
            double time = clock.CurrentTime;
            bool running = clock.IsRunning;

            clock.ChangeSource(source);
            clock.Seek(Math.Clamp(time, 0, Math.Max(0, source.Length)));

            if (running)
                clock.Start();
        }

        /// <summary>Open a channel for every track of the set; close the ones that left.</summary>
        private ForgeTrack[] reconcileChannels()
        {
            ForgeTrack[] tracks;

            try
            {
                tracks = set.Tracks.ToArray();
            }
            catch
            {
                return Array.Empty<ForgeTrack>();
            }

            foreach (var track in tracks)
            {
                if (track == null) continue;

                if (channels.TryGetValue(track, out var open))
                {
                    // A timeline repair gives the same track a new file: open that.
                    if (openedPaths.GetValueOrDefault(track) == track.Path) continue;

                    open.Dispose();
                    channels.Remove(track);
                }

                var ch = AudioChannel.Open(audio, track);
                openedPaths[track] = track.Path;
                if (ch == null) continue;

                // Follow the editor's playback speed like the level music does.
                ch.Track.AddAdjustment(AdjustableProperty.Tempo, clock.AudioAdjustments.AggregateTempo);
                ch.Track.AddAdjustment(AdjustableProperty.Frequency, clock.AudioAdjustments.AggregateFrequency);
                channels[track] = ch;
            }

            foreach (var gone in channels.Keys.Where(t => !tracks.Contains(t)).ToList())
            {
                channels[gone].Dispose();
                channels.Remove(gone);
                openedPaths.Remove(gone);
                soloed.Remove(gone);
            }

            return tracks;
        }

        /// <summary>While monitoring, the level's music copy is silenced (the set's tracks play instead).</summary>
        private void updateLevelMusicSilence()
        {
            Track? target = Active.Value && working.Value.TrackLoaded ? working.Value.Track : null;
            if (ReferenceEquals(target, silencedTrack)) return;

            silencedTrack?.RemoveAdjustment(AdjustableProperty.Volume, silenceLevelMusic);
            silencedTrack = target;
            silencedTrack?.AddAdjustment(AdjustableProperty.Volume, silenceLevelMusic);
        }

        protected override void Dispose(bool isDisposing)
        {
            releaseTransport();

            silencedTrack?.RemoveAdjustment(AdjustableProperty.Volume, silenceLevelMusic);
            silencedTrack = null;

            foreach (var ch in channels.Values)
                ch.Dispose();
            channels.Clear();

            base.Dispose(isDisposing);
        }
    }
}
