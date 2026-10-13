// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Collections.Generic;

namespace Kan.Ruleset.Instruments
{
    /// <summary>
    /// Instrument categories (provisional taxonomy — owner direction
    /// 2026-10-12/13, see game/docs/instrument-modes.md §6 for the open
    /// questions). Ocarina lives in "winds/others" per the owner; piano is a
    /// universal "Keys" profile.
    /// </summary>
    public enum KanInstrumentCategory
    {
        Keys,
        WindsOthers,
    }

    /// <summary>
    /// MVP instrument set (owner, 2026-10-12): piano + ocarina first; bass,
    /// guitar and drums follow as the project gets traction.
    /// </summary>
    public enum KanInstrument
    {
        Piano,
        Ocarina,
    }

    /// <summary>
    /// The single source of truth tying instruments to their category (the
    /// instrument selector is category-tied) and to display names. Later this
    /// catalog maps each instrument to its instrument pack (judgment profile,
    /// kanfx effects preset, helper renderers — platform-architecture §5).
    /// </summary>
    public static class KanInstrumentCatalog
    {
        public static KanInstrumentCategory CategoryOf(KanInstrument instrument)
        {
            switch (instrument)
            {
                case KanInstrument.Piano:
                    return KanInstrumentCategory.Keys;

                case KanInstrument.Ocarina:
                    return KanInstrumentCategory.WindsOthers;

                default:
                    throw new ArgumentOutOfRangeException(nameof(instrument), instrument, "unknown instrument");
            }
        }

        public static IReadOnlyList<KanInstrument> InstrumentsIn(KanInstrumentCategory category)
        {
            var result = new List<KanInstrument>();

            foreach (var instrument in Enum.GetValues<KanInstrument>())
            {
                if (CategoryOf(instrument) == category)
                    result.Add(instrument);
            }

            return result;
        }

        public static string DisplayName(KanInstrumentCategory category)
        {
            switch (category)
            {
                case KanInstrumentCategory.Keys:
                    return @"Keys";

                case KanInstrumentCategory.WindsOthers:
                    return @"Winds/others";

                default:
                    return category.ToString();
            }
        }

        public static string DisplayName(KanInstrument instrument)
        {
            switch (instrument)
            {
                case KanInstrument.Piano:
                    return @"Piano";

                case KanInstrument.Ocarina:
                    return @"Ocarina";

                default:
                    return instrument.ToString();
            }
        }
    }
}
