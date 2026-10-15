// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Globalization;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Logging;
using osu.Framework.Screens;
using osu.Framework.Testing;
using osu.Game;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Graphics;
using osu.Game.Rulesets;
using osu.Game.Screens.Menu;
using osu.Game.Screens.Play;

namespace Kan.Ruleset.Dev
{
    /// <summary>
    /// Developer tooling, inert unless KAN_DEV_PLAY is set: plays a level by
    /// itself so gameplay can be checked without anyone at the screen (run it
    /// on a virtual display with its own data folder). With KAN_LIVE_INPUT
    /// the "player" is a recording; KAN_DEV_SCREENSHOTS="20,45" saves
    /// screenshots that many seconds into the level.
    /// </summary>
    public partial class KanDevPlay : Component
    {
        public const string PLAY_ENV = @"KAN_DEV_PLAY";
        public const string SCREENSHOTS_ENV = @"KAN_DEV_SCREENSHOTS";

        [Resolved]
        private OsuGame game { get; set; } = null!;

        [Resolved]
        private BeatmapManager beatmaps { get; set; } = null!;

        [Resolved]
        private Bindable<WorkingBeatmap> beatmap { get; set; } = null!;

        [Resolved]
        private Bindable<RulesetInfo> ruleset { get; set; } = null!;

        [Resolved]
        private RulesetStore rulesets { get; set; } = null!;


        public static bool Enabled => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(PLAY_ENV));

        protected override void LoadComplete()
        {
            base.LoadComplete();
            Scheduler.AddDelayed(start, 8000);
        }

        private void start()
        {
            if (!Guid.TryParse(Environment.GetEnvironmentVariable(PLAY_ENV), out var setId))
            {
                Logger.Log($"kan! dev play: {PLAY_ENV} is not a beatmap set id", level: LogLevel.Important);
                return;
            }

            var set = beatmaps.QueryBeatmapSet(s => s.ID == setId)?.PerformRead(s => s.Detach());
            var info = set?.Beatmaps.FirstOrDefault();

            if (info == null)
            {
                Logger.Log($"kan! dev play: no level with set id {setId}", level: LogLevel.Important);
                return;
            }

            ruleset.Value = rulesets.GetRuleset(@"kan") ?? ruleset.Value;
            beatmap.Value = beatmaps.GetWorkingBeatmap(info);
            Logger.Log($"kan! dev play: starting {info}", level: LogLevel.Important);

            game.PerformFromScreen(s => s.Push(new UnattendedPlayerLoader()), new[] { typeof(MainMenu) });

            foreach (string part in (Environment.GetEnvironmentVariable(SCREENSHOTS_ENV) ?? string.Empty).Split(','))
            {
                if (!double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds)) continue;

                // The loader takes a few seconds before the level's clock starts.
                Scheduler.AddDelayed(() => game.ChildrenOfType<ScreenshotManager>().FirstOrDefault()?.TakeScreenshotAsync(false, false), (seconds + 6) * 1000);
            }
        }
    }

    /// <summary>Starts without waiting for the window to be focused (nobody is at a virtual display), as replays do.</summary>
    internal partial class UnattendedPlayerLoader : PlayerLoader
    {
        public UnattendedPlayerLoader()
            : base(() => new UnattendedPlayer())
        {
            WindowShouldBeActiveForGameplayStart = false;
        }
    }

    /// <summary>Keeps playing although the (virtual) window never has focus.</summary>
    internal partial class UnattendedPlayer : SoloPlayer
    {
        protected override bool PauseOnFocusLost => false;
    }
}
