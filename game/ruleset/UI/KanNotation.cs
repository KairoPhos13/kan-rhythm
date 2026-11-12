// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Game.Overlays.Settings;
using osu.Game.Screens.Play.PlayerSettings;
using osuTK.Graphics;
using Kan.Core.Design;
using Kan.Ruleset.Configuration;

namespace Kan.Ruleset.UI
{
    /// <summary>How notes are named on screen.</summary>
    public enum KanNoteNames
    {
        [Description("Letters (C D E)")]
        Letters,

        [Description("Do Re Mi")]
        Solfege,

        [Description("No names")]
        None,
    }

    /// <summary>
    /// How the staff writes a note (owner 2026-10-15: "notes as letters are
    /// easy to confuse"): its name as letters or as Do-Re-Mi, or none; and,
    /// optionally, its own colour: each of the twelve notes always the same
    /// colour, in rainbow order (C red, D amber, E green…), so a note is
    /// known at a glance, on any instrument. Set right before playing (the
    /// player loader's Notation group) or in the ruleset settings.
    ///
    /// The colours come in four palettes (issue 18): the designed rainbow
    /// and one per kind of colour vision, kept apart numerically
    /// (<see cref="KanNoteColours"/>). The same choice also overrides the
    /// level's palette (<see cref="KanPalette.ForMode"/>), so one control
    /// makes the whole screen readable; the player wins over the level.
    /// </summary>
    public static class KanNotation
    {
        private static readonly string[] letters = { "C", "C♯", "D", "D♯", "E", "F", "F♯", "G", "G♯", "A", "A♯", "B" };
        private static readonly string[] solfege = { "Do", "Do♯", "Re", "Re♯", "Mi", "Fa", "Fa♯", "Sol", "Sol♯", "La", "La♯", "Si" };

        public static KanNoteNames Names = KanNoteNames.Letters;

        public static bool ColourByNote;

        /// <summary>The palette the player chose: the level's own, or a colour-blind-safe one that wins over the level.</summary>
        public static KanPaletteMode Palette = KanPaletteMode.AsTheLevel;

        private static int pitchClass(int midi) => ((midi % 12) + 12) % 12;

        /// <summary>The note's name with its octave ("G5", "Sol5"), or empty when names are off.</summary>
        public static string Name(int midi) => Names == KanNoteNames.None ? string.Empty : Label(midi);

        /// <summary>The name even when names are off (the line labels, the feedback words).</summary>
        public static string Label(int midi) => midi is >= 0 and < 128
            ? (Names == KanNoteNames.Solfege ? solfege_names : letter_names)[midi]
            : spell(Names == KanNoteNames.Solfege ? solfege : letters, midi);

        // Spelled once: the staff names its notes and lines every frame, and a
        // new string each time was garbage at the display's rate (lag hunt 2026-10-19).
        private static readonly string[] letter_names = Enumerable.Range(0, 128).Select(m => spell(letters, m)).ToArray();
        private static readonly string[] solfege_names = Enumerable.Range(0, 128).Select(m => spell(solfege, m)).ToArray();

        private static string spell(string[] names, int midi) => $"{names[pitchClass(midi)]}{midi / 12 - 1}";

        /// <summary>Whether the note is a sharp (a black key): drawn with points, not round.</summary>
        public static bool IsSharp(int midi) => pitchClass(midi) is 1 or 3 or 6 or 8 or 10;

        /// <summary>The note's own colour (sharps between their neighbours on the wheel, in the palette the player chose).</summary>
        public static Color4 Colour(int midi) => paletteColours(Palette)[pitchClass(midi)];

        // The tables are parsed once per palette (this runs every frame). The
        // default table is the designed rainbow (C red, C♯ peach, D amber…),
        // chosen to be as far apart as they can be in a perceptual space
        // (OKLab), lightness included (owner 2026-10-26: E and F looked
        // alike); the safe tables say how that reads to a colour-blind eye
        // (KanNoteColours, KanColourBlindTest).
        private static readonly Dictionary<KanPaletteMode, Color4[]> parsedColours = new Dictionary<KanPaletteMode, Color4[]>();

        private static Color4[] paletteColours(KanPaletteMode mode)
        {
            if (!parsedColours.TryGetValue(mode, out var table))
                parsedColours[mode] = table = KanNoteColours.Table(mode).Select(h => Color4Extensions.FromHex(h)).ToArray();
            return table;
        }
    }

    /// <summary>
    /// The player loader's "Notation" group (fork F-16), set right before
    /// playing: names, colours, palette and size of the notes, and the text
    /// size (issues 18, 19). The instrument and its range moved to song
    /// select, under the level (fork F-29, owner 2026-11-11: found only by
    /// hovering at the right of this screen).
    /// </summary>
    public partial class KanNotationSettings : PlayerSettingsGroup
    {
        public KanNotationSettings()
            : base(@"Notation")
        {
        }

        [BackgroundDependencyLoader]
        private void load(osu.Game.Rulesets.IRulesetConfigCache configs)
        {
            if (configs.GetConfigFor(new KanRuleset()) is not KanRulesetConfigManager config) return;

            Children = new Drawable[]
            {
                new SettingsEnumDropdown<KanNoteNames>
                {
                    LabelText = @"Note names",
                    Current = config.GetBindable<KanNoteNames>(KanRulesetSetting.NoteNames),
                },
                new PlayerCheckbox
                {
                    LabelText = @"Colour by note",
                    Current = config.GetBindable<bool>(KanRulesetSetting.ColourByNote),
                },
                new SettingsEnumDropdown<KanPaletteMode>
                {
                    LabelText = @"Colour palette",
                    Current = config.GetBindable<KanPaletteMode>(KanRulesetSetting.Palette),
                },
                new PlayerSliderBar<double>
                {
                    LabelText = @"Note size",
                    DisplayAsPercentage = true,
                    Current = config.GetBindable<double>(KanRulesetSetting.NoteSize),
                },
                new PlayerSliderBar<double>
                {
                    LabelText = @"Text size",
                    DisplayAsPercentage = true,
                    Current = config.GetBindable<double>(KanRulesetSetting.TextSize),
                },
                new PlayerCheckbox
                {
                    LabelText = @"Zoom passage by passage",
                    Current = config.GetBindable<bool>(KanRulesetSetting.StaffFollowsPassage),
                },
            };
        }
    }
}
