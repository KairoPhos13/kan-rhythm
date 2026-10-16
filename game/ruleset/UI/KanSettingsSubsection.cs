// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Localisation;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Overlays.Settings;
using Kan.Ruleset.Configuration;
using Kan.Ruleset.Instruments;

namespace Kan.Ruleset.UI
{
    /// <summary>
    /// v0 instrument selector (owner requirement 2026-10-13): choose an
    /// instrument tied to its category, persisted locally so the program
    /// remembers the last selection. Lives in Settings → Rulesets → kan!
    /// (pure ruleset-plugin surface, zero fork divergence). The final
    /// placement at play start (song select / play setup) and the variant
    /// selector (ocarina types) are tracked in game/TODO.md.
    /// </summary>
    public partial class KanSettingsSubsection : RulesetSettingsSubsection
    {
        private FormDropdown<KanInstrument> instrumentDropdown = null!;

        public KanSettingsSubsection(osu.Game.Rulesets.Ruleset ruleset)
            : base(ruleset)
        {
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            var config = (KanRulesetConfigManager)Config;

            var categoryDropdown = new KanCategoryDropdown
            {
                Caption = @"Category",
                Current = config.GetBindable<KanInstrumentCategory>(KanRulesetSetting.SelectedCategory),
            };

            instrumentDropdown = new KanInstrumentDropdown
            {
                Caption = @"Instrument",
                Current = config.GetBindable<KanInstrument>(KanRulesetSetting.SelectedInstrument),
            };

            categoryDropdown.Items = Enum.GetValues<KanInstrumentCategory>();

            // The instrument list is tied to the selected category.
            categoryDropdown.Current.BindValueChanged(change => applyCategoryFilter(change.NewValue), true);

            // The instrument's tuning (owner 2026-10-15): learnt while playing, or set by hand.
            if (Beatmaps.KanLevelFiles.ForgeRoot is { } root)
                Kan.Forge.Tracks.ForgeTiming.Load(root);

            var autoTuning = Kan.Forge.Tracks.ForgeTiming.AUTO_TUNING;

            Children = new Drawable[]
            {
                new SettingsItemV2(categoryDropdown),
                new SettingsItemV2(instrumentDropdown),
                new SettingsItemV2(new FormCheckBox
                {
                    Caption = @"Follow my instrument's tuning",
                    HintText = @"Learn how far your instrument sits from A440 while you play, so a slightly sharp or flat instrument still plays the right notes.",
                    Current = autoTuning,
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

        private void applyCategoryFilter(KanInstrumentCategory category)
        {
            instrumentDropdown.Items = KanInstrumentCatalog.InstrumentsIn(category);

            // Snap the selection to the category if needed; the new value is
            // persisted by the config manager automatically.
            if (KanInstrumentCatalog.CategoryOf(instrumentDropdown.Current.Value) != category)
                instrumentDropdown.Current.Value = KanInstrumentCatalog.InstrumentsIn(category)[0];
        }

        private partial class KanCategoryDropdown : FormDropdown<KanInstrumentCategory>
        {
            protected override LocalisableString GenerateItemText(KanInstrumentCategory item)
                => KanInstrumentCatalog.DisplayName(item);
        }

        private partial class KanInstrumentDropdown : FormDropdown<KanInstrument>
        {
            protected override LocalisableString GenerateItemText(KanInstrument item)
                => KanInstrumentCatalog.DisplayName(item);
        }
    }
}
