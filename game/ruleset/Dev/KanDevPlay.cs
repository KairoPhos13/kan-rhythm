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
using osu.Game.Rulesets.Mods;
using osu.Game.Screens.Menu;
using osu.Game.Screens.Play;
using osu.Game.Screens.Select;

namespace Kan.Ruleset.Dev
{
    /// <summary>
    /// Developer tooling, inert unless KAN_DEV_PLAY is set: plays a level by
    /// itself so gameplay can be checked without anyone at the screen (run it
    /// on a virtual display with its own data folder). With KAN_LIVE_INPUT
    /// the "player" is a recording; KAN_DEV_SCREENSHOTS="20,45" saves
    /// screenshots that many seconds into the level. KAN_DEV_SCREEN=select
    /// opens song select on that level instead of playing it (the screen a
    /// player goes through; owner 2026-10-15: it crashed there).
    /// KAN_DEV_MODS="HT,NF" plays with those mods (acronyms);
    /// KAN_DEV_SCREEN=replay plays back the level's newest local score.
    /// </summary>
    public partial class KanDevPlay : Component
    {
        public const string PLAY_ENV = @"KAN_DEV_PLAY";
        public const string SCREENSHOTS_ENV = @"KAN_DEV_SCREENSHOTS";
        public const string SCREEN_ENV = @"KAN_DEV_SCREEN";
        public const string MODS_ENV = @"KAN_DEV_MODS";

        [Resolved]
        private Bindable<System.Collections.Generic.IReadOnlyList<osu.Game.Rulesets.Mods.Mod>> mods { get; set; } = null!;

        [Resolved]
        private RealmAccess realm { get; set; } = null!;

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

            string[] acronyms = (Environment.GetEnvironmentVariable(MODS_ENV) ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var instance = ruleset.Value.CreateInstance();
            mods.Value = acronyms.Select(a => instance.CreateModFromAcronym(a)).Where(m => m != null).Cast<osu.Game.Rulesets.Mods.Mod>().ToArray();
            if (acronyms.Length > 0)
                Logger.Log($"kan! dev play: mods {string.Join(",", mods.Value.Select(m => m.Acronym))}", level: LogLevel.Important);

            if (Environment.GetEnvironmentVariable(SCREEN_ENV) == @"replay")
            {
                var score = realm.Run(r => r.All<osu.Game.Scoring.ScoreInfo>().AsEnumerable()
                                            .Where(sc => sc.BeatmapInfo != null && sc.BeatmapInfo.ID == info.ID && !sc.DeletePending)
                                            .OrderByDescending(sc => sc.Date).FirstOrDefault()?.Detach());

                if (score == null)
                {
                    Logger.Log(@"kan! dev play: no local score to replay", level: LogLevel.Important);
                    return;
                }

                Logger.Log($"kan! dev play: replaying the score of {score.Date:u} ({score.Accuracy:P1})", level: LogLevel.Important);
                game.PresentScore(score, osu.Game.Screens.ScorePresentType.Gameplay);
                return;
            }

            if (Environment.GetEnvironmentVariable(SCREEN_ENV) == @"select")
            {
                game.PerformFromScreen(s => s.Push(new SoloSongSelect()), new[] { typeof(MainMenu) });
                Scheduler.AddDelayed(() => Logger.Log(@"kan! dev play: song select still open after 15 s", level: LogLevel.Important), 15000);
            }
            else if (mods.Value.OfType<osu.Game.Rulesets.Mods.ICreateReplayData>().FirstOrDefault() is { } auto)
            {
                // As song select does for autoplay: a replay player.
                game.PerformFromScreen(s => s.Push(new UnattendedPlayerLoader(() => new ReplayPlayer((b, m) => auto.CreateScoreFromReplayData(b, m)))), new[] { typeof(MainMenu) });
            }
            else
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
        public UnattendedPlayerLoader(Func<Player>? player = null)
            : base(player ?? (() => new UnattendedPlayer()))
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
