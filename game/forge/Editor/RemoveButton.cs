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
    /// ✕ that asks once: the first click arms it ("remove?", red), a second
    /// click within a few seconds removes. Removing only takes the track out
    /// of the set; no file is deleted.
    /// </summary>
    internal partial class RemoveButton : OsuClickableContainer
    {
        private readonly Action onConfirm;

        private Box background = null!;
        private SpriteIcon cross = null!;
        private OsuSpriteText label = null!;
        private ScheduledDelegate? disarm;
        private bool armed;

        [Resolved]
        private OsuColour colours { get; set; } = null!;

        [Resolved]
        private OverlayColourProvider colourProvider { get; set; } = null!;

        public override LocalisableString TooltipText
        {
            get => armed ? @"click again to remove (the file itself is kept)" : @"remove from the set";
            set { }
        }

        public RemoveButton(Action onConfirm)
        {
            this.onConfirm = onConfirm;
            Size = new Vector2(26);
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            Masking = true;
            CornerRadius = 13;

            Children = new Drawable[]
            {
                background = new Box { RelativeSizeAxes = Axes.Both, Colour = colourProvider.Background3, Alpha = 0 },
                label = new OsuSpriteText
                {
                    Text = @"remove?",
                    Font = OsuFont.GetFont(size: 12, weight: FontWeight.Bold),
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    Margin = new MarginPadding { Left = 10 },
                    Alpha = 0,
                },
                cross = new SpriteIcon
                {
                    Icon = FontAwesome.Solid.Times,
                    Size = new Vector2(11),
                    Anchor = Anchor.CentreRight,
                    Origin = Anchor.Centre,
                    X = -13,
                    Colour = colourProvider.Content2,
                },
            };

            Action = click;
        }

        protected override bool OnHover(HoverEvent e)
        {
            if (!armed)
            {
                background.FadeTo(1, 100);
                cross.FadeColour(colours.Red1, 100);
            }

            return base.OnHover(e);
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            if (!armed)
            {
                background.FadeTo(0, 200);
                cross.FadeColour(colourProvider.Content2, 200);
            }

            base.OnHoverLost(e);
        }

        private void click()
        {
            if (armed)
            {
                disarm?.Cancel();
                onConfirm();
                return;
            }

            armed = true;
            background.FadeColour(colours.Red3, 100).FadeTo(1, 100);
            cross.FadeColour(Color4.White, 100);
            label.FadeIn(150);
            this.ResizeWidthTo(84, 200, Easing.OutQuint);

            disarm?.Cancel();
            disarm = Scheduler.AddDelayed(() =>
            {
                armed = false;
                background.FadeColour(colourProvider.Background3, 200).FadeTo(IsHovered ? 1 : 0, 200);
                cross.FadeColour(IsHovered ? colours.Red1 : colourProvider.Content2, 200);
                label.FadeOut(100);
                this.ResizeWidthTo(26, 200, Easing.OutQuint);
            }, 3000);
        }
    }
}
