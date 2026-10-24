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
    /// A role chip: icon + label in the role's colour. Filled when the role
    /// is active, outlined when not, faded when unavailable. The long role
    /// explanations live in the tooltip, not on screen.
    /// </summary>
    internal partial class RoleChip : OsuClickableContainer
    {
        public readonly ForgeRole Role;

        private readonly bool compact;

        private Box background = null!;
        private SpriteIcon icon = null!;
        private OsuSpriteText label = null!;

        private Color4 roleColour;
        private bool active;
        private bool available = true;
        private string? unavailableReason;

        [Resolved]
        private OsuColour colours { get; set; } = null!;

        [Resolved]
        private OverlayColourProvider colourProvider { get; set; } = null!;

        /// <param name="role">The role shown.</param>
        /// <param name="compact">Small read-only badge (explorer rows, legends).</param>
        public RoleChip(ForgeRole role, bool compact = false)
        {
            Role = role;
            this.compact = compact;

            // Before load: OsuClickableContainer sizes its content from
            // AutoSizeAxes in its own load, which runs before ours.
            AutoSizeAxes = Axes.X;
            Height = compact ? 18 : 26;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            roleColour = ForgeRoles.Colour(colours, Role);

            Masking = true;
            CornerRadius = Height / 2;
            BorderThickness = compact ? 0 : 1.5f;

            Children = new Drawable[]
            {
                background = new Box { RelativeSizeAxes = Axes.Both },
                new FillFlowContainer
                {
                    AutoSizeAxes = Axes.Both,
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    Direction = FillDirection.Horizontal,
                    Spacing = new Vector2(compact ? 4 : 6, 0),
                    Padding = new MarginPadding { Horizontal = compact ? 7 : 11 },
                    Children = new Drawable[]
                    {
                        icon = new SpriteIcon
                        {
                            Icon = ForgeRoles.Icon(Role),
                            Size = new Vector2(compact ? 9 : 11),
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                        },
                        label = new OsuSpriteText
                        {
                            Text = ForgeRoles.Label(Role),
                            Font = OsuFont.GetFont(size: compact ? 11 : 13, weight: FontWeight.SemiBold),
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                        },
                    },
                },
            };

            if (compact)
                Enabled.Value = false;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            updateLook(false);
            FinishTransforms(true);
        }

        public override LocalisableString TooltipText
        {
            get => available || unavailableReason == null ? ForgeRoles.Description(Role) : unavailableReason;
            set { }
        }

        /// <summary>Reflect the model: active (filled) and available (or faded with a reason).</summary>
        public void SetState(bool isActive, bool isAvailable = true, string? reason = null)
        {
            bool changed = isActive != active;

            active = isActive;
            available = isAvailable;
            unavailableReason = reason;

            if (LoadState >= LoadState.Ready)
                updateLook(changed);
        }

        protected override bool OnHover(HoverEvent e)
        {
            updateLook(false);
            return base.OnHover(e);
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            updateLook(false);
            base.OnHoverLost(e);
        }

        private void updateLook(bool pulse)
        {
            bool hovered = IsHovered && !compact && available;

            Color4 fill = active ? roleColour : roleColour.Opacity(hovered ? 0.28f : 0.1f);
            Color4 ink = active ? colourProvider.Background6 : roleColour;

            background.FadeColour(fill, 150, Easing.OutQuint);
            icon.FadeColour(ink, 150, Easing.OutQuint);
            label.FadeColour(ink, 150, Easing.OutQuint);
            this.TransformTo(nameof(BorderColour), (osu.Framework.Graphics.Colour.ColourInfo)roleColour.Opacity(active ? 1 : 0.55f), 150, Easing.OutQuint);
            this.FadeTo(available ? 1 : 0.35f, 150);

            if (pulse && active)
                this.ScaleTo(1.08f, 80, Easing.OutQuint).Then().ScaleTo(1, 250, Easing.OutElasticHalf);
        }
    }
}
