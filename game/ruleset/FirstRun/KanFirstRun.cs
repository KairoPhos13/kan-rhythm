// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Logging;
using osu.Game.Screens.Footer;
using osu.Game.Screens.Menu;
using Kan.Ruleset.Configuration;
using Kan.Ruleset.Dev;

namespace Kan.Ruleset.FirstRun
{
    /// <summary>
    /// kan!'s first-run guide (issue 16) and the calibration screen (issue 17),
    /// brought to the game the way the starter pack and the dev tooling are:
    /// a component added by the ruleset icon (<see cref="Beatmaps.KanLevelFiles"/>),
    /// with no change to the fork.
    ///
    /// Calm by design: the guide shows itself once, on a fresh install, after
    /// osu!'s own first-run setup has had its turn and the player is back at
    /// the menu; closing it at any point ends it for good (it never nags) and
    /// it runs again whenever the player asks (Settings &gt; Rulesets &gt; kan!).
    /// In a developer run it never shows by itself: KAN_DEV_SCREEN=firstrun or
    /// =calibrate opens exactly what is asked for (KAN_DEV_STEP picks a step).
    /// </summary>
    public partial class KanFirstRun : Component
    {
        [Resolved]
        private osu.Game.OsuGame game { get; set; } = null!;

        [Resolved]
        private osu.Game.Rulesets.IRulesetConfigCache configs { get; set; } = null!;

        /// <summary>The game's own footer: osu!'s first-run setup shows on it, one screen's overlay at a time.</summary>
        [Resolved(CanBeNull = true)]
        private ScreenFooter? footer { get; set; }

        private KanFirstRunOverlay? flow;
        private KanCalibrationOverlay? calibration;

        /// <summary>A show asked for while osu!'s own overlay was up.</summary>
        private Action? pending;

        protected override void LoadComplete()
        {
            base.LoadComplete();

            // Hidden until shown, inert while hidden: added to the game like
            // every kan! overlay (the MicTimingOverlay pattern).
            game.Add(flow = new KanFirstRunOverlay());
            game.Add(calibration = new KanCalibrationOverlay());

            flow.State.BindValueChanged(s =>
            {
                // The guide is over when the player leaves it, however they
                // leave it: closing it is skipping it, and nothing asks again.
                if (s.NewValue == Visibility.Hidden && !KanDevPlay.Enabled)
                    done();
            });

            Scheduler.Add(poll);
        }

        private void done()
        {
            if (configs.GetConfigFor(new KanRuleset()) is KanRulesetConfigManager config)
                config.GetBindable<bool>(KanRulesetSetting.FirstRunDone).Value = true;
        }

        private bool isDone => configs.GetConfigFor(new KanRuleset()) is KanRulesetConfigManager config
                               && config.GetBindable<bool>(KanRulesetSetting.FirstRunDone).Value;

        /// <summary>Once a second: a pending show gets its turn; a fresh install gets its guide.</summary>
        private void poll()
        {
            Scheduler.AddDelayed(poll, 1000);

            // osu!'s own first-run setup comes first (it is showing now).
            if (footer?.ActiveOverlay != null) return;

            if (pending is { } open)
            {
                pending = null;
                open();
                return;
            }

            if (KanDevPlay.Enabled || isDone) return;
            if (game.ScreenStack.CurrentScreen is not MainMenu) return;

            Logger.Log(@"kan! first run: showing the guide", level: LogLevel.Important);
            flow?.Show();
        }

        /// <summary>
        /// The guide, whenever wanted (Settings &gt; kan!, the dev tooling):
        /// <paramref name="step"/> opens one of its steps ("welcome",
        /// "instrument", "microphone", "calibration", "level", or a number).
        /// </summary>
        public void ShowFlow(string? step = null)
        {
            pending = () =>
            {
                Logger.Log(@"kan! first run: opening the guide", level: LogLevel.Important);
                flow?.ShowAt(step);
            };
        }

        /// <summary>The calibration screen on its own (Settings &gt; kan!, the dev tooling).</summary>
        public void ShowCalibration(string? step = null)
        {
            pending = () =>
            {
                Logger.Log(@"kan! calibration: opening the screen", level: LogLevel.Important);
                calibration?.Show();

                if (!string.IsNullOrEmpty(step))
                    Scheduler.AddDelayed(() => calibration?.DevRun(step), 2000);
            };
        }
    }
}
