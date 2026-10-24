// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Audio.Track;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Audio;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input.Events;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Overlays;
using osu.Game.Screens.Edit;
using osuTK;
using osuTK.Graphics;
using Kan.Forge.Tracks;
using Kan.Core.Tracks;

namespace Kan.Forge.Editor
{
    /// <summary>
    /// Waveform of a track — activity is visible before selecting anything
    /// (owner 2026-10-13). One mirrored bar per analysis bucket, coloured by
    /// the track's highlight; a pulsing skeleton while the analysis runs.
    ///
    /// Optional layers (the strip is meant to grow into a timeline cell):
    /// a time axis, the editor's playhead, and a range selection that can be
    /// dragged on the waveform (bound to an <see cref="EditRange"/>, the same
    /// one the typed seconds boxes edit).
    /// </summary>
    internal partial class WaveformPreview : CompositeDrawable
    {
        private const float axis_height = 13;
        private const float handle_grab_px = 8;

        private readonly ForgeTrack track;

        /// <summary>Draw a seconds axis under the bars (needs a known duration).</summary>
        public bool ShowTimeAxis { get; init; }

        /// <summary>Draw the editor clock's position (tracks are time-aligned to the song).</summary>
        public bool ShowPlayhead { get; init; }

        /// <summary>The range drawn (and edited, see <see cref="RangeEditable"/>) on the waveform.</summary>
        public EditRange? Range { get; init; }

        /// <summary>Whether dragging on the waveform selects/adjusts <see cref="Range"/>.</summary>
        public bool RangeEditable { get; set; }

        /// <summary>
        /// Draw the full-resolution waveform (the compose tab's graph, coloured
        /// by low/mid/high content) instead of the overview bars.
        /// </summary>
        public bool Detailed { get; init; }

        /// <summary>Darken the parts the track's pending range edits will silence.</summary>
        public bool ShowPendingEdits { get; init; }

        /// <summary>A click moves the editor clock to that point of the track (tracks are time-aligned to the song).</summary>
        public bool SeekOnClick { get; init; }

        [Resolved]
        private OverlayColourProvider colourProvider { get; set; } = null!;

        [Resolved]
        private EditorClock? clock { get; set; }

        private Container barArea = null!;
        private Container bars = null!;
        private Container skeleton = null!;
        private Container axis = null!;
        private Box playhead = null!;
        private Container rangeLayer = null!;
        private Box shadeBefore = null!, shadeAfter = null!, selection = null!, handleFrom = null!, handleTo = null!;
        private OsuSpriteText fromLabel = null!, toLabel = null!;

        private WaveformGraph? graph;
        private Container editsLayer = null!;
        private OsuSpriteText failedText = null!;
        private string? drawnEditsKey;

        private static readonly ConcurrentDictionary<string, Waveform> waveforms = new ConcurrentDictionary<string, Waveform>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Full-resolution waveform of a file (decoded in the background, cached per path).</summary>
        private static Waveform? waveformOf(string path)
        {
            if (waveforms.TryGetValue(path, out var w)) return w;
            if (!File.Exists(path)) return null;

            try
            {
                return waveforms.GetOrAdd(path, p => new Waveform(File.OpenRead(p)));
            }
            catch
            {
                return null;
            }
        }

        private float[]? drawnPeaks;
        private bool drawnAny;
        private double? drawnAxisDuration;
        private Color4 accent = Color4.White;

        private enum DragMode
        {
            None,
            New,
            From,
            To,
        }

        private DragMode dragMode;
        private double dragAnchor;

        public WaveformPreview(ForgeTrack track)
        {
            this.track = track;
        }

