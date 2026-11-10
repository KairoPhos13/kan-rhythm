// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Overlays;
using osuTK;
using osuTK.Graphics;

namespace Kan.Forge.Editor
{
    /// <summary>
    /// One optional action of the track tab (demux / transcribe / render).
    /// Says what it will use (live, from the chips), runs on demand, and
    /// reports its own progress and last outcome in place; nothing scrolls
    /// away. Never disabled for a "missing step": nothing is mandatory; a
    /// run without its selection explains what to select instead.
    /// </summary>
    internal partial class KanActionCard : CompositeDrawable
    {
        private readonly IconUsage icon;
        private readonly string title;
        private readonly string buttonText;
        private readonly Color4 accent;
        private readonly Action run;

        /// <summary>False while another job runs (jobs are serialized).</summary>
        public readonly BindableBool Enabled = new BindableBool(true);

        [Resolved]
        private OsuColour colours { get; set; } = null!;

        [Resolved]
        private OverlayColourProvider colourProvider { get; set; } = null!;

        private OsuTextFlowContainer usage = null!;
        private ForgeButton button = null!;
        private Container progress = null!;
        private Box fill = null!;
        private TruncatingSpriteText phaseText = null!;
        private FillFlowContainer result = null!;
        private SpriteIcon resultIcon = null!;
        private OsuTextFlowContainer resultText = null!;
        private Box flashLayer = null!;

        private string? context;
        private Container pickerSlot = null!;

        /// <summary>Choose the tracks this action works on, inside the card.</summary>
        public void SetPicker(Drawable picker)
        {
            if (IsLoaded)
                pickerSlot.Child = picker;
            else
                Schedule(() => pickerSlot.Child = picker);
        }

        private readonly bool showButton;

