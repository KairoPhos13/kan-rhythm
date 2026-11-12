// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System.Collections.Generic;
using osu.Framework.Graphics.Sprites;
using osu.Game.Graphics;

namespace Kan.Ruleset.UI
{
    /// <summary>
    /// How big kan!'s own text draws (issue 19): one control, "Text size" in
    /// Settings → kan! and in the notation panel, measured against what the
    /// client already does.
    ///
    /// It composes with the client's own scaling and never re-applies it:
    /// text in menus sits inside osu!'s scaling (its UI scale reaches it
    /// through the hierarchy), so it takes the kan! factor alone; text in
    /// gameplay sits in the playfield's scaling container, which the fork
    /// deliberately leaves out of osu!'s UI scaling
    /// (Player.createGameplayComponents → ScalingContainer(ScalingMode.Gameplay)
    /// → applyUIScale: false), so gameplay takes osu!'s UI scale here to
    /// respect the player's choice. Window size still does its own thing in
    /// both cases.
    ///
    /// Fonts are cached by (size, weight, scale): "GetFont spells its weight
    /// anew on every call, every frame" (KanStaff), and the size can change
    /// while the game runs, so the cache is keyed by the scale in force.
    /// </summary>
    public static class KanTextScale
    {
        /// <summary>The player's text size (0.75 to 2; 1 = as designed), followed live.</summary>
        public static float Factor = 1;

        /// <summary>osu!'s own UI scale (Graphics → UI scaling), followed live; gameplay applies it here.</summary>
        public static float OsuUiScale = 1;

        /// <summary>A font for a gameplay surface (the staff, the judgment words, the wait overlay).</summary>
        public static FontUsage GameplayFont(float size, FontWeight weight) => font(size, weight, Factor * OsuUiScale);

        /// <summary>A font for a menu surface (the instrument wedge, the notation panel): the client scales those already.</summary>
        public static FontUsage MenuFont(float size, FontWeight weight) => font(size, weight, Factor);

        /// <summary>The same, at the regular weight.</summary>
        public static FontUsage MenuFont(float size) => font(size, FontWeight.Regular, Factor);

        /// <summary>A font from one of osu!'s own styles (the wedge), grown by the kan! factor.</summary>
        public static FontUsage MenuFont(FontUsage style) => style.With(size: style.Size * Factor);

        private static readonly Dictionary<(float, FontWeight, float), FontUsage> fonts = new Dictionary<(float, FontWeight, float), FontUsage>();

        private static FontUsage font(float size, FontWeight weight, float scale)
        {
            var key = (size, weight, scale);
            if (!fonts.TryGetValue(key, out var font))
                fonts[key] = font = OsuFont.GetFont(size: size * scale, weight: weight);
            return font;
        }
    }
}
