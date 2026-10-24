// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Beatmaps.Timing;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Overlays;
using osu.Game.Screens.Edit;
using osuTK;
using Kan.Core.Score;

namespace Kan.Forge.Editor.Compose
{
    /// <summary>
    /// The beat grid and the song's tempo, right where the notes are written
    ///: grid divisions (straight, triplets, odd), BPM with
    /// tap tempo, the first beat (to line the grid up with the song) and beats
    /// per bar. The same data the timing tab edits.
    ///
    /// A tempo that breathes (owner 2026-10-22: a cover played without a
    /// click speeds up and slows down): the tempo shown and edited is the one
    /// at the playhead, a tempo change can be put at the playhead, and "the
    /// beat falls here" stretches the tempo before the playhead so the nearest
    /// beat lands on it — the grid fitted to the music, so the notes stay
    /// where they sound (<see cref="ScoreSession.GridChangeKeepsNotes"/>).
    /// </summary>
    internal partial class GridTempoBar : CompositeDrawable
    {
        [Resolved]
        private EditorBeatmap editorBeatmap { get; set; } = null!;

        [Resolved]
        private BindableBeatDivisor beatDivisor { get; set; } = null!;

        [Resolved]
        private EditorClock clock { get; set; } = null!;

        [Resolved]
        private OverlayColourProvider colourProvider { get; set; } = null!;

        private OsuTextBox bpmBox = null!, firstBeatBox = null!, barBox = null!;
        private OsuSpriteText tempoLabel = null!;

        [Resolved]
        private osu.Game.Overlays.INotificationOverlay? notifications { get; set; }
        private readonly List<(int divisor, Chip chip)> chips = new List<(int, Chip)>();
        private readonly List<Family> families = new List<Family>();
        private Family? shownFamily;
        private readonly List<double> taps = new List<double>();
        private string? shownTempo;

        public GridTempoBar()
        {
            RelativeSizeAxes = Axes.X;
            AutoSizeAxes = Axes.Y;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            var flow = new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Full,
                Spacing = new Vector2(4, 6),
            };

            // One grid family at a time (owner 2026-10-14: fewer buttons):
            // pick the family, then one of its divisions.
            flow.Add(label(@"grid"));
            addFamily(flow, @"straight", @"Even divisions of the beat — most music", new[]
            {
                (1, @"1 beat (quarter notes in 4/4)"), (2, @"half a beat (eighth notes)"), (4, @"a quarter beat (sixteenth notes)"),
                (8, @"an eighth of a beat (thirty-second notes)"), (16, @"a sixteenth of a beat"),
            });
            addFamily(flow, @"triplets", @"Beats split in three — a swung or rolling feel", new[]
            {
                (3, @"a third of a beat (eighth-note triplets)"), (6, @"a sixth of a beat (sixteenth-note triplets)"), (12, @"a twelfth of a beat (thirty-second-note triplets)"),
            });
            addFamily(flow, @"other", @"Unusual splits (fives, sevens, nines)", new[]
            {
                (5, @"a fifth of a beat (quintuplets)"), (7, @"a seventh of a beat (septuplets)"), (9, @"a ninth of a beat"),
            });

            foreach (var family in families)
                flow.Add(family.Divisions);

            flow.Add(new Container { Width = 14, Height = 26 });
            flow.Add(tempoLabel = label(@"tempo"));
            flow.Add(bpmBox = box(64, @"beats per minute at the playhead"));
            flow.Add(label(@"BPM"));
            flow.Add(button(FontAwesome.Solid.HandPointUp, @"tap", @"Tap along with the beat a few times: the tempo at the playhead follows your taps", tap, 64));
            flow.Add(button(FontAwesome.Solid.Plus, @"change", @"A tempo change at the playhead: from here on the tempo can differ (it starts as the one before)", addTempoChangeHere, 84));
            flow.Add(button(FontAwesome.Solid.MapPin, @"beat here", @"The music's beat falls at the playhead: the tempo before it stretches or shrinks so the nearest beat lands here. The notes stay where they sound.", beatHere, 96));
            flow.Add(button(FontAwesome.Solid.Times, @"remove", @"Remove the tempo change the playhead is in (the first tempo stays)", removeTempoChange, 84));