        /// <summary>The bars' colour (role highlight, neutral, or grey for "no meaningful audio").</summary>
        public Color4 AccentColour
        {
            get => accent;
            set
            {
                accent = value;
                if (LoadState < LoadState.Ready) return;

                bars.FadeColour(value, 200, Easing.OutQuint);
                if (graph != null) colourGraph(graph, value);
            }
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            InternalChildren = new Drawable[]
            {
                barArea = new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Padding = new MarginPadding { Top = 3, Bottom = ShowTimeAxis ? axis_height + 2 : 3 },
                    Children = new Drawable[]
                    {
                        new Box
                        {
                            RelativeSizeAxes = Axes.X,
                            Height = 1,
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Colour = colourProvider.Light4,
                            Alpha = 0.25f,
                        },
                        skeleton = createSkeleton(),
                        bars = new Container
                        {
                            RelativeSizeAxes = Axes.Both,
                            Colour = accent,
                        },
                        // Above the bars, below the edit and range overlays.
                        Detailed ? graph = createGraph() : Empty(),
                        editsLayer = new Container { RelativeSizeAxes = Axes.Both },
                        failedText = new OsuSpriteText
                        {
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            Text = @"can't read this file",
                            Font = OsuFont.GetFont(size: 11, weight: FontWeight.SemiBold),
                            Colour = colourProvider.Foreground1,
                            Alpha = 0,
                        },
                        rangeLayer = new Container
                        {
                            RelativeSizeAxes = Axes.Both,
                            Alpha = 0,
                            Children = new Drawable[]
                            {
                                shadeBefore = new Box { RelativeSizeAxes = Axes.Both, Colour = Color4.Black, Alpha = 0.45f },
                                shadeAfter = new Box { RelativeSizeAxes = Axes.Both, RelativePositionAxes = Axes.X, Colour = Color4.Black, Alpha = 0.45f },
                                selection = new Box { RelativeSizeAxes = Axes.Both, RelativePositionAxes = Axes.X, Colour = colourProvider.Highlight1, Alpha = 0.16f },
                                handleFrom = new Box { RelativeSizeAxes = Axes.Y, RelativePositionAxes = Axes.X, Width = 2, Origin = Anchor.TopCentre, Colour = colourProvider.Highlight1 },
                                handleTo = new Box { RelativeSizeAxes = Axes.Y, RelativePositionAxes = Axes.X, Width = 2, Origin = Anchor.TopCentre, Colour = colourProvider.Highlight1 },
                                fromLabel = new OsuSpriteText
                                {
                                    RelativePositionAxes = Axes.X,
                                    Origin = Anchor.TopRight,
                                    Font = OsuFont.GetFont(size: 10, weight: FontWeight.Bold),
                                    Colour = colourProvider.Highlight1,
                                    Margin = new MarginPadding { Right = 4 },
                                },
                                toLabel = new OsuSpriteText
                                {
                                    RelativePositionAxes = Axes.X,
                                    Origin = Anchor.TopLeft,
                                    Font = OsuFont.GetFont(size: 10, weight: FontWeight.Bold),
                                    Colour = colourProvider.Highlight1,
                                    Margin = new MarginPadding { Left = 4 },
                                },
                            },
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
                    },
                },
                axis = new Container
                {
                    RelativeSizeAxes = Axes.X,
                    Height = axis_height,
                    Anchor = Anchor.BottomLeft,
                    Origin = Anchor.BottomLeft,
                    Alpha = ShowTimeAxis ? 1 : 0,
                },
            };
        }

        private WaveformGraph createGraph()
        {
            var g = new WaveformGraph { RelativeSizeAxes = Axes.Both };
            colourGraph(g, accent);
            return g;
        }

        private static void colourGraph(WaveformGraph g, Color4 colour)
        {
            g.BaseColour = colour.Opacity(0.3f);
            g.LowColour = colour.Lighten(0.4f);
            g.MidColour = colour;
            g.HighColour = colour.Darken(0.4f);
        }

        protected override void Update()
        {
            base.Update();

            if (!drawnAny || !ReferenceEquals(drawnPeaks, track.Peaks))
                rebuildBars();

            if (graph != null && graph.Waveform == null)
                graph.Waveform = waveformOf(track.Path);

            bool failed = track.Peaks == null && track.AnalysisFailed;
            failedText.Alpha = failed ? 1 : 0;
            if (failed && skeleton.Alpha > 0)
            {
                skeleton.ClearTransforms();
                skeleton.Alpha = 0;
            }

            double? duration = TrackDurations.Get(track.Path);

            if (ShowPendingEdits)
                updateEditsLayer(duration);

            if (ShowTimeAxis && duration != drawnAxisDuration)
                rebuildAxis(duration);

            updatePlayhead(duration);
            updateRange(duration);
        }

        // ------------------------------------------------------------------
        // Bars
        // ------------------------------------------------------------------

