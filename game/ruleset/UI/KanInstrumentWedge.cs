// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input.Events;
using osu.Framework.Threading;
using osu.Game.Beatmaps;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Overlays;
using osuTK;
using Kan.Ruleset.Configuration;

namespace Kan.Ruleset.UI
{
    /// <summary>
    /// Song select's instrument wedge (fork F-29), under the level's title and
    /// stars (owner 2026-11-11: the instrument and its range were chosen in
    /// the player loader's side panel, found only by hovering at the right;
    /// "it is more intuitive" where the level is chosen). Shows the player's
    /// instrument, its range and how the selected level fits it; a click
    /// opens the choices.
    /// </summary>
    public partial class KanInstrumentWedge : OsuClickableContainer, IHasPopover
    {
        private readonly Action refilter;

        [Resolved]
        private IBindable<WorkingBeatmap> beatmap { get; set; } = null!;

        private Bindable<string> instrument = null!;
        private Bindable<int> lowest = null!, highest = null!;
        private Bindable<bool> onlyPlayable = null!;

        private OsuSpriteText mine = null!;
        private OsuTextFlowContainer fit = null!;

        private ScheduledDelegate? pendingFit, pendingRefilter;

        public KanInstrumentWedge(Action refilter)
        {
            this.refilter = refilter;

            RelativeSizeAxes = Axes.X;
            AutoSizeAxes = Axes.Y;
            Action = this.ShowPopover;
            TooltipText = @"Choose your instrument and the notes it plays";
        }

