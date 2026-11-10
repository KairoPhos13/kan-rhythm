// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Linq;
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
using osu.Game.Overlays;
using osu.Game.Screens.Edit;
using osuTK;
using osuTK.Graphics;
using Kan.Forge.Tracks;
using Kan.Core.Transcription;
using Kan.Core.Tracks;

namespace Kan.Forge.Editor
{
    /// <summary>
    /// One track on the set's shared timeline (owner 2026-10-14: every track
    /// can be moved in time freely). The track is a clip placed at its start
    /// position in a view shared by all lanes (<see cref="SetMonitor"/>
    /// zoom/pan): drag the clip to move it, click anywhere to jump there,
    /// Ctrl+wheel to zoom, Shift+wheel to pan. In edit mode dragging on the
    /// clip selects a range instead (in the track's own time). The waveform is
    /// the compose tab's detailed graph; pending silences are shaded; the
    /// transcription's notes are drawn on the track they came from.
    /// </summary>
    internal partial class TrackLane : CompositeDrawable, IHasTooltip
    {
        private const float axis_height = 14;

        /// <summary>Moves snap to this grid (ms).</summary>
        private const double snap_ms = 10;

        private readonly ForgeTrack track;
        private readonly EditRange range;

        /// <summary>Invoked when a drag finished moving the track.</summary>
        public Action? Moved { get; init; }

        [Resolved]
        private SetMonitor monitor { get; set; } = null!;

        [Resolved]
        private EditorClock? clock { get; set; }

        [Resolved]
        private OverlayColourProvider colourProvider { get; set; } = null!;

        private Container clipArea = null!;
        private Container clip = null!;
        private Box clipFill = null!;
        private WaveformPreview waveform = null!;
        private Container axis = null!;
        private Box playhead = null!;
        private OsuSpriteText moveLabel = null!;

        private Color4 accent = Color4.White;
        private bool editMode;
        private (double start, double length) drawnAxisView = (-1, -1);
        private Container notesLayer = null!;
        private Container ghost = null!;
        private Container full = null!;
        private Box startHandle = null!;
        private MoveGrip grip = null!;
        private Box endHandle = null!;

        /// <summary>Grab distance (px) for a clip's sides.</summary>
        private const float edge_grab_px = 8;

        private enum DragKind
        {
            None,
            Move,
            TrimStart,
            TrimEnd,
        }

        private DragKind dragKind;
        private double dragStartValue;
        private DragKind hoveredEdge;
        private TranscriptionDraft? drawnDraft;
        private double drawnNotesLength = -1;


        public TrackLane(ForgeTrack track, EditRange range)
        {
            this.track = track;
            this.range = range;
        }

        public LocalisableString TooltipText => editMode
            ? @"drag across the track to select a range · drag the strip on top to move it · click to jump there · Ctrl+wheel to zoom"
            : hoveredEdge != DragKind.None
                ? @"drag this side to trim the track (drag back out to restore it)"
                : @"drag the track to move it · drag its sides to trim · click to jump there · Ctrl+wheel to zoom, Shift+wheel to pan";

        /// <summary>Edit mode: dragging selects a range instead of moving the track.</summary>
        public bool EditMode
        {
            get => editMode;
            set
            {
                editMode = value;
                if (LoadState >= LoadState.Ready) waveform.RangeEditable = value;
            }
        }

        public Color4 AccentColour
        {
            get => accent;
            set
            {
                accent = value;

                if (LoadState < LoadState.Ready) return;

                waveform.AccentColour = value;
                clipFill.FadeColour(value, 200);
                clip.TransformTo(nameof(BorderColour), (osu.Framework.Graphics.Colour.ColourInfo)value.Opacity(0.6f), 200);
            }
        }

        /// <summary>Dim the clip when the track isn't heard (muted, or others soloed).</summary>
        public void SetAudible(bool audible) => clip.FadeTo(audible ? 1 : 0.35f, 200);

