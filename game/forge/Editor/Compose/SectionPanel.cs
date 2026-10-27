// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Overlays;
using osuTK;
using Kan.Forge.Pipeline;
using Kan.Forge.Tracks;

namespace Kan.Forge.Editor.Compose
{
    /// <summary>
    /// Work on one section of the score at a time: set it
    /// at the playhead or from the selected notes, then fill it by playing
    /// it into the microphone or by transcribing that part of the reference
    /// again. Only the notes inside the section are replaced (undoable).
    /// </summary>
    internal partial class SectionPanel : CompositeDrawable
    {
        /// <summary>The screen's section tools.</summary>
        public interface ITools
        {
            double SectionFromMs { get; }

            double SectionToMs { get; }

            bool HasSection { get; }

            bool Recording { get; }

            bool Busy { get; }

            void SectionStartHere();

            void SectionEndHere();

            void SectionFromSelection();

            void ToggleRecording();

            void RetranscribeFromReference();

            void ShowMicCalibration();

            /// <summary>The length the repairs put in, in beats at the playhead; NaN = <see cref="RepairLengthMs"/>.</summary>
            double RepairBeats { get; set; }

            /// <summary>The length typed (custom, ms).</summary>
            double RepairLengthMs { get; set; }

            /// <summary>The length in use (ms).</summary>
            double RepairLength { get; }

            /// <summary>What puts the next tempo change (at TempoMs) in step with the tempo at the playhead: + silence, − cut, at AtMs (the playhead); or null.</summary>
            (double TempoMs, double DeltaMs, double AtMs)? NextTempoSync { get; }

            void SyncWithNextTempo();

            void InsertSilenceHere();

            void CutSection();

            void StretchSection();

            void FitSectionToGrid();
        }

        private readonly ITools tools;
        private readonly Action changed;

        private OsuSpriteText range = null!;
        private ForgeButton record = null!;
        private ForgeButton retranscribe = null!;
        private OsuSpriteText recordLabel = null!;
        private OsuTextBox lengthBox = null!;
        private OsuSpriteText lengthText = null!, syncText = null!;
        private ForgeButton cut = null!, stretch = null!, fit = null!, insert = null!, sync = null!;
        private readonly System.Collections.Generic.List<(double Beats, ForgeButton Chip)> lengthChips = new System.Collections.Generic.List<(double, ForgeButton)>();
        private FillFlowContainer lengthFlow = null!;
        private int[]? shownDivisions;

        [Resolved]
        private osu.Game.Screens.Edit.BindableBeatDivisor beatDivisor { get; set; } = null!;

        /// <summary>
        /// The lengths offered follow the grid's family (owner 2026-10-22: "with
        /// triplets in the editor it could offer triplet lengths"): straight
        /// halves, triplet thirds, or the odd split in use.
        /// </summary>
        private static int[] divisionsFor(int divisor) => divisor % 3 == 0 ? new[] { 1, 3, 6, 12 }
            : divisor is 5 or 7 or 9 ? new[] { 1, divisor }
            : new[] { 1, 2, 4, 8, 16 };

        private void showLengths()
        {
            var divisions = divisionsFor(beatDivisor.Value);
            if (shownDivisions != null && shownDivisions.SequenceEqual(divisions)) return;

            shownDivisions = divisions;
            lengthFlow.Clear();
            lengthChips.Clear();

            foreach (int d in divisions)
                lengthFlow.Add(lengthChip(1.0 / d, d == 1 ? @"1 beat" : $"1/{d}"));

            // A length of the other family goes back to a beat.
            if (!double.IsNaN(tools.RepairBeats) && !lengthChips.Any(c => Math.Abs(c.Beats - tools.RepairBeats) < 1e-9))
                tools.RepairBeats = 1;
        }

        [Resolved]
        private OverlayColourProvider colourProvider { get; set; } = null!;

        [Resolved]
        private OsuColour colours { get; set; } = null!;

        public SectionPanel(ITools tools, Action changed)
        {
            this.tools = tools;
            this.changed = changed;

            RelativeSizeAxes = Axes.X;
            AutoSizeAxes = Axes.Y;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            lengthBox.OnCommit += (_, _) =>
            {
                if (double.TryParse(lengthBox.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double ms) && ms >= 1 && ms <= 60000)
                {
                    tools.RepairLengthMs = ms;
                    tools.RepairBeats = double.NaN;
                }
                else if (double.IsNaN(tools.RepairBeats))
                {
                    tools.RepairBeats = 1;
                    lengthBox.Text = string.Empty;
                }
            };
        }

