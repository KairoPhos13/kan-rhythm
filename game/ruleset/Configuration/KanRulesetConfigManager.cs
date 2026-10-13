// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using osu.Game.Configuration;
using osu.Game.Rulesets.Configuration;
using Kan.Ruleset.Instruments;

namespace Kan.Ruleset.Configuration
{
    /// <summary>
    /// Persists the kan! instrument selection in the local client database
    /// (client.realm, RulesetConfigManager / RealmRulesetSetting rows keyed by
    /// ruleset short name) — the program remembers the last selection across
    /// runs without any custom file handling.
    /// </summary>
    public class KanRulesetConfigManager : RulesetConfigManager<KanRulesetSetting>
    {
        public KanRulesetConfigManager(SettingsStore? store, osu.Game.Rulesets.RulesetInfo ruleset)
            : base(store, ruleset)
        {
        }

        protected override void InitialiseDefaults()
        {
            // Provisional defaults (owner has not specified a startup
            // instrument yet): the universal piano profile in "Keys".
            SetDefault(KanRulesetSetting.SelectedCategory, KanInstrumentCategory.Keys);
            SetDefault(KanRulesetSetting.SelectedInstrument, KanInstrument.Piano);
        }
    }
}
