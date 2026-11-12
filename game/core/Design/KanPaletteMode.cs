// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System.ComponentModel;

namespace Kan.Core.Design
{
    /// <summary>
    /// The player's colour palette (issue 18): as the level designed it, or
    /// one of the colour-blind-safe overrides. The player wins over the
    /// level: an override keeps each level's lightness and backgrounds (the
    /// design's mood) but moves the meaning-carrying colours where a
    /// colour-blind eye can tell them apart (<see cref="KanPalette.ForMode"/>).
    /// </summary>
    public enum KanPaletteMode
    {
        [Description("As the level")]
        AsTheLevel,

        [Description("Deuteranopia safe")]
        DeuteranopiaSafe,

        [Description("Protanopia safe")]
        ProtanopiaSafe,

        [Description("Tritanopia safe")]
        TritanopiaSafe,
    }

    public static class KanPaletteModes
    {
        /// <summary>The kind of vision each mode is designed for (as the level: whatever the level's palette does).</summary>
        public static KanVision Vision(this KanPaletteMode mode) => mode switch
        {
            KanPaletteMode.DeuteranopiaSafe => KanVision.Deuteranopia,
            KanPaletteMode.ProtanopiaSafe => KanVision.Protanopia,
            KanPaletteMode.TritanopiaSafe => KanVision.Tritanopia,
            _ => KanVision.Typical,
        };

        /// <summary>The mode behind a settings or developer-run word ("level", "deuteranopia"…), or null.</summary>
        public static KanPaletteMode? Parse(string word) => word.Trim().ToLowerInvariant() switch
        {
            @"level" or @"as the level" => KanPaletteMode.AsTheLevel,
            @"deuteranopia" or @"deutanopia" => KanPaletteMode.DeuteranopiaSafe,
            @"protanopia" => KanPaletteMode.ProtanopiaSafe,
            @"tritanopia" => KanPaletteMode.TritanopiaSafe,
            _ => null,
        };
    }
}
