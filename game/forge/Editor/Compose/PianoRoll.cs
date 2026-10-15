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
using osu.Framework.Input.Events;
using osu.Framework.Utils;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Overlays;
using osu.Game.Rulesets.Edit;
using osu.Game.Screens.Edit;
using osuTK;
using osuTK.Graphics;
using osuTK.Input;
using Kan.Forge.Score;

namespace Kan.Forge.Editor.Compose
{
    /// <summary>The piano roll's editing tool.</summary>
    internal enum RollTool
    {
        /// <summary>Select, move (also in groups), resize.</summary>
        Select,

        /// <summary>Click to add a note, drag to set its length.</summary>
        Draw,
    }

    /// <summary>
    /// The score as a piano roll (owner 2026-10-14): time across, pitch up.
    /// Notes snap to the timing tab's beat grid (hold Shift for free
    /// placement). Select tool: click / Ctrl+click / drag a box to select,
    /// drag notes to move them together in time and pitch, drag a note's
    /// right edge to resize. Draw tool: click to add, drag to set the length.
    /// Ctrl+wheel zooms time, Shift+wheel pans, the wheel scrolls pitch.
    /// </summary>
    internal partial class PianoRoll : CompositeDrawable
    {
        public const float KEYS_WIDTH = 58;
        private const float edge_grab_px = 6;

        /// <summary>The score changed (the screen records undo before, saves after).</summary>
        public Action? BeforeChange { get; set; }

        public Action? Changed { get; set; }

        public RollTool Tool { get; set; }

        /// <summary>Snap to the beat grid (Shift during a drag overrides it).</summary>
        public bool Snap { get; set; } = true;

        public readonly HashSet<ScoreNote> Selected = new HashSet<ScoreNote>();

        // ---- view ------------------------------------------------------------

        public double ViewStartMs { get; private set; }

        public double ViewLengthMs { get; private set; } = 12000;

        /// <summary>Highest visible pitch (top row).</summary>
        public float TopMidi { get; private set; } = 84;

        public float RowHeight { get; private set; } = 14;

        public bool FollowPlayhead { get; set; } = true;

        [Resolved]
        private EditorClock clock { get; set; } = null!;

        [Resolved]
        private EditorBeatmap editorBeatmap { get; set; } = null!;

        [Resolved]
        private IBeatSnapProvider? snapProvider { get; set; }

        [Resolved]
        private OsuColour colours { get; set; } = null!;

        [Resolved]
        private OverlayColourProvider colourProvider { get; set; } = null!;

        private Container keys = null!;
        private Container grid = null!;
        private Container rows = null!;
        private Container beats = null!;
        // Created up front: the screen adds its layers before this loads.
        private readonly Container overlays = new Container { RelativeSizeAxes = Axes.Both };
        private Container notes = null!;
        private Box playhead = null!;
        private Box band = null!;
        private Box rowHighlight = null!;
        private Box keyHighlight = null!;
        private Box timeMarker = null!;
        private Container ghost = null!;
        private OsuSpriteText ghostLabel = null!;

        private string? drawnGridKey;
        private string? drawnNotesKey;
        private int scoreVersion;

        private KanScore score => ScoreSession.Score;

