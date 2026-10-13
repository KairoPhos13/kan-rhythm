// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System.Collections.Generic;
using System.Linq;

namespace Kan.Forge.Tracks
{
    /// <summary>
    /// One track of the editor's unified track list (owner requirements
    /// 2026-10-13): everything the user loads lands in a single list, and
    /// each track gets a **role** there:
    ///
    /// - <b>reference</b> (exactly one) — the original performance; the take
    ///   transcription runs on automatically (and the future Mod Auto plays
    ///   along to it);
    /// - <b>accompaniment</b> (many) — what the beatmap-audio render mixes;
    /// - <b>demux</b> (queue) — tracks scheduled for source separation; the
    ///   resulting stems join the list as new tracks.
    ///
    /// A track that has been demuxed — or that came out of a demux — cannot
    /// be demuxed again ("hace perder el tiempo"); it can still take any
    /// other role. A rendered mixdown is mixed material and cannot be
    /// demuxed either.
    /// </summary>
    public class ForgeTrack
    {
        public string Name;

        /// <summary>File path of the track's audio.</summary>
        public string Path;

        /// <summary>Linear gain, 0..2 (render and playback).</summary>
        public float Gain = 1.0f;

        /// <summary>Muted tracks are excluded from the rendered mix.</summary>
        public bool Muted;

        /// <summary>The reference take (exactly one per track set).</summary>
        public bool IsReference;

        /// <summary>Selected for the beatmap-audio render (the mixdown).</summary>
        public bool IsAccompaniment;

        /// <summary>Queued for source separation (demux).</summary>
        public bool DemuxPending;

        /// <summary>
        /// Already demuxed, or produced by a demux — never demuxable again.
        /// </summary>
        public bool IsSeparated;

        /// <summary>Product of a render/mixdown — mixed material, never demuxable.</summary>
        public bool IsMixdown;

        // ---- content analysis (waveform preview + meaningful-audio marking)

        /// <summary>Waveform preview buckets (0..1), null until analysed.</summary>
        public float[]? Peaks;

        public float PeakLevel;

        public float RmsLevel;

        /// <summary>
        /// The analysis found no meaningful audio: the track is labelled and
        /// moved to the end of the list (it stays selectable — the user may
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
            IsReference = IsReference,
            IsAccompaniment = IsAccompaniment,
            DemuxPending = DemuxPending,
            IsSeparated = IsSeparated,
            IsMixdown = IsMixdown,
        };
    }

    /// <summary>
    /// The editor's unified track list. Rendering mixes the *accompaniment*
    /// selection; transcription targets the *reference*; separation consumes
    /// the *demux* queue and its results rejoin the list.
    /// </summary>
    public class ForgeTrackSet
    {
        public List<ForgeTrack> Tracks { get; } = new();

        /// <summary>The reference take (exactly one), if chosen.</summary>
        public ForgeTrack? Reference => Tracks.FirstOrDefault(t => t.IsReference);

        /// <summary>The tracks selected for the beatmap-audio mixdown.</summary>
        public IEnumerable<ForgeTrack> Accompaniments => Tracks.Where(t => t.IsAccompaniment && !t.Muted);

        /// <summary>The tracks queued for source separation.</summary>
        public IEnumerable<ForgeTrack> DemuxQueue => Tracks.Where(t => t.DemuxPending && t.CanDemux);

        public void Add(ForgeTrack track) => Tracks.Add(track);

        public void Remove(ForgeTrack track) => Tracks.Remove(track);

        /// <summary>Import any audio the user has (reference take, stems, extras…).</summary>
        public ForgeTrack Import(string name, string path)
        {
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
