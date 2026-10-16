// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Kan.Forge.Design
{
    /// <summary>
    /// A level's colours (owner 2026-10-15: colours and visual impact per
    /// level). The judgement colours stay the same in every palette — great
    /// is gold, ok green, meh blue — so a player reads them anywhere; a
    /// palette sets the notes, the background, the trail and the hot line.
    /// Colours are hex strings (RRGGBB), so the editor and the game share them.
    /// </summary>
    public sealed class KanPalette
    {
        public const string DEFAULT = @"sakura";

        public string Name { get; init; } = DEFAULT;

        public string Title { get; init; } = @"Sakura";

        /// <summary>Notes still to play.</summary>
        public string Note { get; init; } = @"ff66aa";

        /// <summary>A second note colour: the lighter top of a note's gradient.</summary>
        public string NoteLight { get; init; } = @"ffb3d4";

        /// <summary>The background's dark (bottom) and lit (top, as the music blooms).</summary>
        public string BackgroundDark { get; init; } = @"120e1a";

        public string BackgroundLit { get; init; } = @"2c2148";

        /// <summary>The player's pitch on a sounding note.</summary>
        public string Trail { get; init; } = @"b7ffcf";

        /// <summary>The now line at full heat, the sparks of streaks and milestones.</summary>
        public string Hot { get; init; } = @"ffd54f";

        /// <summary>Climax sections: the background glows with this.</summary>
        public string Climax { get; init; } = @"ff3d8b";

        public static readonly IReadOnlyList<KanPalette> Presets = new[]
        {
            new KanPalette(),
            new KanPalette
            {
                Name = @"ocean", Title = @"Ocean",
                Note = @"38c6ff", NoteLight = @"a6e8ff",
                BackgroundDark = @"07131f", BackgroundLit = @"0f3550",
                Trail = @"c9fff4", Hot = @"7df9ff", Climax = @"2f7bff",
            },
            new KanPalette
            {
                Name = @"ember", Title = @"Ember",
                Note = @"ff7a3d", NoteLight = @"ffc29e",
                BackgroundDark = @"140a08", BackgroundLit = @"3a1a12",
                Trail = @"fff1b8", Hot = @"ffcf4d", Climax = @"ff2d2d",
            },
            new KanPalette
            {
                Name = @"forest", Title = @"Forest",
                Note = @"7ee07e", NoteLight = @"c8f7c0",
                BackgroundDark = @"07140c", BackgroundLit = @"123d22",
                Trail = @"f2ffd6", Hot = @"e6ff6b", Climax = @"2fd27a",
            },
            new KanPalette
            {
                Name = @"aurora", Title = @"Aurora",
                Note = @"b388ff", NoteLight = @"e0ceff",
                BackgroundDark = @"0b0a1c", BackgroundLit = @"1f2a5a",
                Trail = @"b9fff0", Hot = @"6cffd8", Climax = @"9d4dff",
            },
        };

        public static KanPalette Get(string? name)
            => Presets.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) ?? Presets[0];
    }
}
