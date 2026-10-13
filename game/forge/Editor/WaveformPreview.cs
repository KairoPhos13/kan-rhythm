// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using Kan.Forge.Tracks;

namespace Kan.Forge.Editor
{
    /// <summary>
    /// Waveform preview of a track: one bar per analysis bucket — activity is
    /// visible before selecting anything (owner 2026-10-13). Used both as a
    /// compact strip (track rows) and stretched as a row background.
    /// </summary>
    internal partial class WaveformPreview : CompositeDrawable
    {
        public WaveformPreview(ForgeTrack track)
        {
            Height = 32;

            var peaks = track.Peaks;

            if (peaks == null)
            {
                AddInternal(new OsuSpriteText
                {
                    Text = @"analysing…",
                    Font = OsuFont.GetFont(size: 9),
                    Alpha = 0.6f,
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                });
                return;
            }

            Colour4 colour = track.LikelyEmpty
                ? new Colour4(0.6f, 0.6f, 0.6f, 0.35f)
                : new Colour4(1, 1, 1, 0.75f);

            for (int i = 0; i < peaks.Length; i++)
            {
                AddInternal(new Box
                {
                    RelativeSizeAxes = Axes.X,
                    RelativePositionAxes = Axes.Both,
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    X = i / (float)peaks.Length,
                    Width = 0.85f / peaks.Length,
                    Y = 0,
                    Height = MathF.Max(1.5f, peaks[i] * 30),
                    Colour = colour,
                });
            }
        }
    }
}
