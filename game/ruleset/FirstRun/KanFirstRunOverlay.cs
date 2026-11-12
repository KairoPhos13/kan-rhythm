// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Framework.Screens;
using osu.Framework.Testing;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Overlays;
using osuTK;

namespace Kan.Ruleset.FirstRun
{
    /// <summary>
    /// The shell of kan!'s setup screens (issues 16 and 17): a card with one
    /// step at a time and its own Back / Next / Close buttons. The steps are
    /// osu!'s own first-run wizard screens (<see cref="WizardScreen"/>, with
    /// their transitions and scrolling) inside a ruleset overlay: the fork's
    /// wizard shell sits on the game's screen footer, which a ruleset cannot
    /// reach, so the shell is kan!'s own and the fork stays untouched.
    ///
    /// Calm by design: nothing here is a test, every step is skippable, and
    /// closing the card is a perfectly good way to finish it.
    /// </summary>
    public abstract partial class KanWizardOverlay : OsuFocusedOverlayContainer
    {
        [Cached]
        private readonly OverlayColourProvider colours = new OverlayColourProvider(OverlayColourScheme.Purple);

        private readonly List<Type> steps = new List<Type>();

        private readonly LocalisableString title;
        private readonly LocalisableString description;

        private ScreenStack stack = null!;
        private RoundedButton next = null!;

        /// <summary>Where the wizard is (0-based; -1 before it starts).</summary>
        protected int CurrentStep { get; private set; } = -1;

        /// <summary>The step being shown (null before the wizard starts).</summary>
        public WizardScreen? CurrentScreen => stack?.CurrentScreen as WizardScreen;

        /// <summary>Whether the wizard reached its last step (the guide's end).</summary>
        public bool Completed { get; private set; }

        protected KanWizardOverlay(LocalisableString title, LocalisableString description)
        {
            this.title = title;
            this.description = description;

            RelativeSizeAxes = Axes.Both;
        }

        protected void AddStep<T>()
            where T : WizardScreen
            => steps.Add(typeof(T));

