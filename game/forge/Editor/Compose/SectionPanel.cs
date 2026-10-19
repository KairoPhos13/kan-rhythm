// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Overlays;
using osuTK;
using Kan.Forge.Pipeline;
using Kan.Forge.Tracks;

namespace Kan.Forge.Editor.Compose
{
    /// <summary>
    /// Work on one section of the score at a time (owner 2026-10-14): set it
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
        }

        private readonly ITools tools;
        private readonly Action changed;

        private OsuSpriteText range = null!;
        private ForgeButton record = null!;
        private ForgeButton retranscribe = null!;
        private OsuSpriteText recordLabel = null!;

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
        }
    }
}
