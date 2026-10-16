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

            // Owner 2026-10-15: more themes. The backgrounds stay dark enough for
            // the staff's light lines, names and trail to read on every one.
            new KanPalette
            {
                Name = @"holy", Title = @"Holy (white and gold)",
                Note = @"f5e6b8", NoteLight = @"ffffff",
                BackgroundDark = @"1c1912", BackgroundLit = @"5c5038",
                Trail = @"ffffff", Hot = @"ffd700", Climax = @"fff3c4",
            },
            new KanPalette
            {
                Name = @"space", Title = @"Space",
                Note = @"9fa8ff", NoteLight = @"e4e8ff",
                BackgroundDark = @"05060f", BackgroundLit = @"1b1f4a",
                Trail = @"ffffff", Hot = @"7af0ff", Climax = @"6a3cff",
            },
            new KanPalette
            {
                Name = @"neon", Title = @"Neon",
                Note = @"ff2bd6", NoteLight = @"ff9cf0",
                BackgroundDark = @"0a0014", BackgroundLit = @"2a0a4a",
                Trail = @"39ff14", Hot = @"00f0ff", Climax = @"ff2bd6",
            },
            new KanPalette
            {
                Name = @"cowboy", Title = @"Cowboy",
                Note = @"e0a458", NoteLight = @"f5d6a1",
                BackgroundDark = @"1f120a", BackgroundLit = @"6b3a1c",
                Trail = @"fff0c9", Hot = @"ff8c3a", Climax = @"c2410c",
            },
            new KanPalette
            {
                Name = @"cyberpunk", Title = @"Cyberpunk",
                Note = @"fcee0a", NoteLight = @"fff799",
                BackgroundDark = @"0b0d17", BackgroundLit = @"1f2140",
                Trail = @"02d7f2", Hot = @"ff2a6d", Climax = @"05d9e8",
            },
            new KanPalette
            {
                Name = @"solarpunk", Title = @"Solarpunk",
                Note = @"f2c14e", NoteLight = @"fbe7a1",
                BackgroundDark = @"0d1a12", BackgroundLit = @"2f5a3a",
                Trail = @"b8f2a1", Hot = @"9be564", Climax = @"f2c14e",
            },
            new KanPalette
            {
                Name = @"happy", Title = @"Happy",
                Note = @"ffd166", NoteLight = @"fff0c2",
                BackgroundDark = @"1a0f2e", BackgroundLit = @"5a2a6e",
                Trail = @"06d6a0", Hot = @"ef476f", Climax = @"118ab2",
            },
            new KanPalette
            {
                Name = @"nature", Title = @"Nature",
                Note = @"a3c96b", NoteLight = @"dcebc0",
                BackgroundDark = @"101a10", BackgroundLit = @"2e4a2a",
                Trail = @"fff6d5", Hot = @"e9c46a", Climax = @"6a994e",
            },
        };

        public static KanPalette Get(string? name)
            => Presets.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) ?? Presets[0];
    }
}
