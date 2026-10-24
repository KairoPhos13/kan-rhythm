// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Overlays;
using osu.Game.Screens.Edit;
using osuTK;
using osuTK.Graphics;
using Kan.Forge.Tracks;
using Kan.Core.Tracks;

namespace Kan.Forge.Editor
{
    /// <summary>
    /// A start/end window with handles, "start/end here" (at the playhead)
    /// and "whole" (owner 2026-10-14). Used for the level audio's final
    /// start/end (<see cref="ForLevelAudio"/>) and for each track's side
    /// trims (<see cref="ForTrack"/>) — the same control everywhere.
    /// </summary>
    internal partial class WindowBar : CompositeDrawable, IHasTooltip
    {
        /// <summary>The level audio's final start and end (set timeline).</summary>
        public static WindowBar ForLevelAudio(ForgeTrackSet set, SetMonitor monitor, Action onChanged) => new WindowBar(
            @"Level audio start and end", @"whole mix", @"final length",
            () => monitor.SpanMs,
            () => set.AudioStartMs, () => set.AudioEndMs,
            (s, e) =>
            {
                set.AudioStartMs = s;
                set.AudioEndMs = e;
            },
            t => t, onChanged);

        /// <summary>A track's side trims (file time; the playhead maps through the track's start).</summary>
        public static WindowBar ForTrack(ForgeTrack track, SetMonitor monitor, Action onChanged) => new WindowBar(
            @"Start and end of this track", @"whole track", @"plays for",
            () => monitor.LengthOf(track),
            () => track.ClipStartMs, () => track.ClipEndMs,
            (s, e) =>
            {
                track.ClipStartMs = s;
                track.ClipEndMs = e;
            },
            t => t - track.OffsetMs, onChanged);

        private readonly string title;
        private readonly string wholeLabel;
        private readonly string lengthLabel;
        private readonly Func<double> getSpan;
        private readonly Func<double> getStart;
        private readonly Func<double> getEnd;
        private readonly Action<double, double> setWindow;
        private readonly Func<double, double> clockToLocal;

        private double start => getStart();

        private void setStart(double v) => setWindow(v, getEnd());

        private void setEnd(double v) => setWindow(getStart(), v);

        private bool hasWindow => getStart() > 0 || getEnd() > 0;
        private const float grab_px = 10;
        private const double snap_ms = 10;
        private const double min_song_ms = 1000;

        private readonly Action onChanged;

        [Resolved]
        private SetMonitor monitor { get; set; } = null!;

        [Resolved]
        private EditorClock? clock { get; set; }

        [Resolved]
        private OsuColour colours { get; set; } = null!;

        [Resolved]
        private OverlayColourProvider colourProvider { get; set; } = null!;

        private Container bar = null!;
        private Box window = null!;
        private Box shadeBefore = null!, shadeAfter = null!;
        private Box startHandle = null!, endHandle = null!;
        private Box playhead = null!;
        private OsuSpriteText summary = null!;

        private enum Grab
        {
            None,
            Start,
            End,
        }

        private Grab grab;
        private double grabStart;

        private WindowBar(string title, string wholeLabel, string lengthLabel, Func<double> span, Func<double> start, Func<double> end,
                          Action<double, double> setWindow, Func<double, double> clockToLocal, Action onChanged)
        {
            this.title = title;
            this.wholeLabel = wholeLabel;
            this.lengthLabel = lengthLabel;
            getSpan = span;
            getStart = start;
            getEnd = end;
            this.setWindow = setWindow;
            this.clockToLocal = clockToLocal;
            this.onChanged = onChanged;

            RelativeSizeAxes = Axes.X;
            AutoSizeAxes = Axes.Y;
        }

        public LocalisableString TooltipText => @"drag the handles to set the start and the end · click to jump there";

        private double span => Math.Max(1, getSpan());

        private double songEnd => getEnd() > 0 ? Math.Min(getEnd(), span) : span;

