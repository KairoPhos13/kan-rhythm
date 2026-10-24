// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Overlays;
using osuTK;
using osuTK.Graphics;
using Kan.Forge.Native;
using Kan.Forge.Tracks;

namespace Kan.Forge.Editor
{
    /// <summary>A pending range edit, removable with its ✕.</summary>
    internal partial class PendingChip : CompositeDrawable
    {
        private readonly string text;
        private readonly Action onRemove;

        public PendingChip(string text, Action onRemove)
        {
            this.text = text;
            this.onRemove = onRemove;
            AutoSizeAxes = Axes.X;
            Height = 24;
        }

        [BackgroundDependencyLoader]
        private void load(OverlayColourProvider colourProvider)
        {
            Masking = true;
            CornerRadius = 12;

            InternalChildren = new Drawable[]
            {
                new Box { RelativeSizeAxes = Axes.Both, Colour = colourProvider.Background3 },
                new FillFlowContainer
                {
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Horizontal,
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    Padding = new MarginPadding { Left = 10, Right = 2 },
                    Spacing = new Vector2(2, 0),
                    Children = new Drawable[]
                    {
                        new OsuSpriteText
                        {
                            Text = text,
                            Font = OsuFont.GetFont(size: 12, weight: FontWeight.SemiBold),
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                        },
                        new IconButton
                        {
                            Icon = FontAwesome.Solid.Times,
                            Size = new Vector2(20),
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            TooltipText = @"remove this edit",
                            Action = onRemove,
                        },
                    },
                },
            };
        }
    }
}
