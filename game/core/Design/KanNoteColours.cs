// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System.Collections.Generic;

namespace Kan.Core.Design
{
    /// <summary>
    /// The twelve note colours ("colour by note", issue 18): one per pitch
    /// class, C to B. The default table is the designed rainbow (C red, D
    /// amber, E green…), tuned in OKLab so no two notes look alike. Each
    /// colour-blind-safe table gives the naturals one hue and the sharps
    /// another (blue against orange, green against blue, teal against rose),
    /// with a lightness ladder inside each family: for dichromats, who see
    /// one hue dimension, lightness is the second one that keeps the twelve
    /// apart. Sharp notes already carry two more cues (a spiked head, a "♯"
    /// in the name), so their hue family doubles up with their shape.
    ///
    /// Verified offline (KanColourBlindTest): in every table, no two notes
    /// are closer than 0.08 in OKLab under the vision it is made for (and
    /// under typical vision), and none of them comes near the unknown pitch's
    /// grey or the perfect's white.
    /// </summary>
    public static class KanNoteColours
    {
        // Pitch class order: C, C♯, D, D♯, E, F, F♯, G, G♯, A, A♯, B.
        private static readonly string[] as_the_level =
        {
            @"ff451d", @"ffbf78", @"c68e15", @"02fb0b", @"30c05e", @"61fcc4",
            @"74cbf0", @"1897fd", @"a66cff", @"e2a6ff", @"f300ff", @"ff5eb0",
        };

        private static readonly string[] deuteranopia_safe =
        {
            @"453081", @"ffc8b2", @"5d4a9d", @"fa9c88", @"7664ba", @"907fd8",
            @"cc7360", @"ab9bf6", @"9f4a39", @"c8b8ff", @"742313", @"e5d5ff",
        };

        private static readonly string[] protanopia_safe =
        {
            @"115100", @"c5e2ff", @"2e6b19", @"9ab6ff", @"498636", @"63a252",
            @"728bd7", @"7fbe6d", @"4c62aa", @"9bdc89", @"2a3c80", @"b8faa5",
        };

        private static readonly string[] tritanopia_safe =
        {
            @"005238", @"ffcce9", @"006c50", @"e7a1bd", @"268769", @"47a383",
            @"bb7893", @"65bf9f", @"8f516b", @"82ddbb", @"662b46", @"a0fbd8",
        };

        private static readonly string[][] tables = { as_the_level, deuteranopia_safe, protanopia_safe, tritanopia_safe };

        /// <summary>The note's colour for a pitch class (0 = C … 11 = B) in the palette the player chose.</summary>
        public static string Hex(int pitchClass, KanPaletteMode mode)
            => tables[(int)mode][((pitchClass % 12) + 12) % 12];

        /// <summary>One whole table, for settings previews and the accessibility checks.</summary>
        public static IReadOnlyList<string> Table(KanPaletteMode mode) => tables[(int)mode];
    }
}
