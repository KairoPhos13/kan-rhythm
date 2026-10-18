// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System.ComponentModel;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
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
    /// optionally, its own colour — each of the twelve notes always the same
    /// hue around the colour wheel (C red, D orange, E yellow…), so a note is
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
        public static string Name(int midi) => Names switch
        {
            KanNoteNames.Solfege => $"{solfege[pitchClass(midi)]}{midi / 12 - 1}",
            KanNoteNames.None => string.Empty,
            _ => $"{letters[pitchClass(midi)]}{midi / 12 - 1}",
        };

        /// <summary>The name even when names are off (the line labels, the feedback words).</summary>
        public static string Label(int midi) => Names == KanNoteNames.Solfege
            ? $"{solfege[pitchClass(midi)]}{midi / 12 - 1}"
            : $"{letters[pitchClass(midi)]}{midi / 12 - 1}";

        /// <summary>Whether the note is a sharp (a black key): drawn with points, not round (owner 2026-10-17).</summary>
        public static bool IsSharp(int midi) => pitchClass(midi) is 1 or 3 or 6 or 8 or 10;

        /// <summary>The note's own colour: its place on the colour wheel (sharps between their neighbours).</summary>
        public static Color4 Colour(int midi)
        {
            float hue = pitchClass(midi) / 12f;
            return Color4.FromHsv(new osuTK.Vector4(hue, 0.62f, 1f, 1f));
        }
    }

    /// <summary>
    /// The player loader's "Instrument &amp; notation" group (fork F-16), set
    /// right before playing: how the level fits the player's instrument, and
    /// names, colours and size of the notes.
    /// </summary>
    public partial class KanNotationSettings : PlayerSettingsGroup
    {
        public KanNotationSettings()
            : base(@"Instrument & notation")
        {
        }

        [Resolved]
        private IBindable<osu.Game.Beatmaps.WorkingBeatmap> beatmap { get; set; } = null!;

        [BackgroundDependencyLoader]
        private void load(osu.Game.Rulesets.IRulesetConfigCache configs)
        {
            if (configs.GetConfigFor(new KanRuleset()) is not KanRulesetConfigManager config) return;

            Children = new Drawable[]
            {
                new osu.Game.Graphics.Containers.OsuTextFlowContainer(t => t.Font = osu.Game.Graphics.OsuFont.GetFont(size: 13))
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Text = fitText(),
                },
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
            };
        }

        /// <summary>"Ocarina (A4–F6). This level: C5–A6 — plays an octave lower."</summary>
        private string fitText()
        {
            string mine = $"{Kan.Forge.Pipeline.ForgeJobs.ProfileLabel(KanPlayer.Instrument ?? @"keyboard")} ({KanNotation.Label(KanPlayer.Lowest)}–{KanNotation.Label(KanPlayer.Highest)})";
            var notes = Beatmaps.KanLevelFiles.LoadScore(beatmap.Value.BeatmapSetInfo)?.Notes.Where(n => !n.PitchUnknown).Select(n => n.Midi).ToList();

            if (notes == null || notes.Count == 0)
                return $"Your instrument: {mine}.";

            var fit = KanPlayer.FitOf(notes);
            string level = $"{KanNotation.Label(notes.Min())}–{KanNotation.Label(notes.Max())}";
            return $"Your instrument: {mine}. This level: {level} — {KanPlayer.Describe(fit)?.ToLowerInvariant() ?? "fits as it is"}.";
        }
    }
}