        [BackgroundDependencyLoader]
        private void load()
        {
            Masking = true;
            CornerRadius = 4;

            InternalChildren = new Drawable[]
            {
                new Box { RelativeSizeAxes = Axes.Both, Colour = colourProvider.Background6.Opacity(0.7f) },
                clipArea = new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Padding = new MarginPadding { Top = 3, Bottom = axis_height + 2 },
                    Children = new Drawable[]
                    {
                        // The whole file, faintly: what a trimmed side can be dragged back to.
                        ghost = new Container
                        {
                            RelativeSizeAxes = Axes.Both,
                            RelativePositionAxes = Axes.X,
                            Masking = true,
                            CornerRadius = 3,
                            BorderThickness = 1,
                            BorderColour = colourProvider.Light4.Opacity(0.25f),
                            Child = new Box { RelativeSizeAxes = Axes.Both, Colour = colourProvider.Light4, Alpha = 0.04f },
                        },
                        clip = new Container
                        {
                            RelativeSizeAxes = Axes.Both,
                            RelativePositionAxes = Axes.X,
                            Masking = true,
                            CornerRadius = 3,
                            BorderThickness = 1.5f,
                            BorderColour = accent.Opacity(0.6f),
                            Children = new Drawable[]
                            {
                                clipFill = new Box { RelativeSizeAxes = Axes.Both, Colour = accent, Alpha = 0.1f },
                                // The whole file, shifted so the window shows its own part.
                                full = new Container
                                {
                                    RelativeSizeAxes = Axes.Both,
                                    RelativePositionAxes = Axes.X,
                                    Children = new Drawable[]
                                    {
                                        waveform = new WaveformPreview(track)
                                        {
                                            RelativeSizeAxes = Axes.Both,
                                            Range = range,
                                            RangeEditable = editMode,
                                            AccentColour = accent,
                                            Detailed = true,
                                            ShowPendingEdits = true,
                                        },
                                        notesLayer = new Container { RelativeSizeAxes = Axes.Both },
                                    },
                                },
                                // Always moves the track, while editing too (the body then selects ranges).
                                grip = new MoveGrip(this) { RelativeSizeAxes = Axes.X, Height = 14 },
                                startHandle = new Box { RelativeSizeAxes = Axes.Y, Width = 5, Colour = colourProvider.Highlight1, Alpha = 0 },
                                endHandle = new Box { RelativeSizeAxes = Axes.Y, Width = 5, Anchor = Anchor.TopRight, Origin = Anchor.TopRight, Colour = colourProvider.Highlight1, Alpha = 0 },
                            },
                        },
                    },
                },
                axis = new Container
                {
                    RelativeSizeAxes = Axes.X,
                    Height = axis_height,
                    Anchor = Anchor.BottomLeft,
                    Origin = Anchor.BottomLeft,
                },
                moveLabel = new OsuSpriteText
                {
                    RelativePositionAxes = Axes.X,
                    Y = 4,
                    Font = OsuFont.GetFont(size: 11, weight: FontWeight.Bold),
                    Colour = colourProvider.Highlight1,
                    Margin = new MarginPadding { Left = 4 },
                    Alpha = 0,
                },
                playhead = new Box
                {
                    RelativeSizeAxes = Axes.Y,
                    RelativePositionAxes = Axes.X,
                    Width = 1.5f,
                    Origin = Anchor.TopCentre,
                    Colour = Color4.White,
                    Alpha = 0,
                },
            };
        }

