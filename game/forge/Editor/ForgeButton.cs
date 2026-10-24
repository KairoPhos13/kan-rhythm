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
    /// A button in the editor's form vocabulary (same background, hover and
    /// commit flash as the stock Form* controls) with an icon and a label.
    /// </summary>
    internal partial class ForgeButton : OsuClickableContainer
    {
        private readonly IconUsage icon;
        private LocalisableString text;
        private readonly Color4? accent;

        private FormControlBackground background = null!;
        private FillFlowContainer content = null!;
        private OsuSpriteText? label;

        /// <summary>The button's caption (can change after load).</summary>
        public LocalisableString Text
        {
            get => text;
            set
            {
                text = value;
                if (label != null) label.Text = value;
            }
        }

        [Resolved]
        private OverlayColourProvider colourProvider { get; set; } = null!;

        public ForgeButton(IconUsage icon, LocalisableString text, Color4? accent = null)
        {
            this.icon = icon;
            this.text = text;
            this.accent = accent;

            RelativeSizeAxes = Axes.X;
            Height = 34;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            Masking = true;
            CornerRadius = 5;
            CornerExponent = FormControlBackground.CORNER_EXPONENT;

            Children = new Drawable[]
            {
                background = new FormControlBackground(),
                content = new FillFlowContainer
                {
                    AutoSizeAxes = Axes.Both,
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Direction = FillDirection.Horizontal,
                    Spacing = new Vector2(7, 0),
                    Children = new Drawable[]
                    {
                        new SpriteIcon
                        {
                            Icon = icon,
                            Size = new Vector2(13),
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Colour = accent ?? colourProvider.Highlight1,
                        },
                        label = new OsuSpriteText
                        {
                            Text = text,
                            Font = OsuFont.GetFont(size: 14, weight: FontWeight.SemiBold),
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                        },
                    },
                },
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            Enabled.BindValueChanged(_ => updateStyle(), true);
        }

        protected override bool OnHover(HoverEvent e)
        {
            updateStyle();
            return base.OnHover(e);
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            updateStyle();
            base.OnHoverLost(e);
        }

        protected override bool OnClick(ClickEvent e)
        {
            if (Enabled.Value)
                background.FlashOnCommit();

            return base.OnClick(e);
        }

        private void updateStyle()
        {
            content.FadeTo(Enabled.Value ? 1 : 0.4f, 150);

            if (!Enabled.Value)
                background.VisualStyle = VisualStyle.Disabled;
            else if (IsHovered)
                background.VisualStyle = VisualStyle.Hovered;
            else
                background.VisualStyle = VisualStyle.Normal;
        }
    }
}