            flow.Add(new Container { Width = 14, Height = 26 });
            flow.Add(label(@"first beat"));
            flow.Add(firstBeatBox = box(72, @"where the grid's first beat is, in ms"));
            flow.Add(label(@"ms"));
            flow.Add(button(FontAwesome.Solid.AngleDoubleLeft, @"10", @"Move the grid 10 ms earlier", () => moveFirstBeat(-10), 50));
            flow.Add(button(FontAwesome.Solid.AngleLeft, @"1", @"Move the grid 1 ms earlier", () => moveFirstBeat(-1), 42));
            flow.Add(button(FontAwesome.Solid.AngleRight, @"1", @"Move the grid 1 ms later", () => moveFirstBeat(1), 42));
            flow.Add(button(FontAwesome.Solid.AngleDoubleRight, @"10", @"Move the grid 10 ms later", () => moveFirstBeat(10), 50));
            flow.Add(button(FontAwesome.Solid.MapMarker, @"here", @"Put the grid's first beat at the playhead", () => setFirstBeat(clock.CurrentTime), 70));

            flow.Add(new Container { Width = 14, Height = 26 });
            flow.Add(barBox = box(36, @"beats per bar (the time signature's top number)"));
            flow.Add(label(@"beats per bar"));

            flow.Add(new Container { Width = 14, Height = 26 });
            flow.Add(label(@"the beat is"));
            flow.Add(button(FontAwesome.Solid.Compress, @"½", @"Each beat half as long (the BPM doubles: a half note becomes two quarters). Every tempo of the level; the notes stay where they sound and the grid lines where they were. For a level timed with the wrong note value.", () => ScaleBeat(0.5), 50));
            flow.Add(button(FontAwesome.Solid.Expand, @"×2", @"Each beat twice as long (the BPM halves: two quarters become a half note). Every tempo of the level; the notes stay where they sound and the grid lines where they were. For a level timed with the wrong note value.", () => ScaleBeat(2), 56));

            InternalChild = flow;

            bpmBox.OnCommit += (_, _) =>
            {
                if (double.TryParse(bpmBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double bpm) && bpm >= 10 && bpm <= 1000)
                    setTempo(tp => tp.BeatLength = 60000 / bpm);
                shownTempo = null;
            };
            firstBeatBox.OnCommit += (_, _) =>
            {
                if (double.TryParse(firstBeatBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double ms))
                    setFirstBeat(ms);
                shownTempo = null;
            };
            barBox.OnCommit += (_, _) =>
            {
                if (int.TryParse(barBox.Text, out int beats) && beats is >= 1 and <= 32)
                    setTempo(tp => tp.TimeSignature = new TimeSignature(beats));
                shownTempo = null;
            };
        }

        protected override void Update()
        {
            base.Update();

            foreach (var (divisor, chip) in chips)
                chip.On = beatDivisor.Value == divisor;

            // Show the family of the current grid (it can change elsewhere, e.g. the timeline's own divisor control).
            var current = families.FirstOrDefault(f => f.Has(beatDivisor.Value));
            if (shownFamily == null || (current != null && current != shownFamily))
                showFamily(current ?? families[0]);

            foreach (var family in families)
                family.Chip.On = family == shownFamily;

            // Show the tempo at the playhead (unless the user is typing in one of the boxes).
            var points = editorBeatmap.ControlPointInfo.TimingPoints;
            var tp = currentTimingPoint(points);
            double beatLength = tp?.BeatLength ?? TimingControlPoint.DEFAULT_BEAT_LENGTH;
            double first = points.FirstOrDefault()?.Time ?? 0;
            int beats = tp?.TimeSignature.Numerator ?? 4;

            int index = tp == null ? 0 : points.TakeWhile(p => p != tp).Count();
            tempoLabel.Text = points.Count > 1 ? $"tempo {index + 1}/{points.Count}" : @"tempo";

            string tempo = $"{beatLength:0.###}|{first:0.##}|{beats}|{index}";
            if (tempo == shownTempo || bpmBox.HasFocus || firstBeatBox.HasFocus || barBox.HasFocus) return;

            shownTempo = tempo;
            bpmBox.Text = (60000 / beatLength).ToString("0.##", CultureInfo.InvariantCulture);
            firstBeatBox.Text = first.ToString("0.#", CultureInfo.InvariantCulture);
            barBox.Text = beats.ToString(CultureInfo.InvariantCulture);
        }