        public PianoRoll()
        {
            RelativeSizeAxes = Axes.Both;
            Masking = true;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            InternalChildren = new Drawable[]
            {
                new Box { RelativeSizeAxes = Axes.Both, Colour = colourProvider.Background6 },
                grid = new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Padding = new MarginPadding { Left = KEYS_WIDTH },
                    Children = new Drawable[]
                    {
                        rows = new Container { RelativeSizeAxes = Axes.Both },
                        beats = new Container { RelativeSizeAxes = Axes.Both },
                        // Layers under the notes (spectrogram, section highlights).
                        overlays,
                        notes = new Container { RelativeSizeAxes = Axes.Both },
                        band = new Box { Colour = colourProvider.Highlight1, Alpha = 0 },
                        rowHighlight = new Box { RelativeSizeAxes = Axes.X, Colour = colourProvider.Light4, Alpha = 0 },
                        timeMarker = new Box { RelativeSizeAxes = Axes.Y, Width = 1.5f, Origin = Anchor.TopCentre, Colour = colourProvider.Highlight1, Alpha = 0 },
                        ghost = new Container
                        {
                            Masking = true,
                            CornerRadius = 2,
                            BorderThickness = 1.5f,
                            BorderColour = Color4.White,
                            Alpha = 0,
                            Child = new Box { RelativeSizeAxes = Axes.Both, Colour = colours.Pink, Alpha = 0.45f },
                        },
                        ghostLabel = new OsuSpriteText
                        {
                            Font = OsuFont.GetFont(size: 11, weight: FontWeight.Bold),
                            Colour = Color4.White,
                            Alpha = 0,
                        },
                        playhead = new Box
                        {
                            RelativeSizeAxes = Axes.Y,
                            Width = 1.5f,
                            Origin = Anchor.TopCentre,
                            Colour = Color4.White,
                        },
                    },
                },
                keys = new Container { RelativeSizeAxes = Axes.Y, Width = KEYS_WIDTH },
                keyHighlight = new Box { Width = KEYS_WIDTH, Colour = colours.Pink, Alpha = 0 },
            };
        }

        /// <summary>A layer under the notes, in the grid's coordinates (time × pitch), for the spectrogram.</summary>
        public Container Overlays => overlays;

        /// <summary>Redraw the notes now (after an outside change: undo, inspector, filter).</summary>
        public void Invalidate() => scoreVersion++;

        // ------------------------------------------------------------------
        // Coordinates
        // ------------------------------------------------------------------

        private float gridWidth => Math.Max(1, DrawWidth - KEYS_WIDTH);

        public float XOf(double ms) => (float)((ms - ViewStartMs) / ViewLengthMs * gridWidth);

        public double MsAt(float x) => ViewStartMs + x / gridWidth * ViewLengthMs;

        public float YOf(float midi) => (TopMidi - midi) * RowHeight;

        public int MidiAt(float y) => Math.Clamp((int)MathF.Ceiling(TopMidi - y / RowHeight), 0, 127);

        /// <summary>
        /// Pointer position in the note area's own coordinates (what XOf/YOf
        /// use) — inside the keyboard's padding, not the padded container.
        /// </summary>
        private Vector2 gridPos(Vector2 screenSpace) => notes.ToLocalSpace(screenSpace);

        private double snap(double ms, bool free) => !Snap || free || snapProvider == null ? ms : snapProvider.SnapTime(ms, null);

        // ------------------------------------------------------------------
        // View
        // ------------------------------------------------------------------

        public void ZoomTime(double factor, double anchorMs)
        {
            double length = Math.Clamp(ViewLengthMs * factor, 500, 30 * 60 * 1000);
            ViewStartMs = Math.Max(0, anchorMs - (anchorMs - ViewStartMs) * (length / ViewLengthMs));
            ViewLengthMs = length;
        }

        public void PanTime(double ms) => ViewStartMs = Math.Max(0, ViewStartMs + ms);

        public void ScrollPitch(float semitones) => TopMidi = Math.Clamp(TopMidi + semitones, DrawHeight / RowHeight, 127);

        public void ZoomPitch(float factor) => RowHeight = Math.Clamp(RowHeight * factor, 6, 40);

        private bool fitPending;

        /// <summary>Fit as soon as the roll has its size (on first show).</summary>
        public void RequestFit() => fitPending = true;

