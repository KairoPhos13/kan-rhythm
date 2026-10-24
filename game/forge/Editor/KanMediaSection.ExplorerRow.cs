// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Development;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Extensions;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osu.Game.Beatmaps;
using osu.Game.Extensions;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Overlays;
using osu.Game.Screens.Edit.Setup;
using osu.Game.Utils;
using Kan.Forge.Media;
using Kan.Forge.Tracks;
using osuTK.Graphics;
using Kan.Core.Tracks;

namespace Kan.Forge.Editor
{
    /// <summary>
    /// One track in the Resources section's track explorer.
    /// </summary>
    public partial class KanMediaSection : ResourcesSection
    {
        /// <summary>Compact row: waveform background + name + role chips + tags + remove.</summary>
        private partial class ExplorerRow : CompositeDrawable
        {
            public readonly ForgeTrack Track;

            private readonly Action<ForgeTrack> onRemove;

            private Box flashLayer = null!;
            private TruncatingSpriteText nameText = null!;
            private FillFlowContainer details = null!;
            private RemoveButton remove = null!;

            [Resolved]
            private OsuColour colours { get; set; } = null!;

            [Resolved]
            private OverlayColourProvider colourProvider { get; set; } = null!;

            public ExplorerRow(ForgeTrack track, Action<ForgeTrack> onRemove)
            {
                Track = track;
                this.onRemove = onRemove;

                RelativeSizeAxes = Axes.X;
                Height = 48;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                var primary = ForgeRoles.Primary(Track);
                Color4 highlight = primary != null ? ForgeRoles.Colour(colours, primary.Value) : colourProvider.Background4;
                Color4 background = colourProvider.Background4.Darken(0.1f);

                details = new FillFlowContainer
                {
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Horizontal,
                    Spacing = new osuTK.Vector2(4, 0),
                };

                foreach (var role in ForgeRoles.ALL)
                {
                    if (!ForgeRoles.Has(Track, role)) continue;

                    var chip = new RoleChip(role, compact: true);
                    chip.SetState(true);
                    details.Add(chip);
                }

                details.AddRange(TagPill.For(Track, colours, colourProvider));

                if (TrackDurations.Get(Track.Path) is double seconds)
                {
                    details.Add(new OsuSpriteText
                    {
                        Text = ForgeFormat.Time(seconds),
                        Font = OsuFont.GetFont(size: 11, weight: FontWeight.SemiBold),
                        Colour = colourProvider.Foreground1,
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                    });
                }

                InternalChild = new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Masking = true,
                    CornerRadius = 5,
                    CornerExponent = FormControlBackground.CORNER_EXPONENT,
                    BorderThickness = primary != null ? 2 : 0,
                    BorderColour = highlight,
                    Children = new Drawable[]
                    {
                        new Box { RelativeSizeAxes = Axes.Both, Colour = background },
                        new Container
                        {
                            RelativeSizeAxes = Axes.Both,
                            Padding = new MarginPadding { Horizontal = 6 },
                            Alpha = Track.Muted ? 0.25f : 0.55f,
                            Child = new WaveformPreview(Track)
                            {
                                RelativeSizeAxes = Axes.Both,
                                AccentColour = WaveformPreview.ColourFor(Track, colours, colourProvider),
                            },
                        },
                        // Legibility: the text side fades the waveform out.
                        new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Width = 0.7f,
                            Colour = ColourInfo.GradientHorizontal(background.Opacity(0.9f), background.Opacity(0)),
                        },
                        new Box { RelativeSizeAxes = Axes.Y, Width = 4, Colour = highlight, Alpha = primary != null ? 1 : 0 },
                        flashLayer = new Box { RelativeSizeAxes = Axes.Both, Colour = colourProvider.Highlight1, Alpha = 0 },
                        new FillFlowContainer
                        {
                            AutoSizeAxes = Axes.Both,
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Direction = FillDirection.Vertical,
                            Spacing = new osuTK.Vector2(0, 3),
                            Padding = new MarginPadding { Left = 12 },
                            Children = new Drawable[]
                            {
                                nameText = new TruncatingSpriteText
                                {
                                    Text = Track.Name,
                                    Font = OsuFont.GetFont(size: 14, weight: FontWeight.Bold),
                                    Alpha = Track.LikelyEmpty ? 0.6f : 1,
                                },
                                details,
                            },
                        },
                        remove = new RemoveButton(() => onRemove(Track))
                        {
                            Anchor = Anchor.CentreRight,
                            Origin = Anchor.CentreRight,
                            Margin = new MarginPadding { Right = 8 },
                        },
                    },
                };
            }

            protected override void Update()
            {
                base.Update();
                nameText.MaxWidth = Math.Max(60, DrawWidth - 12 - remove.DrawWidth - 24);
            }

            /// <summary>Draw the eye to this row (just added, or a refused duplicate of it).</summary>
            public void Pulse()
            {
                if (LoadState < LoadState.Ready)
                {
                    Schedule(Pulse);
                    return;
                }

                flashLayer.FadeTo(0.35f).FadeOut(1200, Easing.OutQuint);
            }
        }
    }
}
