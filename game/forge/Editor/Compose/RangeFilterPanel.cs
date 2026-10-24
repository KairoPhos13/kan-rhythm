// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
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
using Kan.Core.Score;

namespace Kan.Forge.Editor.Compose
{
    /// <summary>
    /// Keep a pitch range (owner 2026-10-14): e.g. a level for a piano's
    /// right hand keeps G4–A8 and drops the rest; the removed notes stay
    /// listed by pitch so the keys that belong to the hand can be added back.
    /// </summary>
    internal partial class RangeFilterPanel : CompositeDrawable
    {
        private readonly PianoRoll roll;
        private readonly Action beforeChange;
        private readonly Action changed;

        private readonly Bindable<string> low = new Bindable<string>(@"G4");
        private readonly Bindable<string> high = new Bindable<string>(@"A8");
        private readonly List<ScoreNote> removed = new List<ScoreNote>();

        private FillFlowContainer removedList = null!;
        private OsuTextFlowContainer message = null!;

        [Resolved]
        private OverlayColourProvider colourProvider { get; set; } = null!;

        [Resolved]
        private OsuColour colours { get; set; } = null!;

        public RangeFilterPanel(PianoRoll roll, Action beforeChange, Action changed)
        {
            this.roll = roll;
            this.beforeChange = beforeChange;
            this.changed = changed;

            RelativeSizeAxes = Axes.X;
            AutoSizeAxes = Axes.Y;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            InternalChild = new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, 6),
                Children = new Drawable[]
                {
                    new OsuSpriteText { Text = @"Keep a pitch range", Font = OsuFont.GetFont(size: 15, weight: FontWeight.Bold) },
                    new OsuTextFlowContainer(t =>
                    {
                        t.Font = OsuFont.Style.Caption2;
                        t.Colour = colourProvider.Content2;
                    })
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Text = @"For example a piano's right hand: keep G4 to A8, then add back the keys that belong to it.",
                    },
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
                                new FormTextBox { Caption = @"lowest", Current = low },
                                Empty(),
                                new FormTextBox { Caption = @"highest", Current = high },
                            },
                        },
                    },
                    new ForgeButton(FontAwesome.Solid.Filter, @"remove notes outside")
                    {
                        Action = apply,
                        TooltipText = @"Remove every note below the lowest or above the highest pitch (undo brings them back).",
                    },
                    message = new OsuTextFlowContainer(t => t.Font = OsuFont.Style.Caption2)
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                    },
                    removedList = new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Vertical,
                        Spacing = new Vector2(0, 3),
                    },
                },
            };
        }

        private void apply()
        {
            if (!KanScore.TryParseNote(low.Value, out int lowMidi) || !KanScore.TryParseNote(high.Value, out int highMidi))
            {
                show(@"Type note names such as G4 and A8.", true);
                return;
            }

            if (!ScoreSession.Score.Notes.Any(n => n.Midi < Math.Min(lowMidi, highMidi) || n.Midi > Math.Max(lowMidi, highMidi)))
            {
                show(@"Every note is already inside that range.", false);
                return;
            }

            beforeChange();
            var gone = ScoreSession.Score.KeepRange(lowMidi, highMidi);

            removed.Clear();
            removed.AddRange(gone);
            roll.Selected.RemoveWhere(n => gone.Contains(n));
            roll.Invalidate();
            changed();

            show($"Removed {ForgeFormat.Plural(gone.Count, "note", "notes")} outside {KanScore.NoteName(Math.Min(lowMidi, highMidi))}–{KanScore.NoteName(Math.Max(lowMidi, highMidi))}. Add back what belongs:", false);
            rebuildList();
        }

        private void show(string text, bool error)
        {
            message.Text = text;
            message.Colour = error ? colours.Orange1 : colourProvider.Content2;
        }

        private void rebuildList()
        {
            removedList.Clear();
            if (removed.Count == 0) return;

            removedList.Add(new ForgeButton(FontAwesome.Solid.Undo, @"add all back")
            {
                Height = 28,
                Action = () => addBack(removed.ToList()),
            });

            foreach (var group in removed.GroupBy(n => n.Midi).OrderByDescending(g => g.Key))
            {
                var notes = group.ToList();

                removedList.Add(new Container
                {
                    RelativeSizeAxes = Axes.X,
                    Height = 28,
                    Children = new Drawable[]
                    {
                        new OsuSpriteText
                        {
                            Text = $"{KanScore.NoteName(group.Key)} × {notes.Count}",
                            Font = OsuFont.GetFont(size: 13, weight: FontWeight.SemiBold),
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                        },
                        new ForgeButton(FontAwesome.Solid.Plus, @"add back")
                        {
                            RelativeSizeAxes = Axes.None,
                            Width = 110,
                            Height = 26,
                            Anchor = Anchor.CentreRight,
                            Origin = Anchor.CentreRight,
                            TooltipText = $"Put every removed {KanScore.NoteName(group.Key)} back into the score",
                            Action = () => addBack(notes),
                        },
                    },
                });
            }
        }

        private void addBack(List<ScoreNote> notes)
        {
            beforeChange();
            ScoreSession.Score.Notes.AddRange(notes);
            ScoreSession.Score.Sort();
            removed.RemoveAll(notes.Contains);
            roll.Invalidate();
            changed();

            show(removed.Count == 0 ? @"Every removed note is back." : $"{ForgeFormat.Plural(removed.Count, "note", "notes")} still removed:", false);
            rebuildList();
        }
    }
}