        /// <summary>Show the whole score — its time span and its pitch range (or 12 s around C4 when empty).</summary>
        public void Fit()
        {
            if (score.Notes.Count == 0)
            {
                ViewStartMs = 0;
                ViewLengthMs = 12000;
                return;
            }

            double start = score.Notes.Min(n => n.StartMs), end = score.Notes.Max(n => n.EndMs);
            ViewStartMs = Math.Max(0, start - 1000);
            ViewLengthMs = Math.Max(2000, end - ViewStartMs + 1000);

            int high = score.Notes.Max(n => n.Midi), low = score.Notes.Min(n => n.Midi);

            // Rows sized so the whole pitch range fits, with two rows of margin each side.
            RowHeight = Math.Clamp(DrawHeight / (high - low + 5), 6, 40);
            float visibleRows = DrawHeight / RowHeight;
            TopMidi = Math.Clamp(high + Math.Max(2, (visibleRows - (high - low + 1)) / 2), visibleRows, 127);
        }

        /// <summary>Bring a time into view (keeps the zoom).</summary>
        public void Reveal(double ms)
        {
            if (ms < ViewStartMs || ms > ViewStartMs + ViewLengthMs)
                ViewStartMs = Math.Max(0, ms - ViewLengthMs * 0.1);
        }

        protected override void Update()
        {
            base.Update();

            if (fitPending && DrawHeight > 0 && DrawWidth > KEYS_WIDTH)
            {
                fitPending = false;
                Fit();
            }

            if (FollowPlayhead && clock.IsRunning)
            {
                double t = clock.CurrentTime;
                if (t < ViewStartMs || t > ViewStartMs + ViewLengthMs * 0.95)
                    ViewStartMs = Math.Max(0, t - ViewLengthMs * 0.05);
            }

            string gridKey = $"{ViewStartMs:0.0}|{ViewLengthMs:0.0}|{TopMidi:0.00}|{RowHeight:0.0}|{DrawWidth:0}|{DrawHeight:0}|{timingKey()}|{snapProvider?.BeatDivisor}";

            if (gridKey != drawnGridKey)
            {
                drawnGridKey = gridKey;
                rebuildKeysAndRows();
                rebuildBeats();
            }

            string notesKey = gridKey + "#" + scoreVersion + "#" + score.Notes.Count + "#" + Selected.Count;

            if (notesKey != drawnNotesKey)
            {
                drawnNotesKey = notesKey;
                rebuildNotes();
            }

            float px = XOf(clock.CurrentTime);
            playhead.X = px;
            playhead.Alpha = px >= 0 && px <= gridWidth ? 0.9f : 0;
        }

        private string timingKey()
            => string.Join(';', editorBeatmap.ControlPointInfo.TimingPoints.Select(t => $"{t.Time:0.#}:{t.BeatLength:0.###}:{t.TimeSignature.Numerator}"));

        // ------------------------------------------------------------------
        // Drawing
        // ------------------------------------------------------------------

        private static bool isBlack(int midi) => (((midi % 12) + 12) % 12) is 1 or 3 or 6 or 8 or 10;

        private void rebuildKeysAndRows()
        {
            keys.Clear();
            rows.Clear();

            keys.Add(new Box { RelativeSizeAxes = Axes.Both, Colour = colourProvider.Background5 });

            int top = (int)MathF.Ceiling(TopMidi);
            int bottom = Math.Max(0, (int)MathF.Floor(TopMidi - DrawHeight / RowHeight) - 1);

            for (int midi = Math.Min(127, top); midi >= bottom; midi--)
            {
                float y = YOf(midi);
                bool black = isBlack(midi);

                if (black)
                {
                    rows.Add(new Box { RelativeSizeAxes = Axes.X, Height = RowHeight, Y = y, Colour = Color4.Black, Alpha = 0.25f });
                    keys.Add(new Box { Width = KEYS_WIDTH * 0.6f, Height = RowHeight, Y = y, Colour = colourProvider.Background6 });
                }

                // An octave line under every C, and its name on the keyboard.
                if (midi % 12 == 0)
                {
                    rows.Add(new Box { RelativeSizeAxes = Axes.X, Height = 1, Y = y + RowHeight - 1, Colour = colourProvider.Light4, Alpha = 0.18f });
                    keys.Add(new OsuSpriteText
                    {
                        Text = KanScore.NoteName(midi),
                        Font = OsuFont.GetFont(size: Math.Min(12, RowHeight - 2), weight: FontWeight.Bold),
                        Colour = colourProvider.Content2,
                        Anchor = Anchor.TopRight,
                        Origin = Anchor.CentreRight,
                        X = -5,
                        Y = y + RowHeight / 2,
                    });
                }
            }

            keys.Add(new Box { RelativeSizeAxes = Axes.Y, Width = 1, Anchor = Anchor.TopRight, Origin = Anchor.TopRight, Colour = colourProvider.Background3 });
        }