        // ------------------------------------------------------------------
        // Tempo edits (the level's first timing point)
        // ------------------------------------------------------------------

        private TimingControlPoint firstTimingPoint()
        {
            var tp = editorBeatmap.ControlPointInfo.TimingPoints.FirstOrDefault();
            if (tp != null) return tp;

            // No tempo yet: start one at 0 (the timing tab's default of 120 BPM).
            tp = new TimingControlPoint { BeatLength = 500 };
            editorBeatmap.ControlPointInfo.Add(0, tp);
            return tp;
        }

        /// <summary>The timing point in effect at the playhead, or null when there is none.</summary>
        private TimingControlPoint? currentTimingPoint(IReadOnlyList<TimingControlPoint> points)
            => points.LastOrDefault(p => p.Time <= clock.CurrentTime + 0.5) ?? points.FirstOrDefault();

        private void setTempo(Action<TimingControlPoint> change)
        {
            editorBeatmap.BeginChange();
            change(currentTimingPoint(editorBeatmap.ControlPointInfo.TimingPoints) ?? firstTimingPoint());
            editorBeatmap.EndChange();
        }

        internal void addTempoChangeHere()
        {
            var cpi = editorBeatmap.ControlPointInfo;
            double at = Math.Round(clock.CurrentTime, 1);
            var tp = currentTimingPoint(cpi.TimingPoints) ?? firstTimingPoint();

            if (cpi.TimingPoints.Any(p => Math.Abs(p.Time - at) < 1))
            {
                ForgeNotify.Info(notifications, "There is a tempo change here already.", FontAwesome.Solid.Plus);
                return;
            }

            editorBeatmap.BeginChange();
            cpi.Add(at, new TimingControlPoint { BeatLength = tp.BeatLength, TimeSignature = tp.TimeSignature });
            editorBeatmap.EndChange();
            shownTempo = null;
        }

        internal void beatHere()
        {
            var cpi = editorBeatmap.ControlPointInfo;
            double at = clock.CurrentTime;

            // The tempo before the playhead (not one starting right on it).
            var tp = cpi.TimingPoints.LastOrDefault(p => p.Time < at - 1);

            if (tp == null)
            {
                ForgeNotify.Info(notifications, "Put the grid's first beat before the playhead first.", FontAwesome.Solid.MapPin);
                return;
            }

            int beats = (int)Math.Max(1, Math.Round((at - tp.Time) / tp.BeatLength));
            double beat = Math.Round((at - tp.Time) / beats, 3);

            if (beat < tp.BeatLength / 1.5 || beat > tp.BeatLength * 1.5)
            {
                ForgeNotify.Info(notifications, "That would change the tempo by more than half: put a tempo change nearer the playhead first.", FontAwesome.Solid.MapPin);
                return;
            }

            string before = gridKey();
            editorBeatmap.BeginChange();
            tp.BeatLength = beat;
            editorBeatmap.EndChange();
            ScoreSession.GridChangeKeepsNotes(before, gridKey());
            shownTempo = null;
        }