        /// <param name="showButton">False for a status card (its work starts on its own).</param>
        public KanActionCard(IconUsage icon, string title, string buttonText, Color4 accent, Action run, bool showButton = true)
        {
            this.showButton = showButton;
            this.icon = icon;
            this.title = title;
            this.buttonText = buttonText;
            this.accent = accent;
            this.run = run;

            AutoSizeAxes = Axes.Y;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            InternalChild = new Container
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Masking = true,
                CornerRadius = 6,
                CornerExponent = FormControlBackground.CORNER_EXPONENT,
                Children = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both, Colour = colourProvider.Background5 },
                    new Box { RelativeSizeAxes = Axes.X, Height = 3, Colour = accent },
                    flashLayer = new Box { RelativeSizeAxes = Axes.Both, Colour = accent, Alpha = 0 },
                    new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Vertical,
                        Spacing = new Vector2(0, 8),
                        Padding = new MarginPadding { Horizontal = 12, Top = 14, Bottom = 12 },
                        Children = new Drawable[]
                        {
                            new FillFlowContainer
                            {
                                AutoSizeAxes = Axes.Both,
                                Direction = FillDirection.Horizontal,
                                Spacing = new Vector2(10, 0),
                                Children = new Drawable[]
                                {
                                    new Container
                                    {
                                        Size = new Vector2(32),
                                        Masking = true,
                                        CornerRadius = 16,
                                        Children = new Drawable[]
                                        {
                                            new Box { RelativeSizeAxes = Axes.Both, Colour = accent.Opacity(0.18f) },
                                            new SpriteIcon
                                            {
                                                Icon = icon,
                                                Size = new Vector2(14),
                                                Anchor = Anchor.Centre,
                                                Origin = Anchor.Centre,
                                                Colour = accent,
                                            },
                                        },
                                    },
                                    new FillFlowContainer
                                    {
                                        AutoSizeAxes = Axes.Both,
                                        Direction = FillDirection.Vertical,
                                        Anchor = Anchor.CentreLeft,
                                        Origin = Anchor.CentreLeft,
                                        Children = new Drawable[]
                                        {
                                            new OsuSpriteText { Text = title, Font = OsuFont.GetFont(size: 17, weight: FontWeight.Bold) },
                                            new OsuSpriteText { Text = showButton ? @"optional" : @"automatic", Font = OsuFont.Style.Caption2, Colour = colourProvider.Foreground1 },
                                        },
                                    },
                                },
                            },
                            new Container
                            {
                                RelativeSizeAxes = Axes.X,
                                Height = 36,
                                Child = usage = new OsuTextFlowContainer(t => t.Font = OsuFont.Style.Caption2)
                                {
                                    RelativeSizeAxes = Axes.X,
                                    AutoSizeAxes = Axes.Y,
                                },
                            },
                            pickerSlot = new Container
                            {
                                RelativeSizeAxes = Axes.X,
                                AutoSizeAxes = Axes.Y,
                            },
                            button = new ForgeButton(icon, buttonText, accent)
                            {
                                Alpha = showButton ? 1 : 0,
                                Action = () => run(),
                                Enabled = { BindTarget = Enabled },
                            },
                            progress = new Container
                            {
                                RelativeSizeAxes = Axes.X,
                                Height = 24,
                                Alpha = 0,
                                Children = new Drawable[]
                                {
                                    new Container
                                    {
                                        RelativeSizeAxes = Axes.X,
                                        Height = 6,
                                        Masking = true,
                                        CornerRadius = 3,
                                        Children = new Drawable[]
                                        {
                                            new Box { RelativeSizeAxes = Axes.Both, Colour = colourProvider.Background3 },
                                            fill = new Box
                                            {
                                                RelativeSizeAxes = Axes.Both,
                                                RelativePositionAxes = Axes.X,
                                                Width = 0,
                                                Colour = accent,
                                            },
                                        },
                                    },
                                    phaseText = new TruncatingSpriteText
                                    {
                                        Y = 10,
                                        Font = OsuFont.GetFont(size: 11, weight: FontWeight.SemiBold),
                                        Colour = colourProvider.Content2,
                                        RelativeSizeAxes = Axes.X,
                                    },
                                },
                            },
                            result = new FillFlowContainer
                            {
                                RelativeSizeAxes = Axes.X,
                                AutoSizeAxes = Axes.Y,
                                Direction = FillDirection.Horizontal,
                                Spacing = new Vector2(6, 0),
                                Alpha = 0,
                                Children = new Drawable[]
                                {
                                    resultIcon = new SpriteIcon { Size = new Vector2(11), Margin = new MarginPadding { Top = 2 } },
                                    resultText = new OsuTextFlowContainer(t => t.Font = OsuFont.Style.Caption2)
                                    {
                                        RelativeSizeAxes = Axes.X,
                                        AutoSizeAxes = Axes.Y,
                                        Width = 0.9f,
                                    },
                                },
                            },
                        },
                    },
                },
            };
        }

        /// <summary>What the action will use, derived live from the chips (ready = it has something to work on).</summary>
        public void SetUsage(string text, bool ready)
        {
            if (!IsLoaded)
            {
                Schedule(() => SetUsage(text, ready));
                return;
            }

            usage.Text = text;
            usage.FadeColour(ready ? colourProvider.Content1 : colourProvider.Foreground1, 200);
        }

        /// <summary>Which item the job is on (e.g. the track being demuxed, "1/3").</summary>
        public void SetContext(string? itemContext) => context = itemContext;

        /// <summary>Determinate progress (native reports).</summary>
        public void Progress(int percent, string phase)
        {
            showProgress();
            fill.ClearTransforms();
            fill.X = 0;
            fill.ResizeWidthTo(Math.Clamp(percent, 0, 100) / 100f, 200, Easing.OutQuint);
            phaseText.Text = context != null ? $"{context} · {phase} {percent}%" : $"{phase} {percent}%";
        }

        /// <summary>Indeterminate progress (jobs without native reports).</summary>
        public void Indeterminate(string phase)
        {
            showProgress();
            fill.ClearTransforms();
            fill.Width = 0.3f;
            fill.MoveToX(0).Then().MoveToX(0.7f, 800, Easing.InOutSine).Then().MoveToX(0, 800, Easing.InOutSine).Loop();
            phaseText.Text = context != null ? $"{context} · {phase}…" : $"{phase}…";
        }

        public void Complete(string message)
        {
            hideProgress();
            showResult(FontAwesome.Solid.CheckCircle, colours.GreenLight, message);
            flashLayer.FadeTo(0.2f).FadeOut(800, Easing.OutQuint);
        }

        public void Fail(string message)
        {
            hideProgress();
            showResult(FontAwesome.Solid.TimesCircle, colours.Red1, message);
        }

        /// <summary>A gentle "here's what to select" note (not an error, not blocking).</summary>
        public void Hint(string message)
        {
            showResult(FontAwesome.Solid.InfoCircle, colourProvider.Highlight1, message);
            result.FlashColour(Color4.White, 500);
        }

        private void showProgress()
        {
            result.FadeOut(100);
            progress.FadeIn(150);
        }

        private void hideProgress()
        {
            context = null;
            fill.ClearTransforms();
            fill.X = 0;
            fill.Width = 1;
            progress.Delay(600).FadeOut(300);
        }

        private void showResult(IconUsage resultIconUsage, Color4 colour, string message)
        {
            resultIcon.Icon = resultIconUsage;
            resultIcon.Colour = colour;
            resultText.Text = message;
            resultText.Colour = colourProvider.Content2;
            result.FadeIn(200);
        }
    }
}
