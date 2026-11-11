// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System.ComponentModel;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Game.Overlays.Settings;
using osu.Game.Screens.Play.PlayerSettings;
using osuTK.Graphics;
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
    /// </summary>
    public static class KanNotation
    {
        private static readonly string[] letters = { "C", "C♯", "D", "D♯", "E", "F", "F♯", "G", "G♯", "A", "A♯", "B" };
        private static readonly string[] solfege = { "Do", "Do♯", "Re", "Re♯", "Mi", "Fa", "Fa♯", "Sol", "Sol♯", "La", "La♯", "Si" };

        public static KanNoteNames Names = KanNoteNames.Letters;

        public static bool ColourByNote;

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

        /// <summary>The note's own colour (sharps between their neighbours on the wheel).</summary>
        public static Color4 Colour(int midi) => colours[pitchClass(midi)];

        // Twelve hues evenly around the wheel looked alike where the eye tells
        // hues apart least: E and F were both green, and close on the staff
        // (owner 2026-10-26). Chosen instead to be as far apart as they can be
        // in a perceptual space (OKLab), in rainbow order from C, with lightness
        // varying too, and clear of the unknown pitch's grey and the perfect's
        // white: every two notes are now at least twice as far apart as E and F were.
        private static readonly Color4[] colours =
        {
            Color4Extensions.FromHex(@"ff451d"), // C   red
            Color4Extensions.FromHex(@"ffbf78"), // C♯  peach
            Color4Extensions.FromHex(@"c68e15"), // D   amber
            Color4Extensions.FromHex(@"02fb0b"), // D♯  lime
            Color4Extensions.FromHex(@"30c05e"), // E   green
            Color4Extensions.FromHex(@"61fcc4"), // F   mint
            Color4Extensions.FromHex(@"74cbf0"), // F♯  sky
            Color4Extensions.FromHex(@"1897fd"), // G   blue
            Color4Extensions.FromHex(@"a66cff"), // G♯  violet
            Color4Extensions.FromHex(@"e2a6ff"), // A   lilac
            Color4Extensions.FromHex(@"f300ff"), // A♯  magenta
            Color4Extensions.FromHex(@"ff5eb0"), // B   pink
        };
    }

    /// <summary>
    /// The player loader's "Notation" group (fork F-16), set right before
    /// playing: names, colours and size of the notes. The instrument and its
    /// range moved to song select, under the level (fork F-29, owner
    /// 2026-11-11: found only by hovering at the right of this screen).
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
                new PlayerSliderBar<double>
                {
                    LabelText = @"Note size",
                    DisplayAsPercentage = true,
                    Current = config.GetBindable<double>(KanRulesetSetting.NoteSize),
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