        /// <summary>
        /// The level's beat made <paramref name="factor"/> times as long, in
        /// every tempo (owner 2026-10-23: a level timed at 130 BPM with a beat
        /// per quarter, whose pulse is four quarters; the game's windows follow
        /// the notes' pace, not the beat, so this is for the grid). Nothing moves: the notes stay where they
        /// sound (<see cref="ScoreSession.GridChangeKeepsNotes"/>), the bars keep
        /// their length (beats per bar scaled) and the grid its lines (the
        /// division scaled, when it can be).
        /// </summary>
        internal void ScaleBeat(double factor)
        {
            var points = editorBeatmap.ControlPointInfo.TimingPoints;

            if (points.Count == 0)
            {
                ForgeNotify.Info(notifications, "There is no tempo yet: set the BPM first.", FontAwesome.Solid.Expand);
                return;
            }

            foreach (var tp in points)
            {
                double bpm = 60000 / (tp.BeatLength * factor);
                double perBar = tp.TimeSignature.Numerator / factor;

                if (bpm < 10 || bpm > 1000)
                {
                    ForgeNotify.Info(notifications, $"That would make a tempo {bpm:0.#} BPM (10 to 1000 BPM).", FontAwesome.Solid.Expand);
                    return;
                }

                if (perBar < 1 || perBar > 32 || perBar != Math.Floor(perBar))
                {
                    ForgeNotify.Info(notifications, $"A bar of {tp.TimeSignature.Numerator} beats can't hold beats {(factor > 1 ? "twice as long" : "half as long")}: the bars would move.", FontAwesome.Solid.Expand);
                    return;
                }
            }

            double from = 60000 / points[0].BeatLength;
            string before = gridKey();

            editorBeatmap.BeginChange();

            foreach (var tp in points)
            {
                tp.BeatLength *= factor;
                tp.TimeSignature = new TimeSignature((int)(tp.TimeSignature.Numerator / factor));
            }

            editorBeatmap.EndChange();
            ScoreSession.GridChangeKeepsNotes(before, gridKey());

            // The same grid lines: a quarter-beat grid of the old beat is an eighth of the new one.
            double division = beatDivisor.Value * factor;
            if (division == Math.Floor(division) && families.Any(f => f.Has((int)division)))
                beatDivisor.SetArbitraryDivisor((int)division);

            shownTempo = null;
            ForgeNotify.Info(notifications, $"The beat is now {(factor > 1 ? "twice as long" : "half as long")}: {from:0.##} → {from / factor:0.##} BPM. The notes stayed where they sound.", FontAwesome.Solid.Expand);
        }

        private void removeTempoChange()
        {
            var cpi = editorBeatmap.ControlPointInfo;
            var tp = currentTimingPoint(cpi.TimingPoints);

            if (tp == null || tp == cpi.TimingPoints[0])
            {
                ForgeNotify.Info(notifications, "The playhead is in the first tempo: it stays (move the playhead after a tempo change).", FontAwesome.Solid.Times);
                return;
            }

            editorBeatmap.BeginChange();
            var group = cpi.GroupAt(tp.Time);
            group?.Remove(tp);
            if (group != null && group.ControlPoints.Count == 0) cpi.RemoveGroup(group);
            editorBeatmap.EndChange();
            shownTempo = null;
        }

        private string gridKey() => KanScore.GridKey(editorBeatmap.ControlPointInfo.TimingPoints.Select(p => new GridPoint { TimeMs = p.Time, BeatMs = p.BeatLength }));

        internal void moveFirstBeat(double deltaMs) => setFirstBeat(firstTimingPoint().Time + deltaMs);

        private void setFirstBeat(double ms)
        {
            ms = Math.Round(ms, 1);

            var cpi = editorBeatmap.ControlPointInfo;
            var tp = firstTimingPoint();
            if (Math.Abs(tp.Time - ms) < 0.05) return;

            // Keep the grid's first beat before any later tempo change.
            var next = cpi.TimingPoints.Skip(1).FirstOrDefault();
            if (next != null && ms >= next.Time) ms = next.Time - 1;

            editorBeatmap.BeginChange();

            var group = cpi.GroupAt(tp.Time);
            group?.Remove(tp);
            if (group != null && group.ControlPoints.Count == 0) cpi.RemoveGroup(group);

            cpi.Add(ms, tp);
            editorBeatmap.EndChange();
            shownTempo = null;
        }

        /// <summary>The next division in the shown grid family (G).</summary>
        public void NextDivision()
        {
            var family = shownFamily ?? families[0];
            int i = Array.IndexOf(family.Divisors, beatDivisor.Value);
            beatDivisor.SetArbitraryDivisor(family.Divisors[(i + 1) % family.Divisors.Length]);
        }

        /// <summary>The next grid family (Shift+G).</summary>
        public void NextFamily()
        {
            int i = shownFamily == null ? -1 : families.IndexOf(shownFamily);
            pickFamily(families[(i + 1) % families.Count]);
        }

        /// <summary>Tap tempo (T).</summary>
        public void Tap() => tap();