        private ForgeButton lengthChip(double beats, string text)
        {
            var chip = new ForgeButton(FontAwesome.Solid.Drum, text)
            {
                RelativeSizeAxes = Axes.None,
                Width = beats >= 1 ? 76 : 56,
                Height = 26,
                TooltipText = beats >= 1 ? @"One beat of the tempo at the playhead" : $"{text} of a beat of the tempo at the playhead",
                Action = () =>
                {
                    tools.RepairBeats = beats;
                    lengthBox.Text = string.Empty;
                },
            };

            lengthChips.Add((beats, chip));
            return chip;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            ForgeButton small(IconUsage icon, string text, string tooltip, Action action) => new ForgeButton(icon, text)
            {
                Height = 28,
                TooltipText = tooltip,
                Action = action,
            };

            InternalChild = new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, 6),
                Children = new Drawable[]
                {
                    new OsuSpriteText { Text = @"Section", Font = OsuFont.GetFont(size: 15, weight: FontWeight.Bold) },
                    range = new OsuSpriteText { Font = OsuFont.GetFont(size: 13, weight: FontWeight.SemiBold) },
                    new GridContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        ColumnDimensions = new[] { new Dimension(), new Dimension(GridSizeMode.Absolute, 6), new Dimension() },
                        RowDimensions = new[] { new Dimension(GridSizeMode.AutoSize) },
                        Content = new[]
                        {
                            new Drawable[]
                            {
                                small(FontAwesome.Solid.StepBackward, @"start here", @"The section starts at the playhead", tools.SectionStartHere),
                                Empty(),
                                small(FontAwesome.Solid.StepForward, @"end here", @"The section ends at the playhead", tools.SectionEndHere),
                            },
                        },
                    },
                    small(FontAwesome.Solid.ObjectGroup, @"use selected notes", @"The section spans the selected notes", tools.SectionFromSelection),
                    new InstrumentDropdown(),
                    record = new ForgeButton(FontAwesome.Solid.Microphone, @"record with the mic", colours.Red1)
                    {
                        TooltipText = @"Plays from just before the section; play your part. The section's notes are replaced by what you played (undo brings them back).",
                        Action = tools.ToggleRecording,
                    },
                    recordLabel = new OsuSpriteText { Font = OsuFont.Style.Caption2, Colour = colours.Red1, Alpha = 0 },
                    retranscribe = new ForgeButton(FontAwesome.Solid.Sync, @"re-transcribe from the reference", colours.Yellow)
                    {
                        TooltipText = @"Transcribe that part of the reference again and use it for the section (undo brings the old notes back).",
                        Action = tools.RetranscribeFromReference,
                    },
                    new OsuTextFlowContainer(t =>
                    {
                        t.Font = OsuFont.Style.Caption2;
                        t.Colour = colourProvider.Content2;
                    })
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Text = @"Recorded or re-transcribed notes start as a draft: adjust them with the piano roll, or move them together to align them.",
                    },
                    new OsuSpriteText { Text = @"Repair the audio's timing", Font = OsuFont.GetFont(size: 15, weight: FontWeight.Bold), Margin = new MarginPadding { Top = 8 } },
                    new OsuTextFlowContainer(t =>
                    {
                        t.Font = OsuFont.Style.Caption2;
                        t.Colour = colourProvider.Content2;
                    })
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Text = @"When the recording drifts from the grid, bring the audio back to it. Every track changes together "
                               + @"(the companion and the reference), and the notes, the design's sections and the tempo changes after it move with them.",
                    },
                    new OsuSpriteText { Text = @"length", Font = OsuFont.GetFont(size: 12, weight: FontWeight.SemiBold), Colour = colourProvider.Content2 },
                    lengthFlow = new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Full,
                        Spacing = new Vector2(4),
                    },
                    new GridContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        ColumnDimensions = new[] { new Dimension(GridSizeMode.Absolute, 90), new Dimension(GridSizeMode.Absolute, 6), new Dimension() },
                        RowDimensions = new[] { new Dimension(GridSizeMode.AutoSize) },
                        Content = new[]
                        {
                            new Drawable[]
                            {
                                lengthBox = new OsuTextBox { RelativeSizeAxes = Axes.X, Height = 26, CommitOnFocusLost = true, PlaceholderText = @"custom ms" },
                                Empty(),
                                lengthText = new OsuSpriteText { Font = OsuFont.GetFont(size: 13, weight: FontWeight.SemiBold), Margin = new MarginPadding { Top = 5 } },
                            },
                        },
                    },
                    insert = small(FontAwesome.Solid.Plus, @"insert silence at the playhead",
                        @"Everything from the playhead on plays later by the length: for a part that came in too early", tools.InsertSilenceHere),
                    cut = small(FontAwesome.Solid.Cut, @"cut the section out",
                        @"The section is taken out of every track and what follows closes the gap: for a part that came in too late, or a pause that is too long. Its notes go too.", tools.CutSection),
                    stretch = small(FontAwesome.Solid.ArrowsAltH, @"play the section in the length",
                        @"The section plays faster or slower to last exactly the length, its pitch kept: for a part that sped up or slowed down", tools.StretchSection),
                    fit = small(FontAwesome.Solid.Ruler, @"fit the section to the grid",
                        @"The section plays faster or slower to last the nearest whole number of grid steps: set the section from a beat the music is on to where the next one should be", tools.FitSectionToGrid),
                    syncText = new OsuSpriteText { Font = OsuFont.Style.Caption2, Colour = colourProvider.Content2, Margin = new MarginPadding { Top = 4 } },
                    sync = small(FontAwesome.Solid.Link, @"sync with the next tempo",
                        @"The tempos are fitted in the timing tab: the next tempo change came in off the beats of the one at the playhead. "
                        + @"This puts in the silence (or cuts the bit) that brings it back on them — at the playhead: put it where that time fits best (a pause, a held note). "
                        + @"Everything after it moves with the music.", tools.SyncWithNextTempo),
                    new OsuSpriteText { Text = @"Timing", Font = OsuFont.GetFont(size: 15, weight: FontWeight.Bold), Margin = new MarginPadding { Top = 8 } },
                    new FormSliderBar<double>
                    {
                        Caption = @"mic delay (ms)",
                        HintText = @"How late notes played into the mic arrive: speakers, microphone and recognition together. "
                                   + @"Yours, not the song's (also set before playing a level, in the Microphone group). Section recordings are placed this much earlier, and plays judge your mic with it.",
                        Current = ForgeTiming.MIC_DELAY_MS,
                        KeyboardStep = 1,
                    },
                    small(FontAwesome.Solid.Stopwatch, @"measure the mic delay", @"Play a note on a few clicks: the delay is measured and saved (the same measurement as before playing a level)", tools.ShowMicCalibration),
                    new FormSliderBar<double>
                    {
                        Caption = @"transcriber delay (ms)",
                        HintText = @"How late the transcriber places notes it finds in the reference (slow starts, like an ocarina's breath, are recognised a bit late). "
                                   + @"Changing it moves every note in the score by the difference (undo brings them back), and applies to later transcriptions.",
                        Current = ForgeTiming.TRANSCRIBER_DELAY_MS,
                        KeyboardStep = 1,
                    },
                },
            };
        }

        protected override void Update()
        {
            base.Update();

            range.Text = tools.HasSection
                ? $"{ForgeFormat.Time(tools.SectionFromMs / 1000)} – {ForgeFormat.Time(tools.SectionToMs / 1000)} ({(tools.SectionToMs - tools.SectionFromMs) / 1000:0.0} s)"
                : @"none yet — set its start and end";
            range.Colour = tools.HasSection ? colourProvider.Content1 : colourProvider.Foreground1;

            record.Enabled.Value = tools.Recording || (!tools.Busy && tools.HasSection);
            retranscribe.Enabled.Value = !tools.Busy && !tools.Recording && tools.HasSection;
            recordLabel.Alpha = tools.Recording ? 1 : 0;
            recordLabel.Text = tools.Recording ? @"● recording — press again to stop early" : string.Empty;

            bool free = !tools.Busy && !tools.Recording;
            insert.Enabled.Value = free;
            cut.Enabled.Value = stretch.Enabled.Value = fit.Enabled.Value = free && tools.HasSection;

            showLengths();

            foreach (var (beats, chip) in lengthChips)
                chip.Alpha = Math.Abs(tools.RepairBeats - beats) < 1e-9 ? 1 : 0.55f;
            lengthBox.Alpha = double.IsNaN(tools.RepairBeats) ? 1 : 0.55f;
            lengthText.Text = $"= {tools.RepairLength:0} ms" + (double.IsNaN(tools.RepairBeats) ? string.Empty : @" at the playhead");

            var next = tools.NextTempoSync;
            sync.Enabled.Value = free && next != null;
            syncText.Text = next is { } n
                ? n.DeltaMs > 0
                    ? $"next tempo ({ForgeFormat.Time(n.TempoMs / 1000)}) is {n.DeltaMs:0} ms early: +{n.DeltaMs:0} ms at {ForgeFormat.Time(n.AtMs / 1000)}"
                    : $"next tempo ({ForgeFormat.Time(n.TempoMs / 1000)}) is {-n.DeltaMs:0} ms late: cut {-n.DeltaMs:0} ms at {ForgeFormat.Time(n.AtMs / 1000)}"
                : @"next tempo: none, or already on the beat";
        }
    }
}