        [BackgroundDependencyLoader]
        private void load()
        {
            Color4 accent = ForgeRoles.Colour(colours, ForgeRole.Companion);

            ForgeButton button(IconUsage icon, string text, string tooltip, Action action, float width) => new ForgeButton(icon, text)
            {
                RelativeSizeAxes = Axes.None,
                Width = width,
                Height = 28,
                TooltipText = tooltip,
                Action = action,
            };

            InternalChild = new Container
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Masking = true,
                CornerRadius = 6,
                CornerExponent = FormControlBackground.CORNER_EXPONENT,
                Children = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both, Colour = colourProvider.Background5 },
                    new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Vertical,
                        Spacing = new Vector2(0, 8),
                        Padding = new MarginPadding(12),
                        Children = new Drawable[]
                        {
                            new FillFlowContainer
                            {
                                RelativeSizeAxes = Axes.X,
                                AutoSizeAxes = Axes.Y,
                                Direction = FillDirection.Full,
                                Spacing = new Vector2(8),
                                Children = new Drawable[]
                                {
                                    new SpriteIcon { Icon = FontAwesome.Solid.Cut, Size = new Vector2(13), Colour = accent, Margin = new MarginPadding { Top = 7 } },
                                    new OsuSpriteText
                                    {
                                        Text = title,
                                        Font = OsuFont.GetFont(size: 16, weight: FontWeight.Bold),
                                        Margin = new MarginPadding { Top = 4 },
                                    },
                                    summary = new OsuSpriteText
                                    {
                                        Font = OsuFont.GetFont(size: 13, weight: FontWeight.SemiBold),
                                        Colour = colourProvider.Content2,
                                        Margin = new MarginPadding { Top = 6, Right = 8 },
                                    },
                                    button(FontAwesome.Solid.StepBackward, @"start here", @"Start at the playhead", () => setFromPlayhead(true), 110),
                                    button(FontAwesome.Solid.StepForward, @"end here", @"End at the playhead", () => setFromPlayhead(false), 100),
                                    button(FontAwesome.Solid.ExpandArrowsAlt, wholeLabel, @"No start/end trim", reset, 120),
                                },
                            },
                            bar = new Container
                            {
                                RelativeSizeAxes = Axes.X,
                                Height = 36,
                                Masking = true,
                                CornerRadius = 4,
                                Children = new Drawable[]
                                {
                                    new Box { RelativeSizeAxes = Axes.Both, Colour = colourProvider.Background6 },
                                    window = new Box { RelativeSizeAxes = Axes.Both, RelativePositionAxes = Axes.X, Colour = accent, Alpha = 0.35f },
                                    shadeBefore = new Box { RelativeSizeAxes = Axes.Both, Colour = Color4.Black, Alpha = 0.5f },
                                    shadeAfter = new Box { RelativeSizeAxes = Axes.Both, RelativePositionAxes = Axes.X, Colour = Color4.Black, Alpha = 0.5f },
                                    startHandle = new Box { RelativeSizeAxes = Axes.Y, RelativePositionAxes = Axes.X, Width = 6, Origin = Anchor.TopCentre, Colour = accent },
                                    endHandle = new Box { RelativeSizeAxes = Axes.Y, RelativePositionAxes = Axes.X, Width = 6, Origin = Anchor.TopCentre, Colour = accent },
                                    playhead = new Box { RelativeSizeAxes = Axes.Y, RelativePositionAxes = Axes.X, Width = 1.5f, Origin = Anchor.TopCentre, Colour = Color4.White },
                                },
                            },
                        },
                    },
                },
            };
        }

        protected override void Update()
        {
            base.Update();

            float a = (float)(start / span), b = (float)(songEnd / span);

            window.X = a;
            window.Width = Math.Max(0, b - a);
            shadeBefore.Width = a;
            shadeAfter.X = b;
            shadeAfter.Width = 1 - b;
            startHandle.X = a;
            endHandle.X = b;

            if (clock != null)
            {
                double x = clockToLocal(clock.CurrentTime) / span;
                playhead.X = (float)Math.Clamp(x, 0, 1);
                playhead.Alpha = x >= 0 && x <= 1 ? 0.9f : 0;
            }

            double length = Math.Max(0, songEnd - start);
            summary.Text = $"{ForgeFormat.Time(start / 1000)} → {ForgeFormat.Time(songEnd / 1000)} · {lengthLabel} {ForgeFormat.Time(length / 1000)}"
                           + (hasWindow ? string.Empty : $" ({wholeLabel})");
        }

        private void setFromPlayhead(bool start)
        {
            if (clock == null) return;

            double t = Math.Round(Math.Clamp(clockToLocal(clock.CurrentTime), 0, span) / snap_ms) * snap_ms;
            ForgeTrackSession.History.Record();

            if (start)
                setStart(Math.Max(0, Math.Min(t, songEnd - min_song_ms)));
            else
                setEnd(t >= span - snap_ms ? 0 : Math.Max(t, this.start + min_song_ms));

            onChanged();
        }

        private void reset()
        {
            if (!hasWindow) return;

            ForgeTrackSession.History.Record();
            setWindow(0, 0);
            onChanged();
        }

        private double timeAt(Vector2 screenSpace)
            => Math.Clamp(bar.ToLocalSpace(screenSpace).X / Math.Max(1, bar.DrawWidth), 0, 1) * span;

        private Grab grabAt(Vector2 screenSpace)
        {
            float x = bar.ToLocalSpace(screenSpace).X;
            float w = Math.Max(1, bar.DrawWidth);

            if (Math.Abs(x - start / span * w) <= grab_px) return Grab.Start;
            if (Math.Abs(x - songEnd / span * w) <= grab_px) return Grab.End;

            return Grab.None;
        }

        protected override bool OnClick(ClickEvent e)
        {
            if (!bar.ReceivePositionalInputAt(e.ScreenSpaceMousePosition)) return false;

            // timeAt is local; jump the clock to the same point (local → clock).
            clock?.SeekSmoothlyTo(timeAt(e.ScreenSpaceMousePosition) - clockToLocal(0));
            return true;
        }

        protected override bool OnDragStart(DragStartEvent e)
        {
            grab = grabAt(e.ScreenSpaceMouseDownPosition);
            if (grab == Grab.None) return false;

            ForgeTrackSession.History.Record();
            grabStart = grab == Grab.Start ? start : songEnd;
            return true;
        }

        protected override void OnDrag(DragEvent e)
        {
            double delta = (e.MousePosition.X - e.MouseDownPosition.X) / Math.Max(1, bar.DrawWidth) * span;
            double t = Math.Round((grabStart + delta) / snap_ms) * snap_ms;

            if (grab == Grab.Start)
                setStart(Math.Clamp(t, 0, Math.Max(0, songEnd - min_song_ms)));
            else if (grab == Grab.End)
            {
                t = Math.Clamp(t, start + min_song_ms, span);
                setEnd(t >= span - snap_ms ? 0 : t); // back at the end = untrimmed
            }
        }

        protected override void OnDragEnd(DragEndEvent e)
        {
            if (grab != Grab.None)
                onChanged();

            grab = Grab.None;
            base.OnDragEnd(e);
        }
    }
}