        private void tap()
        {
            double now = Time.Current;

            // A pause starts a new measurement.
            if (taps.Count > 0 && now - taps[^1] > 2000) taps.Clear();

            taps.Add(now);
            if (taps.Count > 8) taps.RemoveAt(0);
            if (taps.Count < 3) return;

            double interval = (taps[^1] - taps[0]) / (taps.Count - 1);
            setTempo(tp => tp.BeatLength = Math.Round(interval, 1));
            shownTempo = null;
        }

        // ------------------------------------------------------------------
        // Pieces
        // ------------------------------------------------------------------

        private void addFamily(FillFlowContainer flow, string name, string meaning, (int divisor, string meaning)[] divisors)
        {
            var divisions = new FillFlowContainer
            {
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Horizontal,
                Spacing = new Vector2(4, 0),
                Alpha = 0,
            };

            foreach (var (divisor, divisionMeaning) in divisors)
            {
                int d = divisor;
                var chip = new Chip($"1/{d}", $"Grid: {divisionMeaning}", () => beatDivisor.SetArbitraryDivisor(d));
                chips.Add((d, chip));
                divisions.Add(chip);
            }

            Family family = null!;
            family = new Family(divisors.Select(d => d.divisor).ToArray(), divisions, new Chip(name, meaning, () => pickFamily(family), 70));
            families.Add(family);
            flow.Add(family.Chip);
        }

        private void pickFamily(Family family)
        {
            showFamily(family);

            // Switching family moves the grid into it (its first division).
            if (!family.Has(beatDivisor.Value))
                beatDivisor.SetArbitraryDivisor(family.Divisors[0]);
        }

        private void showFamily(Family family)
        {
            shownFamily = family;
            foreach (var f in families)
                f.Divisions.Alpha = f == family ? 1 : 0;
        }

        private record Family(int[] Divisors, FillFlowContainer Divisions, Chip Chip)
        {
            public bool Has(int divisor) => Divisors.Contains(divisor);
        }

        private OsuSpriteText label(string text) => new OsuSpriteText
        {
            Text = text,
            Font = OsuFont.GetFont(size: 12, weight: FontWeight.SemiBold),
            Colour = colourProvider.Content2,
            Margin = new MarginPadding { Top = 5 },
        };

        private OsuTextBox box(float width, string tooltip) => new OsuTextBox
        {
            Width = width,
            Height = 26,
            CommitOnFocusLost = true,
            PlaceholderText = tooltip,
        };

        private ForgeButton button(IconUsage icon, string text, string tooltip, Action action, float width) => new ForgeButton(icon, text)
        {
            RelativeSizeAxes = Axes.None,
            Width = width,
            Height = 26,
            TooltipText = tooltip,
            Action = action,
        };

        /// <summary>A grid division chip (filled when it is the current grid).</summary>
        private partial class Chip : OsuClickableContainer
        {
            private readonly string text;
            private readonly string tooltip;
            private Box fill = null!;
            private OsuSpriteText label = null!;
            private bool on;

            [Resolved]
            private OverlayColourProvider colourProvider { get; set; } = null!;

            public Chip(string text, string tooltip, Action action, float width = 40)
            {
                this.text = text;
                this.tooltip = tooltip;
                Action = action;
                Size = new Vector2(width, 26); // before load: the clickable container sizes its content in its own load
            }

            public override LocalisableString TooltipText
            {
                get => tooltip;
                set { }
            }

            public bool On
            {
                set
                {
                    if (value == on || LoadState < LoadState.Ready) return;

                    on = value;
                    fill.FadeColour(on ? colourProvider.Highlight1 : colourProvider.Background4, 100);
                    label.FadeColour(on ? colourProvider.Background6 : colourProvider.Content1, 100);
                }
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                Masking = true;
                CornerRadius = 5;

                Children = new Drawable[]
                {
                    fill = new Box { RelativeSizeAxes = Axes.Both, Colour = colourProvider.Background4 },
                    label = new OsuSpriteText
                    {
                        Text = text,
                        Font = OsuFont.GetFont(size: 13, weight: FontWeight.Bold),
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Colour = colourProvider.Content1,
                    },
                };
            }

            protected override bool OnHover(HoverEvent e)
            {
                if (!on) fill.FadeColour(colourProvider.Background3, 100);
                return base.OnHover(e);
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                if (!on) fill.FadeColour(colourProvider.Background4, 100);
                base.OnHoverLost(e);
            }
        }
    }
}