        private Container createSkeleton()
        {
            var c = new Container { RelativeSizeAxes = Axes.Both, Alpha = 0.35f };
            const int n = 40;

            for (int i = 0; i < n; i++)
            {
                float h = 0.25f + 0.45f * MathF.Abs(MathF.Sin(i * 0.73f) * MathF.Cos(i * 0.31f));
                c.Add(new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    RelativePositionAxes = Axes.X,
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.Centre,
                    X = (i + 0.5f) / n,
                    Width = 0.5f / n,
                    Height = h,
                    Colour = colourProvider.Light4,
                });
            }

            return c;
        }

        private void rebuildBars()
        {
            drawnAny = true;
            drawnPeaks = track.Peaks;
            bars.Clear();

            if (drawnPeaks == null)
            {
                // Analysing: a pulsing placeholder instead of a text label.
                skeleton.FadeTo(0.35f).Then().FadeTo(0.12f, 700, Easing.InOutSine).Then().FadeTo(0.35f, 700, Easing.InOutSine).Loop();
                return;
            }

            skeleton.ClearTransforms();
            skeleton.FadeOut(200);

            int n = drawnPeaks.Length;

            for (int i = 0; i < n; i++)
            {
                // sqrt: quiet material stays readable, silence stays flat.
                float h = Math.Clamp(MathF.Sqrt(Math.Max(0, drawnPeaks[i])), 0.03f, 1f);

                bars.Add(new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    RelativePositionAxes = Axes.X,
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.Centre,
                    X = (i + 0.5f) / n,
                    Width = 0.72f / n,
                    Height = h,
                });
            }

