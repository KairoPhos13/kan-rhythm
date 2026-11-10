// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Framework.Platform;
using osu.Framework.Screens;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Extensions;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Overlays;
using osu.Game.Screens.Edit;
using osuTK;
using osuTK.Input;
using Kan.Core.Score;
using Kan.Forge.Tracks;

namespace Kan.Forge.Editor.Compose
{
    /// <summary>
    /// The compose tab's controls: the toolbar, the empty state and the banner offering a newer transcription.
    /// </summary>
    public partial class KanComposeScreen : EditorScreen, IKeyBindingHandler<PlatformAction>, SectionPanel.ITools
    {
        private Drawable createToolbar(OverlayColourProvider colourProvider)
        {
            ForgeButton button(IconUsage icon, string text, string tooltip, Action action, float width) => new ForgeButton(icon, text)
            {
                RelativeSizeAxes = Axes.None,
                Width = width,
                Height = 30,
                TooltipText = tooltip,
                Action = action,
            };

            return new Container
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Padding = new MarginPadding(10),
                Children = new Drawable[]
                {
                    new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Full,
                        Spacing = new Vector2(6),
                        Children = new Drawable[]
                        {
                            selectTool = button(FontAwesome.Solid.MousePointer, @"select", @"Select, move and resize notes; drag a box to select several (S)", () => setTool(RollTool.Select), 96),
                            drawTool = button(FontAwesome.Solid.PencilAlt, @"draw", @"Click to add a note, drag to set its length (D)", () => setTool(RollTool.Draw), 90),
                            new Container { Width = 8, Height = 30 },
                            snapButton = button(FontAwesome.Solid.Magnet, @"snap", @"Snap notes to the beat grid (hold Shift while dragging to place freely) (N)", toggleSnap, 86),
                            new Container { Width = 8, Height = 30 },
                            companionToggle = button(FontAwesome.Solid.LayerGroup, @"companion", @"Hear and see the companion (the level audio) behind the notes (M)", () => monitor.CompanionOn.Toggle(), 128),
                            referenceToggle = button(FontAwesome.Solid.Microphone, @"reference", @"Hear and see the reference (the full performance) behind the notes (R)", () => monitor.ReferenceOn.Toggle(), 124),
                            notesToggle = button(FontAwesome.Solid.Music, @"notes", @"Hear the score itself, in time with the rest: to place notes by ear when there is no reference (P)", () => monitor.NotesOn.Toggle(), 96),
                            new Container { Width = 8, Height = 30 },
                            undoButton = button(FontAwesome.Solid.Undo, @"undo", @"Undo (Ctrl+Z)", undo, 88),
                            redoButton = button(FontAwesome.Solid.Redo, @"redo", @"Redo (Ctrl+Y)", redo, 88),
                            new Container { Width = 8, Height = 30 },
                            button(FontAwesome.Solid.SearchMinus, @"zoom out", @"Show more time (or Ctrl+wheel) (minus key)", () => roll.ZoomTime(2, roll.ViewStartMs + roll.ViewLengthMs / 2), 108),
                            button(FontAwesome.Solid.SearchPlus, @"zoom in", @"Show more detail (or Ctrl+wheel; Ctrl+Shift+wheel for pitch) (plus key)", () => roll.ZoomTime(0.5, roll.ViewStartMs + roll.ViewLengthMs / 2), 98),
                            button(FontAwesome.Solid.ExpandArrowsAlt, @"fit", @"Show the whole score (F)", roll.Fit, 72),
                            new Container { Width = 8, Height = 30 },
                            button(FontAwesome.Solid.Microphone, @"start from the transcription", @"Use the transcription of the reference (track tab → Transcribe) as the starting score", startFromTranscription, 230),
                            button(FontAwesome.Solid.Clone, @"remove repeats", @"Remove notes the transcriber heard twice: copies of a note at the same moment (Shift + U; Ctrl+Z brings them back)", removeRepeats, 150),
                            button(FontAwesome.Solid.Save, @"save", @"Save the score into the level (Ctrl+S). It is also saved when you leave this tab.", () => saveToLevel(true), 86),
                            button(FontAwesome.Solid.FileExport, @"export MIDI", @"Save the corrected score as MIDI next to a copy of the reference audio (and the automatic transcription), to check the transcriber against later", exportForValidation, 124),
                            button(FontAwesome.Solid.Keyboard, @"keys", @"Keyboard shortcuts (K)", () => shortcuts.ToggleVisibility(), 76),
                            gridTempo = new GridTempoBar { Margin = new MarginPadding { Top = 2 } },
                        },
                    },
                    status = new OsuSpriteText
                    {
                        Anchor = Anchor.BottomRight,
                        Origin = Anchor.BottomRight,
                        Font = OsuFont.Style.Caption2,
                        Colour = colourProvider.Foreground1,
                        Y = 4,
                    },
                },
            };
        }

        private Container createEmptyState(OverlayColourProvider colourProvider) => new Container
        {
            RelativeSizeAxes = Axes.Both,
            Alpha = 0,
            Child = new FillFlowContainer
            {
                Width = 460,
                AutoSizeAxes = Axes.Y,
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, 10),
                Children = new Drawable[]
                {
                    new OsuSpriteText { Text = @"No notes yet", Font = OsuFont.GetFont(size: 20, weight: FontWeight.Bold), Anchor = Anchor.TopCentre, Origin = Anchor.TopCentre },
                    new OsuTextFlowContainer(t =>
                    {
                        t.Font = OsuFont.Style.Caption1;
                        t.Colour = colourProvider.Content2;
                    })
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        TextAnchor = Anchor.TopCentre,
                        Text = "The score is what gameplay judges: each note's timing, pitch, duration, intensity and playing style.\n"
                               + "Start from the transcription of the reference (track tab → Transcribe), or pick the draw tool and click to add notes.",
                    },
                    new ForgeButton(FontAwesome.Solid.Microphone, @"start from the transcription")
                    {
                        RelativeSizeAxes = Axes.None,
                        Width = 260,
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.TopCentre,
                        Action = startFromTranscription,
                    },
                },
            },
        };

        /// <summary>A newer transcription than the one the score started from (track tab → Transcribe).</summary>
        private Container createNewerTranscriptionBanner(OverlayColourProvider colourProvider) => new Container
        {
            AutoSizeAxes = Axes.Both,
            Anchor = Anchor.TopCentre,
            Origin = Anchor.TopCentre,
            Margin = new MarginPadding { Top = 8 },
            Masking = true,
            CornerRadius = 6,
            Alpha = 0,
            Children = new Drawable[]
            {
                new Box { RelativeSizeAxes = Axes.Both, Colour = colourProvider.Background4 },
                new FillFlowContainer
                {
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Horizontal,
                    Spacing = new Vector2(10, 0),
                    Padding = new MarginPadding(8),
                    Children = new Drawable[]
                    {
                        new SpriteIcon { Icon = FontAwesome.Solid.Microphone, Size = new Vector2(14), Colour = ForgeRoles.Colour(colours, ForgeRole.Reference), Anchor = Anchor.CentreLeft, Origin = Anchor.CentreLeft },
                        newerTranscriptionText = new OsuSpriteText { Font = OsuFont.GetFont(size: 13, weight: FontWeight.SemiBold), Anchor = Anchor.CentreLeft, Origin = Anchor.CentreLeft },
                        new ForgeButton(FontAwesome.Solid.Sync, @"use it")
                        {
                            RelativeSizeAxes = Axes.None,
                            Width = 90,
                            Height = 28,
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            TooltipText = @"Replace the score with the new transcription (Ctrl+Z brings the current score back)",
                            Action = startFromTranscription,
                        },
                    },
                },
            },
        };

        private void setTool(RollTool tool)
        {
            roll.Tool = tool;
        }

        private void toggleSnap() => roll.Snap = !roll.Snap;
    }
}
