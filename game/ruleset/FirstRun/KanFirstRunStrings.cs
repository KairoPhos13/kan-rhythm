// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using osu.Framework.Localisation;

namespace Kan.Ruleset.FirstRun
{
    /// <summary>
    /// The names of the first-run guide's and the calibration screen's steps
    /// (issues 16, 17). They are the screen headings and, through
    /// <c>LocalisableDescription</c>, the footer's "Next (…)" button text.
    /// The words on these screens are part of the design: calm, concrete,
    /// never scolding (calm by design, game/README.md).
    /// </summary>
    public static class KanFirstRunStrings
    {
        public static LocalisableString GuideTitle => @"kan!'s first steps";

        public static LocalisableString GuideDescription => @"A few gentle steps: your instrument, your microphone, and a first level. Nothing here is a test, and every step can wait for later.";

        public static LocalisableString WelcomeTitle => @"Welcome";

        public static LocalisableString InstrumentTitle => @"Your instrument";

        public static LocalisableString MicrophoneTitle => @"Your microphone";

        public static LocalisableString CalibrationTitle => @"Calibration";

        public static LocalisableString LevelTitle => @"Your first level";

        public static LocalisableString CalibrationOverlayTitle => @"Calibration";

        public static LocalisableString CalibrationOverlayDescription => @"Three quiet minutes of setup: the microphone's delay, the room's own sound, and your instrument's tuning. Each step can be skipped and run again whenever you like.";
    }
}
