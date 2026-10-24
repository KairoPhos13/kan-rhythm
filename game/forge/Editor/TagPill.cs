// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osu.Framework.Threading;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Overlays;
using osu.Game.Overlays.Notifications;
using osu.Game.Utils;
using osuTK;
using osuTK.Graphics;
using Kan.Forge.Tracks;

namespace Kan.Forge.Editor
{
    /// <summary>A small text pill for a track's material and status tags (separated, mixdown, from video, empty?).</summary>
    internal partial class TagPill : CompositeDrawable, osu.Framework.Graphics.Cursor.IHasTooltip
    {
        public LocalisableString TooltipText { get; }

        public TagPill(string text, IconUsage? icon, Color4 colour, string tooltip)
        {
            TooltipText = tooltip;
            AutoSizeAxes = Axes.X;
            Height = 18;
            Masking = true;
            CornerRadius = 4;

            var content = new FillFlowContainer
            {
                AutoSizeAxes = Axes.Both,
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                Direction = FillDirection.Horizontal,
                Spacing = new Vector2(4, 0),
                Padding = new MarginPadding { Horizontal = 6 },
            };

            if (icon != null)
            {
                content.Add(new SpriteIcon
                {
                    Icon = icon.Value,
                    Size = new Vector2(9),
                    Colour = colour,
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                });
            }

            content.Add(new OsuSpriteText
            {
                Text = text,
                Font = OsuFont.GetFont(size: 11, weight: FontWeight.SemiBold),
                Colour = colour,
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
            });

            InternalChildren = new Drawable[]
            {
                new Box { RelativeSizeAxes = Axes.Both, Colour = colour.Opacity(0.14f) },
                content,
            };
        }

        /// <summary>The material/status tags of a track (shared by both surfaces).</summary>
        public static IEnumerable<TagPill> For(ForgeTrack track, OsuColour colours, OverlayColourProvider provider)
        {
            if (!File.Exists(track.Path))
            {
                yield return new TagPill(@"file missing", FontAwesome.Solid.ExclamationTriangle, colours.Red1,
                    $"The file was moved or deleted: {track.Path}. Remove the track, or put the file back.");
            }
            else if (track.AnalysisFailed)
            {
                yield return new TagPill(@"can't read", FontAwesome.Solid.ExclamationTriangle, colours.Orange1,
                    @"This file couldn't be decoded for its waveform. It may be damaged or in an unsupported format.");
            }

            if (track.LikelyEmpty)
            {
                yield return new TagPill(@"no meaningful audio?", FontAwesome.Solid.ExclamationTriangle, colours.Orange1,
                    @"This track seems to contain no meaningful audio — it was moved to the end of the list. Select it only if you are sure.");
            }

            if (track.DemuxPending && track.CanDemux)
            {
                yield return new TagPill(@"queued for demux", FontAwesome.Solid.CodeBranch, ForgeRoles.DemuxColour(colours),
                    @"Picked in the Demux card: it will be separated into stems when you run Demux.");
            }

            if (ForgeTrackSession.IsVideoAudio(track))
                yield return new TagPill(@"from video", FontAwesome.Solid.Film, provider.Content2, @"The audio extracted from the level's video.");

            if (track.IsClipped)
                yield return new TagPill(@"trimmed", FontAwesome.Solid.Cut, provider.Content2, @"Trimmed from its sides — only the part shown plays (drag the sides back out to restore).");

            if (track.IsMixdown)
                yield return new TagPill(@"mixdown", FontAwesome.Solid.Compress, provider.Content2, @"Mixed material (a render) — it cannot be demuxed.");
            else if (track.IsSeparated)
                yield return new TagPill(@"separated", FontAwesome.Solid.CodeBranch, provider.Content2, @"Already separated (or a stem) — it is not demuxed again.");
        }
    }
}