        /// <summary>Beat and bar lines from the timing tab (the snapping grid).</summary>
        private void rebuildBeats()
        {
            beats.Clear();

            var cpi = editorBeatmap.ControlPointInfo;
            double end = ViewStartMs + ViewLengthMs;
            int divisor = Math.Max(1, snapProvider?.BeatDivisor ?? 4);

            double t = ViewStartMs;
            int guard = 0;

            while (t <= end && guard++ < 4000)
            {
                var tp = cpi.TimingPointAt(t);
                double beat = tp.BeatLength;
                double step = beat / divisor;
                int perBar = Math.Max(1, tp.TimeSignature.Numerator);

                // Grid lines on this timing point's own phase.
                long index = (long)Math.Ceiling((t - tp.Time) / step - 1e-6);
                double line = tp.Time + index * step;

                // Too dense to read: only beats.
                if (ViewLengthMs / step > 400) step = beat;
                if (ViewLengthMs / step > 400) break;

                index = (long)Math.Ceiling((t - tp.Time) / step - 1e-6);
                line = tp.Time + index * step;

                var next = cpi.TimingPoints.FirstOrDefault(p => p.Time > line);
                double segmentEnd = next?.Time ?? end;

                for (; line <= Math.Min(end, segmentEnd) && guard++ < 4000; line += step, index++)
                {
                    double beatsFromStart = (line - tp.Time) / beat;
                    bool onBeat = Math.Abs(beatsFromStart - Math.Round(beatsFromStart)) < 1e-6;
                    bool onBar = onBeat && (long)Math.Round(beatsFromStart) % perBar == 0;

                    beats.Add(new Box
                    {
                        RelativeSizeAxes = Axes.Y,
                        Width = onBar ? 1.5f : 1,
                        X = XOf(line),
                        Colour = onBar ? colourProvider.Light2 : colourProvider.Light4,
                        Alpha = onBar ? 0.45f : onBeat ? 0.22f : 0.08f,
                    });
                }

                if (next == null || next.Time > end) break;

                t = next.Time;
            }
        }

        private void rebuildNotes()
        {
            notes.Clear();

            double end = ViewStartMs + ViewLengthMs;
            float topY = -RowHeight, bottomY = DrawHeight;

            foreach (var n in score.Notes)
            {
                if (n.EndMs < ViewStartMs || n.StartMs > end) continue;

                float y = YOf(n.Midi);
                if (y < topY || y > bottomY) continue;

                notes.Add(new NoteBlock(n, Selected.Contains(n), colours, colourProvider)
                {
                    X = XOf(n.StartMs),
                    Y = y + 1,
                    Width = Math.Max(3, XOf(n.EndMs) - XOf(n.StartMs)),
                    Height = RowHeight - 2,
                });
            }
        }

        // ------------------------------------------------------------------
        // Input
        // ------------------------------------------------------------------

        private enum DragMode
        {
            None,
            Move,
            Resize,
            Band,
            DrawNew,
        }

        private DragMode dragMode;
        private Vector2 dragOrigin;
        private ScoreNote? dragNote;
        private List<(ScoreNote Note, double Start, int Midi, double Duration)> originals = new List<(ScoreNote, double, int, double)>();
        private bool changedThisDrag;