        protected override void Update()
        {
            base.Update();

            double viewLength = Math.Max(1, monitor.ViewLengthMs);
            double length = monitor.LengthOf(track);

            if (length > 0)
            {
                double winStart = Math.Min(track.ClipStartMs, Math.Max(0, length - 1));
                double winEnd = Math.Max(winStart + 1, track.ClipEndOr(length));
                double win = winEnd - winStart;

                ghost.X = (float)monitor.ToView(track.OffsetMs);
                ghost.Width = (float)(length / viewLength);
                ghost.Alpha = track.IsClipped ? 1 : 0;

                clip.X = (float)monitor.ToView(track.OffsetMs + winStart);
                clip.Width = (float)Math.Max(win / viewLength, 0.004);

                full.X = (float)(-winStart / win);
                full.Width = (float)(length / win);
            }
            else
            {
                ghost.Alpha = 0;
                clip.X = (float)monitor.ToView(track.OffsetMs);
                clip.Width = 0.05f;
                full.X = 0;
                full.Width = 1;
            }

            var view = (Math.Round(monitor.ViewStartMs), Math.Round(viewLength));
            if (view != drawnAxisView)
                rebuildAxis(monitor.ViewStartMs, viewLength);

            if (clock != null)
            {
                double x = monitor.ToView(clock.CurrentTime);
                playhead.X = (float)Math.Clamp(x, 0, 1);
                playhead.Alpha = x >= 0 && x <= 1 ? 0.9f : 0;
            }

            updateNotes(length);

            moveLabel.X = (float)Math.Clamp(clip.X, 0, 0.9);
            moveLabel.Text = dragKind switch
            {
                DragKind.TrimStart => $"plays from {ForgeFormat.Time(track.ClipStartMs / 1000)} of the file",
                DragKind.TrimEnd => track.ClipEndMs > 0 ? $"plays to {ForgeFormat.Time(track.ClipEndMs / 1000)} of the file" : @"plays to the end",
                _ => $"starts at {ForgeFormat.Time(track.TimelineStartMs / 1000)}",
            };
        }

