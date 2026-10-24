// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Overlays;
using osuTK;
using osuTK.Graphics;
using Kan.Forge.Tracks;
using Kan.Core.Tracks;

namespace Kan.Forge.Editor
{
    /// <summary>
    /// Choose the tracks an action works on, right inside the action (owner
    /// 2026-10-14): a checklist (demux) or a pick-one list (transcribe).
    /// Writes the same model flags as the role chips, so both stay in sync.
    /// </summary>
    internal partial class TrackPickList : CompositeDrawable
    {
        private readonly bool single;
        private readonly Color4 accent;
        private readonly Func<ForgeTrack, bool> isOn;
        private readonly Func<ForgeTrack, string?> unavailable;
        private readonly Action<ForgeTrack> toggle;

        private FillFlowContainer flow = null!;
        private string? drawnKey;

        /// <param name="single">Pick one (radio) instead of a checklist.</param>
        /// <param name="accent">The action's colour.</param>
        /// <param name="isOn">Whether a track is picked.</param>
        /// <param name="unavailable">Why a track can't be picked (null = it can).</param>
        /// <param name="toggle">Pick / unpick a track.</param>
        public TrackPickList(bool single, Color4 accent, Func<ForgeTrack, bool> isOn, Func<ForgeTrack, string?> unavailable, Action<ForgeTrack> toggle)
        {
            this.single = single;
            this.accent = accent;
            this.isOn = isOn;
            this.unavailable = unavailable;
            this.toggle = toggle;

            RelativeSizeAxes = Axes.X;
            AutoSizeAxes = Axes.Y;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            InternalChild = flow = new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, 2),
            };
        }

        private IReadOnlyList<ForgeTrack>? pendingTracks;

        protected override void LoadComplete()
        {
            base.LoadComplete();

            if (pendingTracks != null)
                SetTracks(pendingTracks);
        }

        /// <summary>Show these tracks (cheap to call every poll: rebuilds only when something changed).</summary>
        public void SetTracks(IReadOnlyList<ForgeTrack> tracks)
        {
            // Callers may fill the list before it is loaded (the screen's own load).
            if (!IsLoaded)
            {
                pendingTracks = tracks;
                return;
            }

            pendingTracks = null;

            string key = string.Join('|', tracks.Select(t => $"{System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(t)}:{t.Name}:{isOn(t)}:{unavailable(t)}"));
            if (key == drawnKey) return;

            drawnKey = key;
            flow.Clear();

            if (tracks.Count == 0)
            {
                flow.Add(new OsuSpriteText
                {
                    Text = @"no tracks yet — import audio in Resources",
                    Font = OsuFont.Style.Caption2,
                    Alpha = 0.6f,
                });
                return;
            }

            foreach (var track in tracks)
                flow.Add(new Line(track, single, accent, isOn(track), unavailable(track), () => toggle(track)));
        }

        private partial class Line : OsuClickableContainer
        {
            private readonly ForgeTrack track;
            private readonly bool single;
            private readonly Color4 accent;
            private readonly bool on;
            private readonly string? reason;

            private Box hover = null!;

            public Line(ForgeTrack track, bool single, Color4 accent, bool on, string? reason, Action pick)
            {
                this.track = track;
                this.single = single;
                this.accent = accent;
                this.on = on;
                this.reason = reason;

                RelativeSizeAxes = Axes.X;
                Height = 24;
                Action = pick;
                Enabled.Value = reason == null;
            }

            public override LocalisableString TooltipText
            {
                get => reason ?? (on ? @"click to unpick" : @"click to pick");
                set { }
            }

            [BackgroundDependencyLoader]
            private void load(OverlayColourProvider colourProvider)
            {
                Masking = true;
                CornerRadius = 4;

                IconUsage icon = single
                    ? on ? FontAwesome.Solid.DotCircle : FontAwesome.Regular.Circle
                    : on ? FontAwesome.Solid.CheckSquare : FontAwesome.Regular.Square;

                Children = new Drawable[]
                {
                    hover = new Box { RelativeSizeAxes = Axes.Both, Colour = accent, Alpha = on ? 0.12f : 0 },
                    new SpriteIcon
                    {
                        Icon = icon,
                        Size = new Vector2(12),
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        X = 6,
                        Colour = on ? accent : colourProvider.Content2,
                    },
                    new TruncatingSpriteText
                    {
                        Text = reason == null ? track.Name : $"{track.Name} — {reason}",
                        Font = OsuFont.GetFont(size: 13, weight: on ? FontWeight.Bold : FontWeight.Regular),
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        X = 26,
                        RelativeSizeAxes = Axes.X,
                        Width = 0.92f,
                        Alpha = reason == null ? 1 : 0.45f,
                    },
                };
            }

            protected override bool OnHover(HoverEvent e)
            {
                if (reason == null) hover.FadeTo(on ? 0.2f : 0.08f, 100);
                return base.OnHover(e);
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                hover.FadeTo(on ? 0.12f : 0, 200);
                base.OnHoverLost(e);
            }
        }
    }
}