        private ScoreNote? noteAt(Vector2 pos, out bool onRightEdge)
        {
            onRightEdge = false;
            int midi = MidiAt(pos.Y);

            // Topmost (latest-starting) note under the pointer.
            foreach (var n in score.Notes.Where(n => n.Midi == midi).OrderByDescending(n => n.StartMs))
            {
                float x0 = XOf(n.StartMs), x1 = Math.Max(x0 + 3, XOf(n.EndMs));
                if (pos.X < x0 || pos.X > x1) continue;

                onRightEdge = x1 - pos.X <= edge_grab_px && x1 - x0 > edge_grab_px * 2;
                return n;
            }

            return null;
        }

        // ------------------------------------------------------------------
        // Hover preview: the row, its key, and (draw tool) the note to be added
        // ------------------------------------------------------------------

        protected override bool OnMouseMove(MouseMoveEvent e)
        {
            updateHover(gridPos(e.ScreenSpaceMousePosition), e.ShiftPressed);
            return base.OnMouseMove(e);
        }

        // Accepting hover is what makes the framework report when the pointer leaves.
        protected override bool OnHover(HoverEvent e) => true;

        protected override void OnHoverLost(HoverLostEvent e)
        {
            hideHover();
            base.OnHoverLost(e);
        }

        private void hideHover()
        {
            rowHighlight.Alpha = keyHighlight.Alpha = timeMarker.Alpha = ghost.Alpha = ghostLabel.Alpha = 0;
        }

        private void updateHover(Vector2 pos, bool free)
        {
            if (dragMode != DragMode.None || pos.X < 0 || pos.X > gridWidth || pos.Y < 0 || pos.Y > DrawHeight)
            {
                hideHover();
                return;
            }

            int midi = MidiAt(pos.Y);
            float y = YOf(midi);

            rowHighlight.Y = y;
            rowHighlight.Height = RowHeight;
            rowHighlight.Alpha = 0.07f;
            keyHighlight.Y = y;
            keyHighlight.Height = RowHeight;
            keyHighlight.Alpha = 0.5f;

            bool overNote = noteAt(pos, out _) != null;
            double start = Math.Max(0, snap(MsAt(pos.X), free));

            if (Tool == RollTool.Draw && !overNote)
            {
                double beat = editorBeatmap.ControlPointInfo.TimingPointAt(start).BeatLength;
                double length = Math.Max(KanScore.MIN_DURATION_MS, beat / Math.Max(1, snapProvider?.BeatDivisor ?? 4));

                ghost.Position = new Vector2(XOf(start), y + 1);
                ghost.Size = new Vector2(Math.Max(3, XOf(start + length) - XOf(start)), RowHeight - 2);
                ghost.Alpha = 1;
                timeMarker.Alpha = 0;

                ghostLabel.Text = $"{KanScore.NoteName(midi)} · {ForgeFormat.Time(start / 1000)}";
                ghostLabel.Position = new Vector2(XOf(start), Math.Max(0, y - 14));
                ghostLabel.Alpha = 1;
            }
            else
            {
                ghost.Alpha = 0;
                ghostLabel.Alpha = 0;

                // Select tool: where a double-click would add a note.
                timeMarker.X = XOf(start);
                timeMarker.Alpha = overNote ? 0 : 0.35f;
            }
        }

        protected override bool OnMouseDown(MouseDownEvent e)
        {
            if (e.Button != MouseButton.Left) return false;

            var pos = gridPos(e.ScreenSpaceMousePosition);
            if (pos.X < 0) return false; // the keyboard

            return true; // take the press; clicks and drags decide
        }

