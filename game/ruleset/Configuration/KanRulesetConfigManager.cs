// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using osu.Game.Configuration;
using osu.Game.Rulesets.Configuration;

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
            // The player's instrument (owner 2026-10-15: any level on any
            // instrument); as the level until they choose.
            SetDefault(KanRulesetSetting.PlayerInstrument, string.Empty); // empty: as the level
            SetDefault(KanRulesetSetting.RangeLowest, 21, 12, 120);
            SetDefault(KanRulesetSetting.RangeHighest, 108, 12, 120);

            // Owner 2026-10-15: "circles and fonts must be bigger" (2K screen).
            SetDefault(KanRulesetSetting.NoteSize, 1.2, 0.8, 2.0, 0.05);
            SetDefault(KanRulesetSetting.NoteNames, Kan.Ruleset.UI.KanNoteNames.Letters);
            SetDefault(KanRulesetSetting.ColourByNote, false);
            SetDefault(KanRulesetSetting.OnlyPlayableLevels, true);
            SetDefault(KanRulesetSetting.StaffFollowsPassage, false);
        }
    }
}
