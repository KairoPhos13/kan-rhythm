// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Localisation;
using osu.Framework.Screens;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Overlays;
using osu.Game.Rulesets;
using osu.Game.Screens.Menu;
using osu.Game.Screens.Play;
using osuTK;
using Kan.Ruleset.Beatmaps;
using Kan.Ruleset.Configuration;
using Kan.Ruleset.UI;

namespace Kan.Ruleset.FirstRun
{
    /// <summary>The guide's first screen: what kan! is, and no pressure at all (issue 16).</summary>
    [LocalisableDescription(typeof(KanFirstRunStrings), nameof(KanFirstRunStrings.WelcomeTitle))]
    public partial class KanFirstRunWelcome : WizardScreen
    {
        [BackgroundDependencyLoader]
        private void load()
        {
            Content.Children = new Drawable[]
            {
                new OsuTextFlowContainer(t => t.Font = OsuFont.Default.With(size: CONTENT_FONT_SIZE))
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Text = @"kan! is a rhythm game you play on your own instrument. Notes scroll along a staff towards a line, and you play each one as it reaches it. The microphone is all it needs.",
                },
                new OsuTextFlowContainer(t => t.Font = OsuFont.Default.With(size: CONTENT_FONT_SIZE))
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Text = @"Grab your instrument. The next steps are choosing it, checking that kan! can hear you, a little calibration, and one short level: a few minutes in all, and none of it is a test. Nothing is recorded or sent anywhere.",
                },
                new OsuTextFlowContainer(t => t.Font = OsuFont.Default.With(size: CONTENT_FONT_SIZE))
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Colour = OverlayColourProvider.Content2,
                    Text = @"Everything here can wait for later: any step can be skipped, and the whole guide runs again whenever you want it (Settings > Rulesets > kan!).",
                },
            };
        }
    }

    /// <summary>
    /// What the player plays (issue 16): the same choices as the instrument
    /// wedge in song select and Settings > kan!, remembered from here on.
    /// </summary>
    [LocalisableDescription(typeof(KanFirstRunStrings), nameof(KanFirstRunStrings.InstrumentTitle))]
    public partial class KanFirstRunInstrument : WizardScreen
    {
        [Resolved]
        private osu.Game.Rulesets.IRulesetConfigCache configs { get; set; } = null!;

        private Bindable<string> instrument = null!;
        private Bindable<int> lowest = null!, highest = null!;
        private Drawable lowestItem = null!, highestItem = null!;

        [BackgroundDependencyLoader]
        private void load()
        {
            var intro = new OsuTextFlowContainer(t => t.Font = OsuFont.Default.With(size: CONTENT_FONT_SIZE))
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Text = @"What do you play? kan! listens for it, and any level is fitted to its range. Winds, strings, keys and voice all come through here. ""As the level"" listens for the instrument each level is made for.",
            };

            if (configs.GetConfigFor(new KanRuleset()) is not KanRulesetConfigManager config)
            {
                Content.Children = new Drawable[] { intro };
                return;
            }

            instrument = config.GetBindable<string>(KanRulesetSetting.PlayerInstrument);
            lowest = config.GetBindable<int>(KanRulesetSetting.RangeLowest);
            highest = config.GetBindable<int>(KanRulesetSetting.RangeHighest);

            Content.Children = new Drawable[]
            {
                intro,
                new KanSettingsSubsection.InstrumentDropdown
                {
                    Caption = @"My instrument",
                    HintText = @"What you play. Any level is fitted to its range. ""As the level"": the instrument the level is made for.",
                    Current = instrument,
                },
                lowestItem = new KanSettingsSubsection.NoteDropdown
                {
                    Caption = @"Lowest note it plays",
                    Current = lowest,
                },
                highestItem = new KanSettingsSubsection.NoteDropdown
                {
                    Caption = @"Highest note it plays",
                    HintText = @"A level outside this range plays an octave up or down. You can fine-tune this later in Settings > kan!.",
                    Current = highest,
                },
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            // A new instrument starts from its usual range (the player can then adjust it).
            instrument?.BindValueChanged(v =>
            {
                if (v.OldValue != v.NewValue && !string.IsNullOrEmpty(v.NewValue))
                    (lowest.Value, highest.Value) = Kan.Core.Play.RangeFit.DefaultRange(v.NewValue);

                if (lowestItem != null)
                    lowestItem.Alpha = highestItem.Alpha = string.IsNullOrEmpty(v.NewValue) ? 0 : 1;
            }, true);
        }
    }

    /// <summary>
    /// The first level (issue 16): a gentle landing on a starter level (the
    /// shortest one, Xicochi), degrading gracefully when there is none yet.
    /// </summary>
    [LocalisableDescription(typeof(KanFirstRunStrings), nameof(KanFirstRunStrings.LevelTitle))]
    public partial class KanFirstRunLevel : WizardScreen
    {
        [Resolved]
        private RealmAccess realm { get; set; } = null!;

        [Resolved]
        private BeatmapManager beatmaps { get; set; } = null!;

        [Resolved]
        private osu.Game.Rulesets.RulesetStore rulesets { get; set; } = null!;

        [Resolved]
        private Bindable<RulesetInfo> ruleset { get; set; } = null!;

        [Resolved]
        private Bindable<WorkingBeatmap> beatmap { get; set; } = null!;

        [Resolved(CanBeNull = true)]
        private osu.Game.OsuGame? game { get; set; }

        private OsuTextFlowContainer words = null!;
        private RoundedButton play = null!;

        private (BeatmapSetInfo Set, BeatmapInfo Info)? level;

        [BackgroundDependencyLoader]
        private void load()
        {
            Content.Children = new Drawable[]
            {
                words = new OsuTextFlowContainer(t => t.Font = OsuFont.Default.With(size: CONTENT_FONT_SIZE))
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                },
                new FillFlowContainer
                {
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Horizontal,
                    Spacing = new Vector2(10, 0),
                    Children = new Drawable[]
                    {
                        play = new RoundedButton
                        {
                            Width = 180,
                            Height = 36,
                            Text = @"Play it now",
                            Action = start,
                        },
                        new RoundedButton
                        {
                            Width = 180,
                            Height = 36,
                            Text = @"Maybe later",
                            Action = () => words.Text = laterText,
                        },
                    },
                },
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            level = findLevel();
            words.Text = level is { } found ? foundText(found) : noneText;
            play.Alpha = level == null ? 0 : 1;
        }

        private string laterText => @"Of course. Whenever you feel like playing, every level is in song select (the play button in the menu).";

        private string noneText => @"There is no level to play yet, and that is all right. kan! ships its own starter levels; you can also make one in kan!forge, or ask a friend for theirs. Song select is where they will be.";

        private string foundText((BeatmapSetInfo Set, BeatmapInfo Info) found)
            => $"Ready when you are: {found.Info.Metadata.Title} ({found.Info.Metadata.Artist}). "
               + (found.Info.Metadata.Source == KanStarterPack.STARTER_SOURCE
                   ? $"It is the shortest of kan!'s starter levels ({found.Info.Length / 1000:0} seconds), a gentle place to begin. "
                   : $"It runs {found.Info.Length / 1000:0} seconds, a gentle place to begin. ")
               + @"You can play it now, or leave it for later: it stays in song select.";

        /// <summary>
        /// The level to land on: a starter level (issue 15's pack, Xicochi
        /// first) and, failing that, any level with kan! notes; else none.
        /// </summary>
        private (BeatmapSetInfo Set, BeatmapInfo Info)? findLevel()
        {
            try
            {
                var sets = realm.Run(r => r.All<BeatmapSetInfo>().AsEnumerable()
                                           .Where(s => !s.DeletePending && s.Beatmaps.Count > 0)
                                           .Select(s => s.Detach())
                                           .ToList());

                // A level with kan! notes: that is what the game plays (one
                // without them would open on an empty staff).
                var playable = sets.SelectMany(s => s.Beatmaps.Select(b => (Set: s, Info: b)))
                                   .Where(x => KanLevelFiles.LoadScore(x.Set) is { Notes.Count: > 0 })
                                   .ToList();

                return playable.OrderBy(x => x.Info.Metadata.Title.Contains(@"xicochi", StringComparison.OrdinalIgnoreCase) ? 0
                                           : x.Info.Metadata.Source == KanStarterPack.STARTER_SOURCE ? 1 : 2)
                               .ThenBy(x => x.Info.Length)
                               .Cast<(BeatmapSetInfo Set, BeatmapInfo Info)?>()
                               .FirstOrDefault();
            }
            catch (Exception e)
            {
                osu.Framework.Logging.Logger.Log($"kan!: the first level could not be found: {e.Message}");
                return null;
            }
        }

        /// <summary>Land on the level: it opens in the player, ready to play.</summary>
        private void start()
        {
            if (level is not { } found || game == null) return;

            ruleset.Value = rulesets.GetRuleset(@"kan") ?? ruleset.Value;
            beatmap.Value = beatmaps.GetWorkingBeatmap(found.Info);
            osu.Framework.Logging.Logger.Log($"kan! first run: the first level is {found.Info.Metadata.Title}", level: osu.Framework.Logging.LogLevel.Important);

            // The guide steps aside; the level's own player loader comes up
            // (with its Microphone group, where everything set here can be
            // checked and changed once more).
            this.FindClosestParent<KanFirstRunOverlay>()?.Hide();
            game.PerformFromScreen(s => s.Push(new PlayerLoader(() => new SoloPlayer())), new[] { typeof(MainMenu) });
        }
    }

    /// <summary>How big the guide's panels draw.</summary>
    internal static class KanFirstRunLayout
    {
        /// <summary>A panel of the guide or the calibration screen: rounded, softly filled.</summary>
        public static Container Panel(OverlayColourProvider colours, params Drawable[] children) => new Container
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Masking = true,
            CornerRadius = 10,
            Children = new Drawable[]
            {
                new Box { RelativeSizeAxes = Axes.Both, Colour = colours.Background4 },
                new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(8, 0),
                    Padding = new MarginPadding(14),
                    Children = children,
                },
            },
        };

        /// <summary>A line of the guide's own words.</summary>
        public static OsuTextFlowContainer Words(OverlayColourProvider colours, string text, float size = 15) => new OsuTextFlowContainer(t => t.Font = OsuFont.GetFont(size: size))
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Colour = colours.Content1,
            Text = text,
        };
    }
}