        protected override bool OnClick(ClickEvent e)
        {
            var pos = gridPos(e.ScreenSpaceMousePosition);
            if (pos.X < 0) return false;

            var hit = noteAt(pos, out _);

            if (hit != null)
            {
                if (e.ControlPressed)
                {
                    if (!Selected.Remove(hit)) Selected.Add(hit);
                }
                else
                {
                    Selected.Clear();
                    Selected.Add(hit);
                }
            }
            else if (Tool == RollTool.Draw)
                addNoteAt(pos, e.ShiftPressed, null);
            else if (!e.ControlPressed)
            {
                Selected.Clear();
                clock.SeekSmoothlyTo(Math.Max(0, MsAt(pos.X))); // click on empty space: jump there
            }

            Invalidate();
            return true;
        }

        protected override bool OnDoubleClick(DoubleClickEvent e)
        {
            var pos = gridPos(e.ScreenSpaceMousePosition);
            if (pos.X < 0 || noteAt(pos, out _) != null) return false;

            addNoteAt(pos, e.ShiftPressed, null);
            return true;
        }

        private ScoreNote addNoteAt(Vector2 pos, bool free, double? durationMs)
        {
            BeforeChange?.Invoke();

            double start = Math.Max(0, snap(MsAt(pos.X), free));
            double beat = editorBeatmap.ControlPointInfo.TimingPointAt(start).BeatLength;
            int divisor = Math.Max(1, snapProvider?.BeatDivisor ?? 4);

            var note = new ScoreNote
            {
                StartMs = start,
                DurationMs = durationMs ?? Math.Max(KanScore.MIN_DURATION_MS, beat / divisor),
                Midi = MidiAt(pos.Y),
            };

            score.Notes.Add(note);
            score.Sort();
            Selected.Clear();
            Selected.Add(note);
            Invalidate();
            Changed?.Invoke();
            return note;
        }

        protected override bool OnDragStart(DragStartEvent e)
        {
            if (e.Button != MouseButton.Left) return false;

            var pos = gridPos(e.ScreenSpaceMouseDownPosition);
            if (pos.X < 0) return false;

            dragOrigin = pos;
            changedThisDrag = false;
            hideHover();

            var hit = noteAt(pos, out bool onEdge);

            if (hit != null)
            {
                if (!Selected.Contains(hit))
                {
                    if (!e.ControlPressed) Selected.Clear();
                    Selected.Add(hit);
                }

                dragNote = hit;
                dragMode = onEdge ? DragMode.Resize : DragMode.Move;
                originals = Selected.Select(n => (n, n.StartMs, n.Midi, n.DurationMs)).ToList();
                BeforeChange?.Invoke();
                Invalidate();
                return true;
            }

            if (Tool == RollTool.Draw)
            {
                dragNote = addNoteAt(pos, e.ShiftPressed, KanScore.MIN_DURATION_MS);
                dragMode = DragMode.DrawNew;
                originals = new List<(ScoreNote, double, int, double)> { (dragNote, dragNote.StartMs, dragNote.Midi, dragNote.DurationMs) };
                return true;
            }

            if (!e.ControlPressed) Selected.Clear();
            dragMode = DragMode.Band;
            return true;
        }

