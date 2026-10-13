// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System.Collections.Generic;
using System.Linq;

namespace Kan.Forge.Tracks
{
    /// <summary>
    /// One editor track: an audio source (a generated stem, the source audio,
    /// or an individual track the user imported) with its playback/render
    /// settings — owner requirement 2026-10-13: volume per track and the
    /// ability to add individual tracks when the user has them.
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

        public ForgeTrack(string name, string path)
        {
            Name = name;
            Path = path;
        }

        public ForgeTrack Clone() => new ForgeTrack(Name, Path)
        {
            Gain = Gain,
            Muted = Muted,
        };
    }

    /// <summary>
    /// The editor's track set (the timeline slice builds the full NLE on top
    /// of this). Rendering: <c>ForgeJobs.RenderMixAsync</c> mixes the audible
    /// tracks into the single playable track.
    /// </summary>
    public class ForgeTrackSet
    {
        public List<ForgeTrack> Tracks { get; } = new();

        /// <summary>The tracks that take part in the render (not muted).</summary>
        public IEnumerable<ForgeTrack> Audible => Tracks.Where(t => !t.Muted);

        public void Add(ForgeTrack track) => Tracks.Add(track);

        public void Remove(ForgeTrack track) => Tracks.Remove(track);

        /// <summary>Import an individual track (any audio the user has).</summary>
        public ForgeTrack Import(string name, string path)
        {
            var track = new ForgeTrack(name, path);
            Tracks.Add(track);
            return track;
        }
    }
}
