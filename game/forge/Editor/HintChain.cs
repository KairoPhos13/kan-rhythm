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
    /// <summary>
    /// First-run guidance: a short chain of icon steps ("import → assign
    /// roles → optional actions") instead of a bare sentence.
    /// </summary>
    internal partial class HintChain : CompositeDrawable
    {
        private readonly (IconUsage icon, string text)[] steps;

        public HintChain(params (IconUsage icon, string text)[] steps)
        {
            this.steps = steps;
            RelativeSizeAxes = Axes.X;
            AutoSizeAxes = Axes.Y;
        }

        [BackgroundDependencyLoader]
        private void load(OverlayColourProvider colourProvider)
        {
            var flow = new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Full,
                Spacing = new Vector2(8, 8),
                Padding = new MarginPadding(14),
            };

            for (int i = 0; i < steps.Length; i++)
            {
                if (i > 0)
                {
                    flow.Add(new SpriteIcon
                    {
                        Icon = FontAwesome.Solid.ChevronRight,
                        Size = new Vector2(9),
                        Colour = colourProvider.Foreground1,
                        Margin = new MarginPadding { Top = 9 },
                    });
                }

                flow.Add(new FillFlowContainer
                {
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Horizontal,
                    Spacing = new Vector2(7, 0),
                    Children = new Drawable[]
                    {
                        new Container
                        {
                            Size = new Vector2(26),
                            Masking = true,
                            CornerRadius = 13,
                            Children = new Drawable[]
                            {
                                new Box { RelativeSizeAxes = Axes.Both, Colour = colourProvider.Background3 },
                                new SpriteIcon
                                {
                                    Icon = steps[i].icon,
                                    Size = new Vector2(11),
                                    Anchor = Anchor.Centre,
                                    Origin = Anchor.Centre,
                                    Colour = colourProvider.Highlight1,
                                },
                            },
                        },
                        new OsuSpriteText
                        {
                            Text = steps[i].text,
                            Font = OsuFont.Style.Caption1,
                            Colour = colourProvider.Content2,
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                        },
                    },
                });
            }

            InternalChild = new Container
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Masking = true,
                CornerRadius = 6,
                BorderThickness = 1.5f,
                BorderColour = colourProvider.Background2,
                Children = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both, Colour = colourProvider.Background5.Opacity(0.6f) },
                    flow,
                },
            };
        }
    }
}