        protected override void OnDrag(DragEvent e)
        {
            var pos = gridPos(e.ScreenSpaceMousePosition);
            bool free = e.ShiftPressed;

            switch (dragMode)
            {
                case DragMode.Move:
                {
                    // Restore, then move the whole group by the snapped delta of the grabbed note.
                    foreach (var o in originals)
                    {
                        o.Note.StartMs = o.Start;
                        o.Note.Midi = o.Midi;
                    }

                    var grabbed = originals.First(o => o.Note == dragNote);
                    double target = snap(grabbed.Start + (MsAt(pos.X) - MsAt(dragOrigin.X)), free);
                    int semitones = MidiAt(pos.Y) - MidiAt(dragOrigin.Y);

                    score.Move(originals.Select(o => o.Note).ToList(), target - grabbed.Start, semitones);
                    changedThisDrag = true;
                    break;
                }

                case DragMode.Resize:
                case DragMode.DrawNew:
                {
                    var grabbed = originals.First(o => o.Note == dragNote);
                    double end = snap(MsAt(pos.X), free);
                    double delta = Math.Max(KanScore.MIN_DURATION_MS, end - grabbed.Start) - grabbed.Duration;

                    foreach (var o in originals)
                        o.Note.DurationMs = Math.Max(KanScore.MIN_DURATION_MS, o.Duration + delta);

                    changedThisDrag = true;
                    break;
                }

                case DragMode.Band:
                {
                    float x0 = Math.Min(dragOrigin.X, pos.X), x1 = Math.Max(dragOrigin.X, pos.X);
                    float y0 = Math.Min(dragOrigin.Y, pos.Y), y1 = Math.Max(dragOrigin.Y, pos.Y);

                    band.Position = new Vector2(x0, y0);
                    band.Size = new Vector2(x1 - x0, y1 - y0);
                    band.Alpha = 0.15f;

                    if (!e.ControlPressed) Selected.Clear();

                    double t0 = MsAt(x0), t1 = MsAt(x1);
                    int high = MidiAt(y0), low = MidiAt(y1);

                    foreach (var n in score.Notes)
                    {
                        if (n.EndMs >= t0 && n.StartMs <= t1 && n.Midi >= low && n.Midi <= high)
                            Selected.Add(n);
                    }

                    break;
                }
            }

            Invalidate();
        }

        protected override void OnDragEnd(DragEndEvent e)
        {
            if (dragMode is DragMode.Move or DragMode.Resize or DragMode.DrawNew && changedThisDrag)
            {
                score.Sort();
                Changed?.Invoke();
            }

            band.Alpha = 0;
            dragMode = DragMode.None;
            dragNote = null;
            Invalidate();
            base.OnDragEnd(e);
        }

        protected override bool OnScroll(ScrollEvent e)
        {
            var pos = gridPos(e.ScreenSpaceMousePosition);
            float delta = e.ScrollDelta.Y != 0 ? e.ScrollDelta.Y : e.ScrollDelta.X;

            if (e.ControlPressed && e.ShiftPressed)
                ZoomPitch(delta > 0 ? 1.15f : 1 / 1.15f);
            else if (e.ControlPressed)
                ZoomTime(delta > 0 ? 0.8 : 1.25, MsAt(pos.X));
            else if (e.ShiftPressed)
                PanTime(-delta * ViewLengthMs * 0.1);
            else
                ScrollPitch(delta * 3);

            return true;
        }

        /// <summary>One note: intensity sets its brightness; selected notes are outlined.</summary>
        private partial class NoteBlock : CompositeDrawable
        {
            public NoteBlock(ScoreNote note, bool selected, OsuColour colours, OverlayColourProvider provider)
            {
                Masking = true;
                CornerRadius = 2;
                BorderThickness = selected ? 2 : 0;
                BorderColour = Color4.White;

                // Pink: apart from the companion (blue) and reference (yellow) sound behind.
                Color4 baseColour = note.PitchUnknown ? colours.Gray9 : colours.Pink;
                // Intensity sets the brightness — never so dark the note disappears.
                Color4 fill = Interpolation.ValueAt(note.Intensity, baseColour.Darken(0.6f), baseColour, 0f, 1f);

                InternalChildren = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both, Colour = selected ? fill.Lighten(0.3f) : fill },
                    new OsuSpriteText
                    {
                        Text = (note.PitchUnknown ? "? " : string.Empty) + KanScore.NoteName(note.Midi) + (note.Articulation == Articulation.Normal ? string.Empty : note.Articulation switch
                        {
                            Articulation.Legato => " ⌒",
                            Articulation.Staccato => " ·",
                            _ => " >",
                        }) + (note.Vibrato ? " ~" : string.Empty),
                        Font = OsuFont.GetFont(size: 10, weight: FontWeight.Bold),
                        Colour = provider.Background6,
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        X = 3,
                    },
                };
            }
        }
    }
}