        [BackgroundDependencyLoader]
        private void load(osu.Game.Rulesets.IRulesetConfigCache configs, OverlayColourProvider colours)
        {
            if (configs.GetConfigFor(new KanRuleset()) is not KanRulesetConfigManager config) return;

            // The text size follows the player's setting (issue 19).
            config.GetBindable<double>(KanRulesetSetting.TextSize)
                  .BindValueChanged(v => KanTextScale.Factor = (float)v.NewValue, true);

            KanPlayer.Bind(config);

            instrument = config.GetBindable<string>(KanRulesetSetting.PlayerInstrument);
            lowest = config.GetBindable<int>(KanRulesetSetting.RangeLowest);
            highest = config.GetBindable<int>(KanRulesetSetting.RangeHighest);
            onlyPlayable = config.GetBindable<bool>(KanRulesetSetting.OnlyPlayableLevels);

            Child = new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, 2),
                Children = new Drawable[]
                {
                    new FillFlowContainer
                    {
                        AutoSizeAxes = Axes.Both,
                        Direction = FillDirection.Horizontal,
                        Spacing = new Vector2(8, 0),
                        Children = new Drawable[]
                        {
                            new SpriteIcon
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Icon = FontAwesome.Solid.Music,
                                Size = new Vector2(14),
                                Colour = colours.Highlight1,
                            },
                            mine = new OsuSpriteText
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Font = KanTextScale.MenuFont(OsuFont.Style.Heading2),
                            },
                            new OsuSpriteText
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Text = @"Change",
                                Font = KanTextScale.MenuFont(OsuFont.Style.Caption1).With(weight: FontWeight.SemiBold),
                                Colour = colours.Highlight1,
                            },
                        },
                    },
                    fit = new OsuTextFlowContainer(t => t.Font = KanTextScale.MenuFont(OsuFont.Style.Caption1))
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Colour = colours.Content2,
                    },
                },
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            if (instrument == null) return;

            instrument.BindValueChanged(_ => choiceChanged());
            lowest.BindValueChanged(_ => choiceChanged());
            highest.BindValueChanged(_ => choiceChanged());
            onlyPlayable.BindValueChanged(_ => choiceChanged());

            // Reading the level's notes touches the disk: not for every level
            // passed while scrolling, only the one the selection rests on.
            beatmap.BindValueChanged(_ =>
            {
                pendingFit?.Cancel();
                pendingFit = Scheduler.AddDelayed(updateFit, 150);
            });

            updateMine();
            updateFit();
        }

        private void choiceChanged()
        {
            updateMine();
            updateFit();

            // Song select lists the levels the instrument plays: list them again.
            pendingRefilter?.Cancel();
            pendingRefilter = Scheduler.AddDelayed(refilter, 300);
        }

        private void updateMine() => mine.Text = KanPlayer.Instrument == null
            ? @"My instrument: as the level"
            : $"My instrument: {KanPlayer.ChoiceLabel(instrument.Value)} ({KanNotation.Label(KanPlayer.Lowest)}-{KanNotation.Label(KanPlayer.Highest)})";

        private void updateFit() => fit.Text = FitText(beatmap.Value?.BeatmapSetInfo);

        /// <summary>"This level (Ocarina): C5-A6. Plays an octave lower."</summary>
        public static string FitText(BeatmapSetInfo? set)
        {
            var score = set == null ? null : Beatmaps.KanLevelFiles.LoadScore(set);
            var notes = score?.Notes.Where(n => !n.PitchUnknown).Select(n => n.Midi).ToList();
            string written = Kan.Forge.Pipeline.ForgeJobs.ProfileLabel(score?.Instrument ?? @"keyboard");

            if (notes == null || notes.Count == 0)
                return KanPlayer.Instrument == null ? @"The game listens for the instrument each level is made for." : string.Empty;

            string level = $"{KanNotation.Label(notes.Min())}-{KanNotation.Label(notes.Max())}";

            if (KanPlayer.Instrument == null)
                return $"The game listens for the level's instrument ({written}); it plays {level}.";

            string how = KanPlayer.Describe(KanPlayer.FitOf(notes)) ?? @"Fits as it is";
            return $"This level ({written}): {level}. {how}.";
        }

        protected override bool OnHover(HoverEvent e)
        {
            Child.FadeTo(0.8f, 100);
            return base.OnHover(e);
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            Child.FadeTo(1, 100);
            base.OnHoverLost(e);
        }

        public Popover GetPopover() => new ChoicePopover(instrument, lowest, highest, onlyPlayable);

        /// <summary>The choices themselves, as in Settings → kan!.</summary>
        private partial class ChoicePopover : OsuPopover
        {
            private readonly Bindable<string> instrument;
            private readonly Bindable<int> lowest, highest;
            private readonly Bindable<bool> onlyPlayable;

            private Drawable lowestItem = null!, highestItem = null!;

            public ChoicePopover(Bindable<string> instrument, Bindable<int> lowest, Bindable<int> highest, Bindable<bool> onlyPlayable)
            {
                // Own copies: what this popover binds goes with it when it closes.
                this.instrument = instrument.GetBoundCopy();
                this.lowest = lowest.GetBoundCopy();
                this.highest = highest.GetBoundCopy();
                this.onlyPlayable = onlyPlayable.GetBoundCopy();
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                Child = new FillFlowContainer
                {
                    Width = 380,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(0, 6),
                    Children = new Drawable[]
                    {
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
                            HintText = @"A level outside this range plays an octave up or down.",
                            Current = highest,
                        },
                        new FormCheckBox
                        {
                            Caption = @"Only levels my instrument plays",
                            HintText = @"Search fits=no for the others.",
                            Current = onlyPlayable,
                        },
                    },
                };
            }

            protected override void LoadComplete()
            {
                base.LoadComplete();

                // A new instrument starts from its usual range (adjustable here).
                instrument.BindValueChanged(v =>
                {
                    // Only a real change: opening the choices keeps the range the player set.
                    if (v.OldValue != v.NewValue && !string.IsNullOrEmpty(v.NewValue)) (lowest.Value, highest.Value) = Kan.Core.Play.RangeFit.DefaultRange(v.NewValue);
                    lowestItem.Alpha = highestItem.Alpha = string.IsNullOrEmpty(v.NewValue) ? 0 : 1;
                }, true);
            }
        }
    }
}
