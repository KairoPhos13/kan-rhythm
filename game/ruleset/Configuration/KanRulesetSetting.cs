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
        /// <summary>The player's instrument: a transcription profile ("ocarina", "keyboard"…), what the game listens for.</summary>
        PlayerInstrument,

        /// <summary>The lowest and highest notes the player's instrument plays (MIDI).</summary>
        RangeLowest,

        RangeHighest,

        /// <summary>How big the staff draws (notes, names, the line): 1 = sized for a 900-pixel-high screen, scaled to the real one.</summary>
        NoteSize,

        /// <summary>How notes are named on screen (<see cref="Kan.Ruleset.UI.KanNoteNames"/>).</summary>
        NoteNames,

        /// <summary>Each note in its own colour (C red, D orange…), whatever the palette.</summary>
        ColourByNote,

        /// <summary>Song select lists only the levels the player's instrument can play.</summary>
        OnlyPlayableLevels,
    }
}
