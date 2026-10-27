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
    /// KAN_DEV_SCREEN=replay plays back the level's newest local score;
    /// KAN_DEV_SCREEN=design / setup / compose / tracks / verify opens the editor on that tab
    /// (KAN_DEV_TRACKS=render: leaves it to render the level audio, back on its mixing desk);
    /// KAN_DEV_REPAIR="insert:10000:500;undo;cut:2000-3000" runs timeline
    /// repairs in compose, one every 12 s (stretch:from-to:length, redo too);
    /// KAN_DEV_PAUSE=12 pauses 12 s in and continues 2 s later;
    /// KAN_DEV_RESTART=12 restarts the level quickly 12 s in;
    /// KAN_DEV_SEEK="20:8" (with replay) seeks to 8 s, 20 s in.
    /// </summary>
    public partial class KanDevPlay : Component
    {
        public const string PLAY_ENV = @"KAN_DEV_PLAY";
        public const string SCREENSHOTS_ENV = @"KAN_DEV_SCREENSHOTS";
        public const string SCREEN_ENV = @"KAN_DEV_SCREEN";
        public const string MODS_ENV = @"KAN_DEV_MODS";
        public const string PAUSE_ENV = @"KAN_DEV_PAUSE";

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

        [Resolved]
        private osu.Game.Rulesets.IRulesetConfigCache configs { get; set; } = null!;


        /// <summary>
        /// What the mic timing listens to in a developer run: KAN_LIVE_INPUT,
        /// paced in real time — never the microphone (a test instance must not
        /// hear the person at the computer; no input = nothing heard).
        /// </summary>
        public static Kan.Forge.Play.ILiveSource? CalibrationInput()
        {
            if (!Enabled) return null;

            var since = System.Diagnostics.Stopwatch.StartNew();
            return new Kan.Forge.Play.FileSource(Environment.GetEnvironmentVariable(@"KAN_LIVE_INPUT") ?? string.Empty, () => since.Elapsed.TotalMilliseconds);
        }

        public static bool Enabled => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(PLAY_ENV));

        protected override void LoadComplete()
        {
            base.LoadComplete();

            // Mic measurements in a developer run hear KAN_LIVE_INPUT, never the microphone.
            if (Enabled) Kan.Forge.Play.MicCalibrator.DevInput = CalibrationInput;

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

            // KAN_DEV_NOTATION=solfege,colour: the notation settings.
            string notation = Environment.GetEnvironmentVariable(@"KAN_DEV_NOTATION") ?? string.Empty;

            if (notation.Length > 0 && configs.GetConfigFor(instance) is Kan.Ruleset.Configuration.KanRulesetConfigManager kanConfig)
            {
                if (notation.Contains(@"solfege")) kanConfig.SetValue(Kan.Ruleset.Configuration.KanRulesetSetting.NoteNames, Kan.Ruleset.UI.KanNoteNames.Solfege);
                kanConfig.SetValue(Kan.Ruleset.Configuration.KanRulesetSetting.ColourByNote, notation.Contains(@"colour"));
            }

            // KAN_DEV_INSTRUMENT=guitar:40-64: the player's instrument and range.
            string[] player = (Environment.GetEnvironmentVariable(@"KAN_DEV_INSTRUMENT") ?? string.Empty).Split(':', '-');

            if (player.Length == 3 && configs.GetConfigFor(instance) is Kan.Ruleset.Configuration.KanRulesetConfigManager playerConfig
                && int.TryParse(player[1], out int low) && int.TryParse(player[2], out int high))
            {
                playerConfig.SetValue(Kan.Ruleset.Configuration.KanRulesetSetting.PlayerInstrument, player[0]);
                playerConfig.SetValue(Kan.Ruleset.Configuration.KanRulesetSetting.RangeLowest, low);
                playerConfig.SetValue(Kan.Ruleset.Configuration.KanRulesetSetting.RangeHighest, high);
            }

            // KAN_DEV_PRACTICE=2-3: the practice mod's sections.
            string[] range = (Environment.GetEnvironmentVariable(@"KAN_DEV_PRACTICE") ?? string.Empty).Split('-');

            if (range.Length == 2 && mods.Value.OfType<Kan.Ruleset.Mods.KanModPractice>().FirstOrDefault() is { } practice
                && int.TryParse(range[0], out int from) && int.TryParse(range[1], out int to))
            {
                practice.FromSection.Value = from;
                practice.ToSection.Value = to;
            }

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
                scheduleSeeks();
                scheduleScreenshots();
                return;
            }

            if (Environment.GetEnvironmentVariable(SCREEN_ENV) is @"design" or @"setup" or @"compose" or @"tracks" or @"verify")
            {
                // The editor on its design tab (the level's design, owner 2026-10-15).
                game.PerformFromScreen(s => s.Push(new osu.Game.Screens.Edit.EditorLoader()), new[] { typeof(MainMenu) });
                Scheduler.AddDelayed(openDesignTab, 1000);
                scheduleScreenshots();
                return;
            }

            if (Environment.GetEnvironmentVariable(SCREEN_ENV) is @"select" or @"selectplay" or @"mictiming")
            {
                game.PerformFromScreen(s => s.Push(new SoloSongSelect()), new[] { typeof(MainMenu) });

                // KAN_DEV_SCREEN=selectplay: start the level from song select, through osu!'s own player loader.
                if (Environment.GetEnvironmentVariable(SCREEN_ENV) is @"selectplay" or @"mictiming")
                {
                    // KAN_DEV_SCREEN=mictiming: in the player loader, measure the mic delay (KAN_LIVE_INPUT answers the clicks).
                    if (Environment.GetEnvironmentVariable(SCREEN_ENV) == @"mictiming")
                    {
                        Scheduler.AddDelayed(() =>
                        {
                            var settings = game.ChildrenOfType<Kan.Ruleset.UI.KanMicSettings>().FirstOrDefault();
                            Logger.Log($"kan! dev play: mic timing ({(settings == null ? "no microphone group" : "measuring")})", level: LogLevel.Important);
                            settings?.DevMeasure();
                        }, 10000);
                    }

                    Scheduler.AddDelayed(() =>
                    {
                        // As pressing play: song select's own start (protected; developer tooling only).
                        if (game.ScreenStack.CurrentScreen is SongSelect select)
                        {
                            select.GetType().GetMethod(@"OnStart", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.Invoke(select, null);
                            Logger.Log(@"kan! dev play: started from song select", level: LogLevel.Important);
                        }
                    }, 5000);
                    scheduleScreenshots();
                }

                // KAN_DEV_SCROLL=mods: the selected mods' settings controls, shown on screen (for screenshots).
                if (Environment.GetEnvironmentVariable(@"KAN_DEV_SCROLL") == @"mods")
                {
                    Scheduler.AddDelayed(() =>
                    {
                        var controls = mods.Value.SelectMany(m => osu.Game.Configuration.SettingSourceExtensions.CreateSettingsControls(m)).ToArray();
                        game.Add(new osu.Framework.Graphics.Containers.Container
                        {
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            Width = 420,
                            AutoSizeAxes = osu.Framework.Graphics.Axes.Y,
                            Masking = true,
                            CornerRadius = 8,
                            Children = new Drawable[]
                            {
                                new osu.Framework.Graphics.Shapes.Box { RelativeSizeAxes = osu.Framework.Graphics.Axes.Both, Colour = osuTK.Graphics.Color4.Black, Alpha = 0.85f },
                                new osu.Framework.Graphics.Containers.FillFlowContainer
                                {
                                    RelativeSizeAxes = osu.Framework.Graphics.Axes.X,
                                    AutoSizeAxes = osu.Framework.Graphics.Axes.Y,
                                    Direction = osu.Framework.Graphics.Containers.FillDirection.Vertical,
                                    Padding = new osu.Framework.Graphics.MarginPadding(10),
                                    Children = controls,
                                },
                            },
                        });
                        Logger.Log($"kan! dev play: showing {controls.Length} mod settings", level: LogLevel.Important);
                    }, 4000);
                }

                Scheduler.AddDelayed(() => Logger.Log(@"kan! dev play: song select still open after 15 s", level: LogLevel.Important), 15000);
            }
            else if (mods.Value.OfType<osu.Game.Rulesets.Mods.ICreateReplayData>().FirstOrDefault() is { } auto)
            {
                // As song select does for autoplay: a replay player.
                game.PerformFromScreen(s => s.Push(new UnattendedPlayerLoader(() => new ReplayPlayer((b, m) => auto.CreateScoreFromReplayData(b, m)))), new[] { typeof(MainMenu) });
            }
            else
                game.PerformFromScreen(s => s.Push(new UnattendedPlayerLoader()), new[] { typeof(MainMenu) });

            scheduleScreenshots();
            schedulePause();
            scheduleRestart();
            schedulePracticeSteps();
        }

        /// <summary>KAN_DEV_PRACTICE_NAV="right@10,shift+right@20,retry@30": practice navigation at those seconds (as the keys; retry = osu!'s quick retry).</summary>
        private void schedulePracticeSteps()
        {
            foreach (string step in (Environment.GetEnvironmentVariable(@"KAN_DEV_PRACTICE_NAV") ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                string[] parts = step.Split('@');
                if (parts.Length != 2 || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds)) continue;

                string key = parts[0];

                if (key == @"retry")
                {
                    // osu!'s quick retry (owner 2026-10-17: after it practice no longer heard)
                    Scheduler.AddDelayed(() => (game.ScreenStack.CurrentScreen as Player)?.Restart(quickRestart: true), (seconds + 6) * 1000);
                    continue;
                }

                if (key.Length == 1)
                {
                    // w / s / l: the practice options
                    Scheduler.AddDelayed(() => Kan.Ruleset.Mods.KanModPractice.DevToggle(key[0]), (seconds + 6) * 1000);
                    continue;
                }

                bool resize = key.StartsWith(@"shift+", StringComparison.Ordinal);
                bool forward = key.EndsWith(@"right", StringComparison.Ordinal);
                Scheduler.AddDelayed(() => Kan.Ruleset.Mods.KanModPractice.DevStep(forward, resize), (seconds + 6) * 1000);
            }
        }

        /// <summary>KAN_DEV_PAUSE=12: pause the level that many seconds in and continue 2 s later (as after a window switch).</summary>
        private bool pauseAtWait;

        protected override void Update()
        {
            base.Update();

            if (!pauseAtWait || game.ScreenStack.CurrentScreen is not Player player) return;
            if (player.ChildrenOfType<Kan.Ruleset.UI.KanDrawableRuleset>().FirstOrDefault()?.State is not { Waiting: true }) return;

            pauseAtWait = false;
            Logger.Log($"kan! dev play: pausing at a wait ({player.Pause()})", level: LogLevel.Important);
            Scheduler.AddDelayed(() =>
            {
                Logger.Log(@"kan! dev play: continuing", level: LogLevel.Important);
                player.Resume();
            }, 2500);
        }

        /// <summary>KAN_DEV_SEEK="20:8,30:2": that many seconds in, seek the level to that second (a replay rewound; owner 2026-10-22: it crashed).</summary>
        private void scheduleSeeks()
        {
            foreach (string step in (Environment.GetEnvironmentVariable(@"KAN_DEV_SEEK") ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                string[] parts = step.Split(':');
                if (parts.Length != 2 || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double at)
                                      || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double to)) continue;

                Scheduler.AddDelayed(() =>
                {
                    if (game.ScreenStack.CurrentScreen is not Player player) return;

                    Logger.Log($"kan! dev play: seeking to {to} s", level: LogLevel.Important);
                    player.Seek(to * 1000);
                }, (at + 6) * 1000);
            }
        }

        private void schedulePause()
        {
            // KAN_DEV_PAUSE=wait: pause the first time the level waits for a note (wait mode).
            if (Environment.GetEnvironmentVariable(PAUSE_ENV) == @"wait")
            {
                pauseAtWait = true;
                return;
            }

            if (!double.TryParse(Environment.GetEnvironmentVariable(PAUSE_ENV), NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds)) return;

            Scheduler.AddDelayed(() =>
            {
                if (game.ScreenStack.CurrentScreen is not Player player) return;

                Logger.Log($"kan! dev play: pausing ({player.Pause()})", level: LogLevel.Important);
                Scheduler.AddDelayed(() =>
                {
                    Logger.Log(@"kan! dev play: continuing", level: LogLevel.Important);
                    player.Resume();
                }, 2000);
            }, (seconds + 6) * 1000);
        }

        /// <summary>KAN_DEV_RESTART=12: a quick restart 12 s into the level (as the retry key does).</summary>
        private void scheduleRestart()
        {
            if (!double.TryParse(Environment.GetEnvironmentVariable(@"KAN_DEV_RESTART"), NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds)) return;

            Scheduler.AddDelayed(() =>
            {
                if (game.ScreenStack.CurrentScreen is not Player player) return;

                Logger.Log($"kan! dev play: quick restart ({player.Restart(true)})", level: LogLevel.Important);
            }, (seconds + 6) * 1000);
        }

        private void scheduleScreenshots()
        {
            foreach (string part in (Environment.GetEnvironmentVariable(SCREENSHOTS_ENV) ?? string.Empty).Split(','))
            {
                if (!double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds)) continue;

                // The loader takes a few seconds before the level's clock starts.
                Scheduler.AddDelayed(() => game.ChildrenOfType<ScreenshotManager>().FirstOrDefault()?.TakeScreenshotAsync(false, false), (seconds + 6) * 1000);
            }
        }

        private int designTries;

        private void openDesignTab()
        {
            if (game.ScreenStack.CurrentScreen is osu.Game.Screens.Edit.Editor editor && editor.IsLoaded)
            {
                bool setup = Environment.GetEnvironmentVariable(SCREEN_ENV) == @"setup";
                bool compose = Environment.GetEnvironmentVariable(SCREEN_ENV) == @"compose";
                bool tracks = Environment.GetEnvironmentVariable(SCREEN_ENV) == @"tracks";
                bool verify = Environment.GetEnvironmentVariable(SCREEN_ENV) == @"verify";
                editor.Mode.Value = setup ? osu.Game.Screens.Edit.EditorScreenMode.SongSetup
                    : compose ? osu.Game.Screens.Edit.EditorScreenMode.Compose
                    : tracks ? osu.Game.Screens.Edit.EditorScreenMode.Tracks
                    : verify ? osu.Game.Screens.Edit.EditorScreenMode.Verify
                    : osu.Game.Screens.Edit.EditorScreenMode.Design;
                Logger.Log($"kan! dev play: editor on the {editor.Mode.Value} tab", level: LogLevel.Important);

                if (compose)
                {
                    string[] steps = (Environment.GetEnvironmentVariable(@"KAN_DEV_REPAIR") ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                    for (int i = 0; i < steps.Length; i++)
                    {
                        string step = steps[i];
                        Scheduler.AddDelayed(() => editor.ChildrenOfType<Kan.Forge.Editor.Compose.KanComposeScreen>().FirstOrDefault()?.DevRepair(step), 6000 + i * 12000);
                    }
                }

                // KAN_DEV_TRACKS=render: leave the track tab (the level audio is rendered
                // if the mix changed) and come back to its mixing desk.
                if (tracks && Environment.GetEnvironmentVariable(@"KAN_DEV_TRACKS") == @"render")
                {
                    Scheduler.AddDelayed(() => editor.Mode.Value = osu.Game.Screens.Edit.EditorScreenMode.Compose, 3000);
                    Scheduler.AddDelayed(() =>
                    {
                        editor.Mode.Value = osu.Game.Screens.Edit.EditorScreenMode.Tracks;
                        Scheduler.AddDelayed(() => editor.ChildrenOfType<Kan.Forge.Editor.KanTracksScreen>().FirstOrDefault()?.ShowMixView(), 1000);
                    }, 20000);
                }

                // KAN_DEV_SCROLL=credits: bring that setup section into view (for screenshots).
                if (setup && Environment.GetEnvironmentVariable(@"KAN_DEV_SCROLL") == @"credits")
                {
                    Scheduler.AddDelayed(() =>
                    {
                        var section = editor.ChildrenOfType<Kan.Forge.Editor.KanCreditsSection>().FirstOrDefault();
                        section?.FindClosestParent<osu.Game.Graphics.Containers.OsuScrollContainer>()?.ScrollIntoView(section);
                    }, 1500);
                }
                return;
            }

            if (++designTries < 30)
                Scheduler.AddDelayed(openDesignTab, 1000);
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
