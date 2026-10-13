// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

namespace Kan.Ruleset.Configuration
{
    /// <summary>
    /// Persisted kan! ruleset settings (stored in the local client.realm via
    /// osu!lazer's RulesetConfigManager, so the selection is remembered across
    /// runs — owner requirement 2026-10-13).
    /// </summary>
    public enum KanRulesetSetting
    {
        SelectedCategory,
        SelectedInstrument,
    }
}
