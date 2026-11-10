// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Kan.Core.Tracks
{
    /// <summary>
    /// One track of the editor's unified track list. Everything the user
    /// loads lands in a single list, and each track can take a **role**
    /// (owner naming 2026-10-14):
    ///
    /// - <b>companion</b> (many): plays under the player during the level;
    ///   the mix of the companion tracks <i>is</i> the level audio (the
    ///   beatmap's single audio file);
    /// - <b>reference</b> (one): the full performance; transcription runs on
    ///   it, and the future auto/demo playback plays it with the companion.
    ///
    /// <b>Demux</b> is an action, not a role: <see cref="DemuxPending"/>
    /// queues a track for source separation and the stems join the list.
    ///
    /// A track that has been demuxed, or that came out of a demux, cannot
    /// be demuxed again ("hace perder el tiempo"); it can still take any
    /// other role. A rendered mixdown is mixed material and cannot be
    /// demuxed either.
    /// </summary>
    public class ForgeTrack
    {
        public string Name;

        /// <summary>File path of the track's audio.</summary>
        public string Path;

        /// <summary>Highest gain: +20 dB.</summary>
        public const float MAX_GAIN = 10f;

        /// <summary>Linear gain, 0..<see cref="MAX_GAIN"/> (render and playback).</summary>
        public float Gain = 1.0f;

        /// <summary>Muted tracks are excluded from the rendered mix.</summary>
        public bool Muted;

        /// <summary>
        /// Edits heard live but not yet saved (owner 2026-10-14: hear before
        /// saving). Saving renders them into a NEW track; this one is never
        /// modified on disk.
        /// </summary>
        public TrackEdits Edits = new TrackEdits();

        /// <summary>
        /// Where the file starts on the set's timeline, in milliseconds
        /// (owner 2026-10-14: every track can be moved in time freely; the
        /// set spans max(start + length)).
        ///
        /// What may not start before 0:00 is the part that plays
        /// (<see cref="TimelineStartMs"/>): a track trimmed at its start can
        /// move left until its audible part reaches 0:00, so the file start
        /// may be negative by up to <see cref="ClipStartMs"/>. When copying a
        /// track, set <see cref="ClipStartMs"/> before this.
        /// </summary>
        public double OffsetMs
        {
            get => offsetMs;
            set => offsetMs = Math.Max(-clipStartMs, double.IsFinite(value) ? value : 0);
        }

        /// <summary>Where the part that plays starts on the timeline (never before 0:00).</summary>
        public double TimelineStartMs => OffsetMs + ClipStartMs;

        private double offsetMs;

        /// <summary>
        /// The part of the file that plays, in the file's own time (owner
        /// 2026-10-14: trim a track from its sides, and undo it, by
        /// dragging its edges). The audio never moves: trimming the start
        /// hides what's before it, the track's timing stays the same.
        /// </summary>
        public double ClipStartMs
        {
            get => clipStartMs;
            // A track moved left of 0:00 can't reveal what is before 0:00.
            set => clipStartMs = Math.Max(Math.Max(0, -offsetMs), double.IsFinite(value) ? value : 0);
        }

        /// <summary>End of the part that plays (file time); 0 = to the end of the file.</summary>
        public double ClipEndMs
        {
            get => clipEndMs;
            set => clipEndMs = Math.Max(0, double.IsFinite(value) ? value : 0);
        }

        private double clipStartMs;
        private double clipEndMs;

        /// <summary>Whether the track is trimmed from either side.</summary>
        public bool IsClipped => ClipStartMs > 0 || ClipEndMs > 0;

        /// <summary>Whether file time <paramref name="fileMs"/> is inside the part that plays.</summary>
        public bool InClip(double fileMs) => fileMs >= ClipStartMs && (ClipEndMs <= 0 || fileMs < ClipEndMs);

        /// <summary>End of the part that plays, given the file's length.</summary>
        public double ClipEndOr(double fileLengthMs) => ClipEndMs > 0 ? Math.Min(ClipEndMs, fileLengthMs) : fileLengthMs;

        /// <summary>
        /// Everything that shapes what this track sounds like, as one edit
        /// chain: the pending edits plus the side trims (one more trim).
        /// </summary>
        public TrackEdits EffectiveEdits()
        {
            var edits = Edits.Clone();

            if (IsClipped)
                edits.Ranges.Add(new PendingRange(Native.KanForgeEditMode.Trim, ClipStartMs, ClipEndMs > 0 ? ClipEndMs : TO_THE_END_MS));

            return edits;
        }

        /// <summary>Whether the track plays differently from its file (edits, filters or side trims).</summary>
        public bool IsShaped => IsClipped || !Edits.IsEmpty;

        /// <summary>Native ranges are clamped to the file: "to the end" as a finite bound (24 h).</summary>
        public const float TO_THE_END_MS = 86_400_000f;

        /// <summary>The reference take (exactly one per track set).</summary>
        public bool IsReference;

        /// <summary>Plays under the player: part of the level audio (the companion mix).</summary>
        public bool IsCompanion;

        /// <summary>Queued for source separation (the demux action).</summary>
        public bool DemuxPending;

        /// <summary>Whether the track has a role (used for selection highlighting).</summary>
        public bool HasAnyRole => IsReference || IsCompanion;

        /// <summary>
        /// Already demuxed, or produced by a demux: never demuxable again.
        /// </summary>
        public bool IsSeparated;

        /// <summary>Product of a render/mixdown: mixed material, never demuxable.</summary>
        public bool IsMixdown;

        // ---- content analysis (waveform preview + meaningful-audio marking)

        /// <summary>Waveform preview buckets (0..1), null until analysed.</summary>
        public float[]? Peaks;

        /// <summary>
        /// The analysis could not decode the file: the track stays unjudged
        /// (no "no meaningful audio" flag) and the UI says so instead of
        /// showing "analysing" forever.
        /// </summary>
        public bool AnalysisFailed;

        public float PeakLevel;

        public float RmsLevel;

        /// <summary>
        /// The analysis found no meaningful audio: the track is labelled and
        /// moved to the end of the list (it stays selectable, the user may
        /// know better).
        /// </summary>
        public bool LikelyEmpty;

        /// <summary>Whether this track may still be scheduled for demux.</summary>
        public bool CanDemux => !IsSeparated && !IsMixdown;

        public ForgeTrack(string name, string path)
        {
            Name = name;
            Path = path;
        }

        /// <summary>
        /// Start position of a track produced by a range edit of this one, so
        /// the result stays where its audio was: keep/silence preserve the
        /// file's timing (same start); trim cuts the file down to the range,
        /// so it starts where the range started.
        /// </summary>
        public double OffsetAfterEdit(Native.KanForgeEditMode mode, double rangeStartMs)
            => mode == Native.KanForgeEditMode.Trim ? OffsetMs + Math.Max(0, rangeStartMs) : OffsetMs;

        /// <summary>
        /// The naming rule for rendered mixdowns (our renders are written as
        /// <c>audio-render*.wav</c>); imported files that look like one are
        /// treated as mixed material too.
        /// </summary>
        public static bool LooksLikeMixdown(string pathOrName)
            => System.IO.Path.GetFileNameWithoutExtension(pathOrName)
                   .StartsWith(@"audio-render", System.StringComparison.OrdinalIgnoreCase);

        public ForgeTrack Clone() => new ForgeTrack(Name, Path)
        {
            Gain = Gain,
            Muted = Muted,
            ClipStartMs = ClipStartMs, // before OffsetMs: it bounds it
            OffsetMs = OffsetMs,
            ClipEndMs = ClipEndMs,
            Edits = Edits.Clone(),
            IsReference = IsReference,
            IsCompanion = IsCompanion,
            DemuxPending = DemuxPending,
            IsSeparated = IsSeparated,
            IsMixdown = IsMixdown,
        };
    }

    /// <summary>
    /// The editor's unified track list. The level audio is the mix of the
    /// *companion* tracks; transcription targets the *reference*; separation
    /// consumes the *demux* queue and its results rejoin the list.
    /// </summary>
    public class ForgeTrackSet
    {
        /// <summary>
        /// The tracks, in the order shown. Changed only on the game's update
        /// thread: background jobs (demux, transcription, render) hand their
        /// results to it (the UI reads the set every frame).
        /// </summary>
        public List<ForgeTrack> Tracks { get; } = new();

        /// <summary>
        /// The level audio's window on the set's timeline (owner 2026-10-14:
        /// a final start/end for the companion mix). The render keeps only
        /// [start, end); 0 end = to the end of the mix.
        /// </summary>
        public double AudioStartMs
        {
            get => audioStartMs;
            set => audioStartMs = Math.Max(0, double.IsFinite(value) ? value : 0);
        }

        public double AudioEndMs
        {
            get => audioEndMs;
            set => audioEndMs = Math.Max(0, double.IsFinite(value) ? value : 0);
        }

        private double audioStartMs;
        private double audioEndMs;

        public bool HasAudioWindow => AudioStartMs > 0 || AudioEndMs > 0;

        /// <summary>
        /// How far (dB) the level audio is rendered from the game's reference
        /// loudness (<see cref="LevelLoudness"/>): 0, the default, is the
        /// reference; louder or softer is the creator's choice.
        /// </summary>
        public double LoudnessOffsetDb
        {
            get => loudnessOffsetDb;
            set => loudnessOffsetDb = LevelLoudness.ClampOffset(value);
        }

        private double loudnessOffsetDb;

        /// <summary>The reference take (exactly one), if chosen.</summary>
        public ForgeTrack? Reference => Tracks.FirstOrDefault(t => t.IsReference);

        /// <summary>The audible companion tracks: what the level audio mixes.</summary>
        public IEnumerable<ForgeTrack> Companions => Tracks.Where(t => t.IsCompanion && !t.Muted);

        /// <summary>The tracks queued for source separation.</summary>
        public IEnumerable<ForgeTrack> DemuxQueue => Tracks.Where(t => t.DemuxPending && t.CanDemux);

        public void Add(ForgeTrack track) => Tracks.Add(track);

        public void Remove(ForgeTrack track) => Tracks.Remove(track);

        /// <summary>True when a track from this file path is already in the set.</summary>
        public bool ContainsPath(string path)
            => Tracks.Any(t => string.Equals(t.Path, path, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Import any audio the user has (reference take, stems, extras…).
        /// Returns <see langword="null"/> when the same file is already in
        /// the set: a track is never added twice.
        /// </summary>
        public ForgeTrack? Import(string name, string path)
        {
            if (ContainsPath(path))
                return null;

            var track = new ForgeTrack(name, path);
            Tracks.Add(track);
            return track;
        }

        /// <summary>Promote one track to reference (exactly one at a time).</summary>
        public void SetReference(ForgeTrack track)
        {
            foreach (var t in Tracks)
                t.IsReference = false;
            track.IsReference = true;
        }

        /// <summary>Clear the reference role (e.g. before removing the track).</summary>
        public void ClearReference()
        {
            foreach (var t in Tracks)
                t.IsReference = false;
        }

        /// <summary>
        /// Meaningful tracks first, "no meaningful audio" tracks at the end
        /// (owner requirement 2026-10-13: don't mislead the user). Stable:
        /// relative order inside each group is preserved.
        /// </summary>
        public void OrderByMeaning()
        {
            var meaningful = Tracks.Where(t => !t.LikelyEmpty).ToList();
            var empty = Tracks.Where(t => t.LikelyEmpty).ToList();
            Tracks.Clear();
            Tracks.AddRange(meaningful);
            Tracks.AddRange(empty);
        }
    }
}
