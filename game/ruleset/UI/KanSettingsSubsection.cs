// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Localisation;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Overlays.Settings;
using Kan.Ruleset.Configuration;

namespace Kan.Ruleset.UI
{
    /// <summary>
    /// Settings → kan!: the player's instrument and its range (owner
    /// 2026-10-15: any level on any instrument), the note size, the text size
    /// and the colour palette (issues 18, 19), and the instrument's tuning.
    /// Persisted locally.
    /// </summary>
    public partial class KanSettingsSubsection : RulesetSettingsSubsection
    {
        public KanSettingsSubsection(osu.Game.Rulesets.Ruleset ruleset)
            : base(ruleset)
        {
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            var config = (KanRulesetConfigManager)Config;

            var instrument = config.GetBindable<string>(KanRulesetSetting.PlayerInstrument);
            var lowest = config.GetBindable<int>(KanRulesetSetting.RangeLowest);
            var highest = config.GetBindable<int>(KanRulesetSetting.RangeHighest);

            // A new instrument starts from its usual range (the player can then adjust it).
            instrument.BindValueChanged(v =>
            {
                if (!string.IsNullOrEmpty(v.NewValue)) (lowest.Value, highest.Value) = Kan.Core.Play.RangeFit.DefaultRange(v.NewValue);
            });

            // The instrument's tuning: learnt while playing, or set by hand.
            if (Beatmaps.KanLevelFiles.ForgeRoot is { } root)
                Kan.Forge.Tracks.ForgeTiming.Load(root);

            Children = new Drawable[]
            {
                new SettingsItemV2(new InstrumentDropdown
                {
                    Caption = @"My instrument",
                    HintText = @"What you play. The game listens for it (one note at a time, or several), and any level is fitted to its range. ""As the level"": the instrument the level is made for, in its own range. Also set in song select, under the level.",
                    Current = instrument,
                }),
                new SettingsItemV2(new NoteDropdown
                {
                    Caption = @"Lowest note it plays",
                    Current = lowest,
                }),
                new SettingsItemV2(new NoteDropdown
                {
                    Caption = @"Highest note it plays",
                    HintText = @"A level outside this range plays an octave up or down, and notes still outside move by octaves into it.",
                    Current = highest,
                }),
                new SettingsItemV2(new FormCheckBox
                {
                    Caption = @"Only levels my instrument plays",
                    HintText = @"Song select lists the levels whose notes fit your instrument's range (as written, or an octave away). Search fits=no for the others. Off while the instrument is ""As the level"".",
                    Current = config.GetBindable<bool>(KanRulesetSetting.OnlyPlayableLevels),
                }),
                new SettingsItemV2(new FormSliderBar<double>
                {
                    Caption = @"Note size",
                    HintText = @"How big the notes, their names and the line are drawn (they also grow with the screen).",
                    Current = config.GetBindable<double>(KanRulesetSetting.NoteSize),
                    KeyboardStep = 0.05f,
                    DisplayAsPercentage = true,
                }),
                new SettingsItemV2(new FormSliderBar<double>
                {
                    Caption = @"Text size",
                    HintText = @"How big kan!'s own words draw: note names, judgment feedback, the wait overlay and the instrument line (issues 18, 19). It grows on top of the client's own UI scaling, never against it.",
                    Current = config.GetBindable<double>(KanRulesetSetting.TextSize),
                    KeyboardStep = 0.05f,
                    DisplayAsPercentage = true,
                }),
                new SettingsItemV2(new FormEnumDropdown<Kan.Core.Design.KanPaletteMode>
                {
                    Caption = @"Colour palette",
                    HintText = @"As the level designed it, or a colour-blind-safe palette that wins over the level: the notes, the trail, the hot line and the climax keep the level's lightness and backgrounds, but their colours are chosen so every one of them reads apart for that kind of vision.",
                    Current = config.GetBindable<Kan.Core.Design.KanPaletteMode>(KanRulesetSetting.Palette),
                }),
                new SettingsItemV2(new FormCheckBox
                {
                    Caption = @"Zoom the staff passage by passage",
                    HintText = @"Fast passages come in wider and slow ones narrower, easing between them. Off: one scale for the whole level, set by its fast notes.",
                    Current = config.GetBindable<bool>(KanRulesetSetting.StaffFollowsPassage),
                }),
                new SettingsItemV2(new FormCheckBox
                {
                    Caption = @"Follow my instrument's tuning",
                    HintText = @"Learn how far your instrument sits from A440 while you play, so a slightly sharp or flat instrument still plays the right notes.",
                    Current = Kan.Forge.Tracks.ForgeTiming.AUTO_TUNING,
                }),
                new SettingsItemV2(new FormSliderBar<double>
                {
                    Caption = @"Instrument tuning (cents)",
                    HintText = @"How sharp (+) or flat (−) your instrument is. 100 cents = a semitone. Turn off the option above to set it by hand.",
                    Current = Kan.Forge.Tracks.ForgeTiming.TUNING_CENTS,
                    KeyboardStep = 1,
                }),
            };
        }

        internal partial class InstrumentDropdown : FormDropdown<string>
        {
            public InstrumentDropdown()
            {
                Items = Configuration.KanPlayer.Choices;
            }

            protected override LocalisableString GenerateItemText(string item) => Configuration.KanPlayer.ChoiceLabel(item);
        }

        internal partial class NoteDropdown : FormDropdown<int>
        {
            public NoteDropdown()
            {
                Items = System.Linq.Enumerable.Range(21, 88).ToArray(); // the piano's keys, A0-C8
            }

            protected override LocalisableString GenerateItemText(int item) => KanNotation.Label(item);
        }
    }
}
