// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Overlays;
using osuTK;

namespace Kan.Forge.Editor.Compose
{
    /// <summary>
    /// The compose tab's keyboard shortcuts, shown over
    /// the piano roll with the "keys" button or the K key.
    /// </summary>
    internal partial class ShortcutsPanel : VisibilityContainer
    {
        /// <summary>Every shortcut, grouped: (keys, what they do). Headings have no keys.</summary>
        public static readonly (string keys, string meaning)[] SHORTCUTS =
        {
            (string.Empty, @"Playback"),
            (@"Space / C", @"play or pause"),
            (@"X", @"play from the start"),
            (@"Z / Home", @"go to the start"),
            (@"V / End", @"go to the end"),
            (@"← / →", @"back / forward one grid step (when no note is selected)"),
            (@"Page Up / Page Down", @"back / forward one bar"),
            (@"Ctrl + ← / →", @"previous / next note"),

            (string.Empty, @"Tools and view"),
            (@"S or 1", @"select tool"),
            (@"D or 2", @"draw tool"),
            (@"N", @"snap to the grid on / off"),
            (@"G / Shift + G", @"next grid division / next grid family"),
            (@"T", @"tap the tempo"),
            (@"M", @"companion on / off (hear and see it)"),
            (@"R", @"reference on / off (hear and see it)"),
            (@"P", @"notes on / off (hear the score itself)"),
            (@"F", @"fit the whole score"),
            (@"+ / -", @"zoom in / out"),
            (@"K", @"show / hide this list"),

            (string.Empty, @"Notes"),
            (@"Ctrl + A", @"select every note"),
            (@"U", @"select the notes whose pitch is unknown (?)"),
            (@"Shift + U", @"remove repeated notes (one sound transcribed twice)"),
            (@"Esc", @"clear the selection"),
            (@"Delete / Backspace", @"delete the selected notes"),
            (@"Ctrl + C / X / V", @"copy / cut / paste the selected notes (pasted at the playhead)"),
            (@"↑ / ↓", @"selected notes a semitone up / down (Shift: an octave)"),
            (@"← / →", @"selected notes one grid step earlier / later (Shift: 10 ms)"),
            (@"Ctrl + Z / Ctrl + Y", @"undo / redo"),
            (@"Ctrl + S", @"save"),

            (string.Empty, @"Section"),
            (@"[ / ]", @"section starts / ends at the playhead"),
            (@"L", @"section from the selected notes"),
            (@"Ctrl + R", @"record the section with the mic (again to stop)"),
        };

        [BackgroundDependencyLoader]
        private void load(OverlayColourProvider colourProvider)
        {
            AutoSizeAxes = Axes.Both;
            Anchor = Anchor.Centre;
            Origin = Anchor.Centre;
            Masking = true;
            CornerRadius = 8;

            var rows = new FillFlowContainer
            {
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, 3),
                Padding = new MarginPadding(16),
            };

            rows.Add(new OsuSpriteText { Text = @"Keyboard shortcuts", Font = OsuFont.GetFont(size: 18, weight: FontWeight.Bold) });

            foreach (var (keys, meaning) in SHORTCUTS)
            {
                if (keys.Length == 0)
                {
                    rows.Add(new OsuSpriteText
                    {
                        Text = meaning,
                        Font = OsuFont.GetFont(size: 14, weight: FontWeight.Bold),
                        Colour = colourProvider.Highlight1,
                        Margin = new MarginPadding { Top = 8 },
                    });
                    continue;
                }

                rows.Add(new FillFlowContainer
                {
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Horizontal,
                    Children = new Drawable[]
                    {
                        new OsuSpriteText { Text = keys, Width = 170, Font = OsuFont.GetFont(size: 13, weight: FontWeight.Bold) },
                        new OsuSpriteText { Text = meaning, Font = OsuFont.GetFont(size: 13), Colour = colourProvider.Content2 },
                    },
                });
            }

            Children = new Drawable[]
            {
                new Box { RelativeSizeAxes = Axes.Both, Colour = colourProvider.Background5, Alpha = 0.97f },
                rows,
            };
        }

        protected override void PopIn() => this.FadeIn(120);

        protected override void PopOut() => this.FadeOut(120);
    }
}