            if (Detailed)
                bars.Alpha = 0; // the graph draws the detail; bars only feed the skeleton swap
            else
                bars.FadeInFromZero(250);
        }

        /// <summary>Shade what the pending range edits will silence (rebuilt when they change).</summary>
        private void updateEditsLayer(double? duration)
        {
            var edits = track.Edits;
            string key = edits.Ranges.Count == 0 || duration is not double total
                ? string.Empty
                : string.Join(';', edits.Ranges.Select(r => $"{(int)r.Mode}:{r.FromMs:0}-{r.ToMs:0}")) + $"/{total:0.000}";

            if (key == drawnEditsKey) return;

            drawnEditsKey = key;
            editsLayer.Clear();

            if (key.Length == 0) return;

            double lengthMs = duration!.Value * 1000;

            // Cut the track at every range boundary; each piece is either
            // wholly heard or wholly silenced.
            var cuts = edits.Ranges.SelectMany(r => new[] { r.FromMs, r.ToMs })
                            .Append(0).Append(lengthMs)
                            .Where(c => c >= 0 && c <= lengthMs)
                            .Distinct().OrderBy(c => c).ToList();

            for (int i = 0; i + 1 < cuts.Count; i++)
            {
                double a = cuts[i], b = cuts[i + 1];
                if (b - a < 1 || edits.IsAudibleAt((a + b) / 2)) continue;

                editsLayer.Add(new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    RelativePositionAxes = Axes.X,
                    X = (float)(a / lengthMs),
                    Width = (float)((b - a) / lengthMs),
                    Colour = Color4.Black,
                    Alpha = 0.6f,
                });
            }
        }

        // ------------------------------------------------------------------
        // Time axis + playhead
        // ------------------------------------------------------------------

        private static readonly double[] tick_steps = { 1, 2, 5, 10, 15, 30, 60, 120, 300, 600 };

        private void rebuildAxis(double? duration)
        {
            drawnAxisDuration = duration;
            axis.Clear();

            if (duration is not double total || total <= 0) return;

            double step = tick_steps[^1];

            foreach (double s in tick_steps)
            {
                if (total / s <= 8)
                {
                    step = s;
                    break;
                }
            }

            for (double t = 0; t <= total + 1e-6; t += step)
            {
                float x = (float)(t / total);

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
                    Origin = t == 0 ? Anchor.TopLeft : x > 0.97f ? Anchor.TopRight : Anchor.TopCentre,
                    Text = ForgeFormat.Time(t),
                    Font = OsuFont.GetFont(size: 9, weight: FontWeight.SemiBold),
                    Colour = colourProvider.Foreground1,
                    Margin = new MarginPadding { Left = t == 0 ? 2 : 0 },
                });
            }
        }

        private void updatePlayhead(double? duration)
        {
            if (!ShowPlayhead || clock == null || duration is not double total || total <= 0)
            {
                playhead.Alpha = 0;
                return;
            }

            double x = clock.CurrentTime / 1000 / total;
            playhead.X = (float)Math.Clamp(x, 0, 1);
            playhead.Alpha = x >= 0 && x <= 1 ? 0.85f : 0;
        }

        // ------------------------------------------------------------------
        // Range selection
        // ------------------------------------------------------------------

        private void updateRange(double? duration)
        {
            if (Range == null || !RangeEditable || duration is not double total || total <= 0 || !Range.IsSet)
            {
                rangeLayer.Alpha = 0;
                return;
            }

            float from = (float)Math.Clamp(Range.From.Value / total, 0, 1);
            float to = (float)Math.Clamp(Range.To.Value / total, 0, 1);

            rangeLayer.Alpha = 1;
            shadeBefore.Width = from;
            shadeAfter.X = to;
            shadeAfter.Width = 1 - to;
            selection.X = from;
            selection.Width = Math.Max(0, to - from);
            handleFrom.X = from;
            handleTo.X = to;
            fromLabel.X = from;
            fromLabel.Text = ForgeFormat.Time(Range.From.Value);
            toLabel.X = to;
            toLabel.Text = ForgeFormat.Time(Range.To.Value);
        }

        private double secondsAt(float localX, double total)
            => Math.Round(Math.Clamp(localX / Math.Max(1, barArea.DrawWidth), 0, 1) * total, 2);

        protected override bool OnClick(ClickEvent e)
        {
            if (!SeekOnClick || clock == null || TrackDurations.Get(track.Path) is not double total || total <= 0)
                return false;

            clock.SeekSmoothlyTo(secondsAt(barArea.ToLocalSpace(e.ScreenSpaceMousePosition).X, total) * 1000);
            return true;
        }

        protected override bool OnDragStart(DragStartEvent e)
        {
            if (Range == null || !RangeEditable || TrackDurations.Get(track.Path) is not double total || total <= 0)
                return false;

            float x = barArea.ToLocalSpace(e.ScreenSpaceMouseDownPosition).X;
            float w = Math.Max(1, barArea.DrawWidth);

            if (Range.IsSet && Math.Abs(x - Range.From.Value / total * w) <= handle_grab_px)
                dragMode = DragMode.From;
            else if (Range.IsSet && Math.Abs(x - Range.To.Value / total * w) <= handle_grab_px)
                dragMode = DragMode.To;
            else
            {
                dragMode = DragMode.New;
                dragAnchor = secondsAt(x, total);
            }

            return true;
        }

        protected override void OnDrag(DragEvent e)
        {
            if (Range == null || TrackDurations.Get(track.Path) is not double total) return;

            double s = secondsAt(barArea.ToLocalSpace(e.ScreenSpaceMousePosition).X, total);

            switch (dragMode)
            {
                case DragMode.New:
                    setRange(Math.Min(dragAnchor, s), Math.Max(dragAnchor, s));
                    break;

                case DragMode.From:
                    if (s >= Range.To.Value)
                    {
                        dragMode = DragMode.To;
                        setRange(Range.To.Value, s);
                    }
                    else
                        setRange(s, Range.To.Value);

                    break;

                case DragMode.To:
                    if (s <= Range.From.Value)
                    {
                        dragMode = DragMode.From;
                        setRange(s, Range.From.Value);
                    }
                    else
                        setRange(Range.From.Value, s);

                    break;
            }
        }

        protected override void OnDragEnd(DragEndEvent e)
        {
            dragMode = DragMode.None;
            base.OnDragEnd(e);
        }

        private void setRange(double from, double to)
        {
            Range!.From.Value = from;
            Range.To.Value = to;
        }

        /// <summary>Waveform colour for a track: its primary role, neutral, or grey when it seems empty.</summary>
        public static Color4 ColourFor(ForgeTrack track, OsuColour colours, OverlayColourProvider provider)
        {
            if (track.LikelyEmpty)
                return provider.Foreground1.Opacity(0.6f);

            var role = ForgeRoles.Primary(track);
            return role != null ? ForgeRoles.Colour(colours, role.Value) : provider.Light2;
        }
    }
}