        [BackgroundDependencyLoader]
        private void load()
        {
            Child = new GridContainer
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.Both,
                Padding = new MarginPadding { Horizontal = 80, Vertical = 30 },
                RowDimensions = new[]
                {
                    new Dimension(GridSizeMode.AutoSize),
                    new Dimension(),
                    new Dimension(GridSizeMode.AutoSize),
                },
                Content = new[]
                {
                    new Drawable[]
                    {
                        new FillFlowContainer
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Direction = FillDirection.Vertical,
                            Spacing = new Vector2(0, 4),
                            Children = new Drawable[]
                            {
                                new OsuSpriteText
                                {
                                    Text = title,
                                    Font = OsuFont.GetFont(size: 28, weight: FontWeight.Bold),
                                },
                                new OsuTextFlowContainer(t => t.Font = OsuFont.GetFont(size: 15))
                                {
                                    RelativeSizeAxes = Axes.X,
                                    AutoSizeAxes = Axes.Y,
                                    Colour = colours.Content2,
                                    Text = description,
                                },
                            },
                        },
                    },
                    new Drawable[]
                    {
                        new Container
                        {
                            RelativeSizeAxes = Axes.Both,
                            Masking = true,
                            CornerRadius = 14,
                            Margin = new MarginPadding { Vertical = 14 },
                            Children = new Drawable[]
                            {
                                new Box { RelativeSizeAxes = Axes.Both, Colour = colours.Background6 },
                                stack = new ScreenStack { RelativeSizeAxes = Axes.Both },
                            },
                        },
                    },
                    new Drawable[]
                    {
                        new FillFlowContainer
                        {
                            AutoSizeAxes = Axes.Both,
                            Direction = FillDirection.Horizontal,
                            Spacing = new Vector2(10, 0),
                            Children = new Drawable[]
                            {
                                new RoundedButton
                                {
                                    Width = 130,
                                    Height = 36,
                                    Text = @"Back",
                                    Action = goBack,
                                },
                                next = new RoundedButton
                                {
                                    Width = 220,
                                    Height = 36,
                                    Action = goNext,
                                },
                                new RoundedButton
                                {
                                    Width = 130,
                                    Height = 36,
                                    Text = @"Close",
                                    Action = Hide,
                                },
                            },
                        },
                    },
                },
            };
        }

        protected override void PopIn()
        {
            this.FadeIn(150);

            if (CurrentStep < 0)
                showFirstStep();
        }

        private void showFirstStep()
        {
            CurrentStep = 0;
            stack.Push((WizardScreen)Activator.CreateInstance(steps[0])!);
            updateButtons();
        }

        protected override void PopOut()
        {
            base.PopOut();
            this.FadeOut(150);

            // No step is left behind: running the wizard again starts it at
            // its first step (re-runnable, as everything here is).
            for (int guard = 0; guard < steps.Count + 2 && stack?.CurrentScreen != null; guard++)
                stack.Exit();

            CurrentStep = -1;
        }

        /// <summary>One step forward; past the last step the wizard is done.</summary>
        private void goNext()
        {
            if (CurrentStep + 1 < steps.Count)
            {
                CurrentStep++;
                stack.Push((WizardScreen)Activator.CreateInstance(steps[CurrentStep])!);
                updateButtons();
                return;
            }

            Completed = true;
            Hide();
        }

        /// <summary>One step back; at the first step it closes (closing is skipping, never a failure).</summary>
        private void goBack()
        {
            if (CurrentStep <= 0)
            {
                Hide();
                return;
            }

            CurrentStep--;
            stack.CurrentScreen?.Exit();
            updateButtons();
        }

        private void updateButtons()
        {
            bool last = CurrentStep >= steps.Count - 1;
            next.Text = last ? @"Finish" : $@"Next: {steps[CurrentStep + 1].GetLocalisableDescription()}";
        }

        /// <summary>Developer tooling: open the wizard at one of its steps (KAN_DEV_STEP).</summary>
        public void ShowAt(string? step, string[] names)
        {
            int index = 0;

            if (int.TryParse(step, out int number))
                index = Math.Clamp(number, 0, steps.Count - 1);
            else if (step != null)
                index = Math.Max(0, Array.IndexOf(names, step.ToLowerInvariant()));

            Show();

            while (CurrentStep >= 0 && CurrentStep < index)
                goNext();
        }
    }

    /// <summary>
    /// kan!'s first-run guide (issue 16): welcome, the instrument, the
    /// microphone, calibration (issue 17), the first level: one gentle path
    /// with no dead ends.
    /// </summary>
    public partial class KanFirstRunOverlay : KanWizardOverlay
    {
        /// <summary>The steps by name (KAN_DEV_STEP opens one of them).</summary>
        internal static readonly string[] STEP_NAMES = { @"welcome", @"instrument", @"microphone", @"calibration", @"level" };

        public KanFirstRunOverlay()
            : base(KanFirstRunStrings.GuideTitle, KanFirstRunStrings.GuideDescription)
        {
            AddStep<KanFirstRunWelcome>();
            AddStep<KanFirstRunInstrument>();
            AddStep<KanFirstRunMicrophone>();
            AddStep<KanCalibrationScreen>();
            AddStep<KanFirstRunLevel>();
        }

        /// <summary>Show the guide at one of its steps ("welcome", "instrument", "microphone", "calibration", "level", or a number).</summary>
        public void ShowAt(string? step) => ShowAt(step, STEP_NAMES);
    }

    /// <summary>
    /// The calibration screen on its own (issue 17), from Settings &gt; kan!:
    /// the same screen the first-run guide carries as a step, so there is one
    /// calibration to keep in step with the game.
    /// </summary>
    public partial class KanCalibrationOverlay : KanWizardOverlay
    {
        /// <summary>The calibration's steps by name (KAN_DEV_STEP runs one of them).</summary>
        internal static readonly string[] STEP_NAMES = { @"latency", @"noise", @"detune" };

        public KanCalibrationOverlay()
            : base(KanFirstRunStrings.CalibrationOverlayTitle, KanFirstRunStrings.CalibrationOverlayDescription)
        {
            AddStep<KanCalibrationScreen>();
        }

        /// <summary>Developer tooling: run one of the three steps (latency, noise, detune).</summary>
        public void DevRun(string? step)
        {
            if (string.IsNullOrEmpty(step)) return;

            if (CurrentScreen is KanCalibrationScreen screen)
                screen.DevRun(step);
            else
                osu.Framework.Logging.Logger.Log(@"kan! dev: the calibration screen is not ready", level: osu.Framework.Logging.LogLevel.Important);
        }
    }
}