        /// <summary>The transcription's notes on the track they came from (source time, height by pitch).</summary>
        private void updateNotes(double lengthMs)
        {
            var draft = string.Equals(ForgeTrackSession.DraftSource, track.Path, StringComparison.OrdinalIgnoreCase)
                ? ForgeTrackSession.Draft
                : null;

            if (ReferenceEquals(draft, drawnDraft) && Math.Abs(lengthMs - drawnNotesLength) < 1) return;

            drawnDraft = draft;
            drawnNotesLength = lengthMs;
            notesLayer.Clear();

            if (draft == null || lengthMs <= 0) return;

            var pitched = draft.Notes.Where(n => n.Midi >= 0).ToList();
            int lo = pitched.Count > 0 ? pitched.Min(n => n.Midi) : 60;
            int hi = pitched.Count > 0 ? pitched.Max(n => n.Midi) : 60;
            float rows = Math.Max(1, hi - lo + 1);

            foreach (var n in draft.Notes)
            {
                // Unresolved pitch: a full-height faint marker at the onset.
                bool resolved = n.Midi >= 0;
                notesLayer.Add(new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    RelativePositionAxes = Axes.Both,
                    X = (float)(n.OnsetMs / lengthMs),
                    Width = (float)Math.Max(Math.Max(n.DurMs, 30) / lengthMs, 0.001),
                    Y = resolved ? 1 - (n.Midi - lo + 1) / rows * 0.9f - 0.05f : 0,
                    Height = resolved ? Math.Max(0.9f / rows, 0.03f) : 1,
                    Colour = colourProvider.Highlight1,
                    Alpha = resolved ? 0.85f : 0.25f,
                });
            }
        }

        // ------------------------------------------------------------------
        // Shared time axis
        // ------------------------------------------------------------------

        private static readonly double[] tick_steps_s = { 0.1, 0.25, 0.5, 1, 2, 5, 10, 15, 30, 60, 120, 300, 600 };

        private void rebuildAxis(double startMs, double lengthMs)
        {
            drawnAxisView = (Math.Round(startMs), Math.Round(lengthMs));
            axis.Clear();

            double start = startMs / 1000, length = lengthMs / 1000;
            double step = tick_steps_s[^1];

            foreach (double st in tick_steps_s)
            {
                if (length / st <= 10)
                {
                    step = st;
                    break;
                }
            }

            for (double t = Math.Ceiling(start / step) * step; t <= start + length; t += step)
            {
                float x = (float)((t - start) / length);

                axis.Add(new Box
                {
                    RelativePositionAxes = Axes.X,
                    X = x,
                    Size = new Vector2(1, 4),
                    Colour = colourProvider.Foreground1,
                });
                axis.Add(new OsuSpriteText
                {
                    RelativePositionAxes = Axes.X,
                    X = x,
                    Y = 3,
                    Origin = x < 0.02f ? Anchor.TopLeft : Anchor.TopCentre,
                    Margin = new MarginPadding { Left = x < 0.02f ? 3 : 0 },
                    Text = step < 1 ? $"{ForgeFormat.Time(t)}" : ForgeFormat.Time(Math.Round(t)),
                    Font = OsuFont.GetFont(size: 9, weight: FontWeight.SemiBold),
                    Colour = colourProvider.Foreground1,
                });
            }
        }

        // ------------------------------------------------------------------
        // Input: click = jump there; drag the clip = move it in time
        // ------------------------------------------------------------------

        private double timeAt(Vector2 screenSpace)
            => monitor.ViewStartMs + Math.Clamp(clipArea.ToLocalSpace(screenSpace).X / Math.Max(1, clipArea.DrawWidth), 0, 1) * monitor.ViewLengthMs;

        protected override bool OnScroll(ScrollEvent e)
        {
            if (e.ControlPressed)
            {
                monitor.ZoomBy(e.ScrollDelta.Y > 0 ? 0.8 : 1.25, timeAt(e.ScreenSpaceMousePosition));
                return true;
            }

            if (e.ShiftPressed)
            {
                float delta = e.ScrollDelta.Y != 0 ? e.ScrollDelta.Y : e.ScrollDelta.X;
                monitor.PanBy(-delta * monitor.ViewLengthMs * 0.1);
                return true;
            }

            return false; // plain wheel scrolls the page
        }

        protected override bool OnClick(ClickEvent e)
        {
            clock?.SeekSmoothlyTo(timeAt(e.ScreenSpaceMousePosition));
            return true;
        }

        protected override bool OnHover(HoverEvent e)
        {
            if (!editMode)
                clipFill.FadeTo(0.2f, 150);

            return true;
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            clipFill.FadeTo(0.1f, 200);
            base.OnHoverLost(e);
        }

        /// <summary>Which side of the clip (if any) is under the pointer.</summary>
        private DragKind edgeAt(Vector2 screenSpace)
        {
            var quad = clip.ScreenSpaceDrawQuad;
            float x = screenSpace.X;

            // Prefer the side the pointer is inside of, so short clips stay grabbable.
            if (Math.Abs(x - quad.TopLeft.X) <= edge_grab_px) return DragKind.TrimStart;
            if (Math.Abs(x - quad.TopRight.X) <= edge_grab_px) return DragKind.TrimEnd;

            return DragKind.None;
        }

        protected override bool OnMouseMove(MouseMoveEvent e)
        {
            hoveredEdge = editMode || dragKind != DragKind.None ? DragKind.None : edgeAt(e.ScreenSpaceMousePosition);
            startHandle.Alpha = hoveredEdge == DragKind.TrimStart || dragKind == DragKind.TrimStart ? 0.9f : 0;
            endHandle.Alpha = hoveredEdge == DragKind.TrimEnd || dragKind == DragKind.TrimEnd ? 0.9f : 0;
            return base.OnMouseMove(e);
        }

        protected override bool OnDragStart(DragStartEvent e)
        {
            // In edit mode the waveform takes the drag (range selection); the
            // grip on top still moves the track.
            if (editMode) return false;

            var edge = edgeAt(e.ScreenSpaceMouseDownPosition);

            if (edge == DragKind.None && !clip.ReceivePositionalInputAt(e.ScreenSpaceMouseDownPosition))
                return false;

            beginDrag(edge == DragKind.None ? DragKind.Move : edge);
            return true;
        }

        protected override void OnDrag(DragEvent e) => dragBy(e.MousePosition.X - e.MouseDownPosition.X);

        protected override void OnDragEnd(DragEndEvent e)
        {
            endDrag();
            base.OnDragEnd(e);
        }

        private void beginDrag(DragKind kind)
        {
            ForgeTrackSession.History.Record();
            monitor.FreezeScale();

            dragKind = kind;
            dragStartValue = kind switch
            {
                DragKind.TrimStart => track.ClipStartMs,
                DragKind.TrimEnd => track.ClipEndOr(monitor.LengthOf(track)),
                _ => track.OffsetMs,
            };

            moveLabel.FadeIn(100);
        }

        /// <summary>Apply a drag of <paramref name="deltaPx"/> pixels (in lane space) since it started.</summary>
        private void dragBy(float deltaPx)
        {
            if (dragKind == DragKind.None) return;

            double deltaMs = deltaPx / Math.Max(1, clipArea.DrawWidth) * monitor.ViewLengthMs;
            double value = Math.Round((dragStartValue + deltaMs) / snap_ms) * snap_ms;
            double length = monitor.LengthOf(track);

            switch (dragKind)
            {
                case DragKind.Move:
                    track.OffsetMs = value;
                    break;

                case DragKind.TrimStart:
                    // Never past the other side (keep at least 50 ms).
                    track.ClipStartMs = Math.Clamp(value, 0, Math.Max(0, track.ClipEndOr(length) - 50));
                    break;

                case DragKind.TrimEnd:
                    double end = Math.Clamp(value, track.ClipStartMs + 50, length > 0 ? length : double.MaxValue);
                    track.ClipEndMs = length > 0 && end >= length - snap_ms ? 0 : end; // back at the end = untrimmed
                    break;
            }
        }

        private void endDrag()
        {
            if (dragKind == DragKind.None) return;

            dragKind = DragKind.None;
            monitor.ReleaseScale();
            moveLabel.FadeOut(400);
            startHandle.FadeOut(200);
            endHandle.FadeOut(200);
            Moved?.Invoke();
        }

        /// <summary>
        /// The strip along the top of a track: drag it to move the track in
        /// time, always, including while editing (owner 2026-10-14: syncing
        /// tracks must stay possible at any moment).
        /// </summary>
        private partial class MoveGrip : CompositeDrawable, IHasTooltip
        {
            private readonly TrackLane lane;
            private Box fill = null!;
            private Drawable label = null!;

            public MoveGrip(TrackLane lane)
            {
                this.lane = lane;
            }

            public LocalisableString TooltipText => @"drag to move this track in time (works while editing too)";

            [BackgroundDependencyLoader]
            private void load(OverlayColourProvider colourProvider)
            {
                InternalChildren = new Drawable[]
                {
                    fill = new Box { RelativeSizeAxes = Axes.Both, Colour = colourProvider.Light4, Alpha = 0.12f },
                    label = new FillFlowContainer
                    {
                        AutoSizeAxes = Axes.Both,
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Direction = FillDirection.Horizontal,
                        Spacing = new Vector2(5, 0),
                        Alpha = 0.5f,
                        Children = new Drawable[]
                        {
                            new SpriteIcon
                            {
                                Icon = FontAwesome.Solid.ArrowsAltH,
                                Size = new Vector2(9),
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                            },
                            new OsuSpriteText
                            {
                                Text = @"move",
                                Font = OsuFont.GetFont(size: 10, weight: FontWeight.Bold),
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                            },
                        },
                    },
                };
            }

            protected override bool OnHover(HoverEvent e)
            {
                fill.FadeTo(0.3f, 100);
                label.FadeTo(1, 100);
                return true;
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                fill.FadeTo(0.12f, 200);
                label.FadeTo(0.5f, 200);
                base.OnHoverLost(e);
            }

            protected override bool OnDragStart(DragStartEvent e)
            {
                lane.beginDrag(DragKind.Move);
                return true;
            }

            protected override void OnDrag(DragEvent e)
                => lane.dragBy(lane.ToLocalSpace(e.ScreenSpaceMousePosition).X - lane.ToLocalSpace(e.ScreenSpaceMouseDownPosition).X);

            protected override void OnDragEnd(DragEndEvent e)
            {
                lane.endDrag();
                base.OnDragEnd(e);
            }
        }
    }
}
