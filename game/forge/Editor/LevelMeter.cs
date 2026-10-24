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
    /// <summary>Live output level (peak with a falling hold) — of one track, or any source (master).</summary>
    internal partial class LevelMeter : CompositeDrawable, osu.Framework.Graphics.Cursor.IHasTooltip
    {
        private readonly ForgeTrack? track;
        private readonly Func<float>? source;
        private Box fill = null!;
        private float level;

        [Resolved]
        private SetMonitor monitor { get; set; } = null!;

        [Resolved]
        private OsuColour colours { get; set; } = null!;

        public osu.Framework.Localisation.LocalisableString TooltipText => @"output level while playing";

        public LevelMeter(ForgeTrack track)
        {
            this.track = track;
            Size = new Vector2(6, 22);
        }

        public LevelMeter(Func<float> source)
        {
            this.source = source;
            Size = new Vector2(6, 22);
        }

        [BackgroundDependencyLoader]
        private void load(OverlayColourProvider colourProvider)
        {
            Masking = true;
            CornerRadius = 2;

            InternalChildren = new Drawable[]
            {
                new Box { RelativeSizeAxes = Axes.Both, Colour = colourProvider.Background6 },
                fill = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Anchor = Anchor.BottomLeft,
                    Origin = Anchor.BottomLeft,
                    Height = 0,
                },
            };
        }

        protected override void Update()
        {
            base.Update();

            float target = Math.Clamp(source?.Invoke() ?? (track != null ? monitor.LevelOf(track) : 0), 0, 1);
            float decay = (float)(Time.Elapsed / 600);
            level = target > level ? target : Math.Max(target, level - decay);

            fill.Height = level;
            fill.Colour = level > 0.95f ? colours.Red1 : level > 0.7f ? colours.Yellow : colours.GreenLight;
        }
    }
}
