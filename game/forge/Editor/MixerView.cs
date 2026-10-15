// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Overlays;
using osuTK;
using osuTK.Graphics;
using Kan.Forge.Tracks;

namespace Kan.Forge.Editor
{
    /// <summary>
    /// The mixing desk for the companion (owner naming 2026-10-14): the
    /// level audio — what plays under the player — is the mix of the
    /// companion tracks. One channel strip per track (companion or not,
    /// fader, solo/mute, level) and a master section to preview exactly the
    /// level audio and render it.
    /// </summary>
    internal partial class MixerView : CompositeDrawable
    {
        private readonly ForgeTrackSet set;
        private readonly Drawable renderCard;
        private readonly Action onChanged;

        [Resolved]
        private SetMonitor monitor { get; set; } = null!;

        [Resolved]
        private osu.Game.Screens.Edit.EditorClock? clock { get; set; }

        [Resolved]
        private OsuColour colours { get; set; } = null!;

        [Resolved]
        private OverlayColourProvider colourProvider { get; set; } = null!;

        private FillFlowContainer strips = null!;
        private string? drawnOrder;

        /// <param name="set">The set.</param>
        /// <param name="renderCard">The level audio status (owned by the screen).</param>
        /// <param name="onChanged">Something in the model changed (refresh/save).</param>
        public MixerView(ForgeTrackSet set, Drawable renderCard, Action onChanged)
        {
            this.set = set;
            this.renderCard = renderCard;
            this.onChanged = onChanged;

            RelativeSizeAxes = Axes.X;
            AutoSizeAxes = Axes.Y;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            InternalChild = new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, 10),
                Children = new Drawable[]
                {
                    createListenBanner(),
                    WindowBar.ForLevelAudio(set, monitor, onChanged),
                    createDesk(),
                },
            };
        }

        private Drawable createDesk()
        {
            return new GridContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                ColumnDimensions = new[]
                {
                    new Dimension(),
                    new Dimension(GridSizeMode.Absolute, 12),
                    new Dimension(GridSizeMode.Absolute, 280),
                },
                RowDimensions = new[] { new Dimension(GridSizeMode.AutoSize) },
                Content = new[]
                {
                    new Drawable[]
                    {
                        new OsuScrollContainer(Direction.Horizontal)
                        {
                            RelativeSizeAxes = Axes.X,
                            Height = 360,
                            ScrollbarOverlapsContent = false,
                            Child = strips = new FillFlowContainer
                            {
                                AutoSizeAxes = Axes.X,
                                RelativeSizeAxes = Axes.Y,
                                Direction = FillDirection.Horizontal,
                                Spacing = new Vector2(6, 0),
                                Padding = new MarginPadding { Bottom = 10 },
                            },
                        },
                        Empty(),
                        new FillFlowContainer
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Direction = FillDirection.Vertical,
                            Spacing = new Vector2(0, 8),
                            Children = new Drawable[]
                            {
                                createMaster(),
                                renderCard,
                            },
                        },
                    },
                },
            };
        }

        /// <summary>
        /// Entering the view: hear the level audio (owner 2026-10-14: in this
        /// view the user hears what will be in the final track).
        /// </summary>
        public void EnterView() => setLevelAudioPreview(true);

        /// <summary>Leaving the view: back to hearing every track.</summary>
        public void LeaveView() => monitor.PreviewLevelAudio = false;

        private void setLevelAudioPreview(bool on)
        {
            monitor.PreviewLevelAudio = on;

            // Hear the result from its first note.
            if (on && clock != null)
            {
                double end = set.AudioEndMs > 0 ? set.AudioEndMs : monitor.SpanMs;
                if (clock.CurrentTime < set.AudioStartMs || clock.CurrentTime >= end)
                    clock.Seek(set.AudioStartMs);
            }
        }

        private Box bannerFill = null!;
        private SpriteIcon bannerIcon = null!;
        private OsuSpriteText bannerTitle = null!;
        private OsuTextFlowContainer bannerText = null!;
        private ForgeButton bannerSwitch = null!;
        private ForgeButton bannerSwitchBack = null!;

        /// <summary>Says, all the time, what is being heard in this view — and switches it.</summary>
        private Drawable createListenBanner() => new Container
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Masking = true,
            CornerRadius = 6,
            CornerExponent = FormControlBackground.CORNER_EXPONENT,
            Children = new Drawable[]
            {
                bannerFill = new Box { RelativeSizeAxes = Axes.Both },
                new GridContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Padding = new MarginPadding(12),
                    ColumnDimensions = new[]
                    {
                        new Dimension(GridSizeMode.Absolute, 34),
                        new Dimension(),
                        new Dimension(GridSizeMode.Absolute, 190),
                    },
                    RowDimensions = new[] { new Dimension(GridSizeMode.AutoSize) },
                    Content = new[]
                    {
                        new Drawable[]
                        {
                            bannerIcon = new SpriteIcon { Icon = FontAwesome.Solid.Headphones, Size = new Vector2(20), Margin = new MarginPadding { Top = 2 } },
                            new FillFlowContainer
                            {
                                RelativeSizeAxes = Axes.X,
                                AutoSizeAxes = Axes.Y,
                                Direction = FillDirection.Vertical,
                                Spacing = new Vector2(0, 2),
                                Children = new Drawable[]
                                {
                                    bannerTitle = new OsuSpriteText { Font = OsuFont.GetFont(size: 16, weight: FontWeight.Bold) },
                                    bannerText = new OsuTextFlowContainer(t => t.Font = OsuFont.Style.Caption1)
                                    {
                                        RelativeSizeAxes = Axes.X,
                                        AutoSizeAxes = Axes.Y,
                                    },
                                },
                            },
                            new Container
                            {
                                RelativeSizeAxes = Axes.X,
                                AutoSizeAxes = Axes.Y,
                                Anchor = Anchor.CentreRight,
                                Origin = Anchor.CentreRight,
                                Children = new Drawable[]
                                {
                                    bannerSwitch = new ForgeButton(FontAwesome.Solid.Users, @"hear every track")
                                    {
                                        TooltipText = @"Compare with the tracks that are not in the level audio (a listening aid — nothing changes).",
                                        Action = () => setLevelAudioPreview(false),
                                    },
                                    bannerSwitchBack = new ForgeButton(FontAwesome.Solid.Headphones, @"hear the level audio")
                                    {
                                        TooltipText = @"Hear only what the level audio will contain.",
                                        Action = () => setLevelAudioPreview(true),
                                    },
                                },
                            },
                        },
                    },
                },
            },
        };

        private void updateBanner(int companions)
        {
            bool on = monitor.PreviewLevelAudio;
            var colour = ForgeRoles.Colour(colours, ForgeRole.Companion);

            bannerFill.Colour = on ? colour.Opacity(0.18f) : colourProvider.Background5;
            bannerIcon.Colour = on ? colour : colourProvider.Content2;
            bannerSwitch.Alpha = on ? 1 : 0;
            bannerSwitchBack.Alpha = on ? 0 : 1;

            string title = on ? @"You're hearing the level audio" : @"You're hearing every track";
            string text = on
                ? companions > 0
                    ? $"Only the {ForgeFormat.Plural(companions, "companion track", "companion tracks")} play, within the level audio's start and end — exactly the final track. Every other track is silent here."
                    : @"No companion track yet, so there is nothing to hear: mark tracks as companion below."
                : @"A listening aid: the tracks that are not companions play too. The level audio will contain only the companion tracks.";

            if (bannerTitle.Text != title)
            {
                bannerTitle.Text = title;
                bannerText.Text = text;
            }
            else if (drawnBannerText != text)
                bannerText.Text = text;

            drawnBannerText = text;
        }

        private string? drawnBannerText;

        private Drawable createMaster() => new Container
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Masking = true,
            CornerRadius = 6,
            CornerExponent = FormControlBackground.CORNER_EXPONENT,
            Children = new Drawable[]
            {
                new Box { RelativeSizeAxes = Axes.Both, Colour = colourProvider.Background5 },
                new Box { RelativeSizeAxes = Axes.X, Height = 3, Colour = ForgeRoles.Colour(colours, ForgeRole.Companion) },
                new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(0, 8),
                    Padding = new MarginPadding { Horizontal = 12, Top = 14, Bottom = 12 },
                    Children = new Drawable[]
                    {
                        new OsuSpriteText { Text = @"Level audio", Font = OsuFont.GetFont(size: 17, weight: FontWeight.Bold) },
                        new OsuTextFlowContainer(t =>
                        {
                            t.Font = OsuFont.Style.Caption2;
                            t.Colour = colourProvider.Content2;
                        })
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Text = @"What plays under the player during the level: the companion tracks, as you hear them here (faders, positions, trims, edits).",
                        },
                        new FillFlowContainer
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Direction = FillDirection.Horizontal,
                            Spacing = new Vector2(8, 0),
                            Children = new Drawable[]
                            {
                                new LevelMeter(() => monitor.MasterLevel) { Size = new Vector2(10, 64) },
                                new OsuTextFlowContainer(t =>
                                {
                                    t.Font = OsuFont.Style.Caption2;
                                    t.Colour = colourProvider.Content2;
                                })
                                {
                                    Width = 230,
                                    AutoSizeAxes = Axes.Y,
                                    Text = @"Output level of what you hear. The level audio is rendered from the companion tracks automatically.",
                                },
                            },
                        },
                    },
                },
            },
        };

        protected override void Update()
        {
            base.Update();

            ForgeTrack[] tracks;

            try
            {
                tracks = set.Tracks.ToArray().Where(t => t != null).ToArray();
            }
            catch
            {
                return;
            }

            string order = string.Join(',', tracks.Select(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode));

            if (order != drawnOrder)
            {
                drawnOrder = order;
                strips.Clear();

                if (tracks.Length == 0)
                {
                    strips.Add(new OsuSpriteText
                    {
                        Text = @"no tracks yet — import audio in Resources (setup)",
                        Font = OsuFont.Style.Caption1,
                        Colour = colourProvider.Foreground1,
                        Margin = new MarginPadding(12),
                    });
                }

                foreach (var track in tracks)
                    strips.Add(new MixerStrip(set, track, onChanged));
            }

            updateBanner(tracks.Count(t => t.IsCompanion && !t.Muted));
        }
    }

    /// <summary>One channel of the mixing desk.</summary>
    internal partial class MixerStrip : CompositeDrawable
    {
        private readonly ForgeTrackSet set;
        private readonly ForgeTrack track;
        private readonly Action onChanged;

        [Resolved]
        private SetMonitor monitor { get; set; } = null!;

        [Resolved]
        private OsuColour colours { get; set; } = null!;

        [Resolved]
        private OverlayColourProvider colourProvider { get; set; } = null!;

        private Box roleBar = null!;
        private Box background = null!;
        private CompanionToggle companionToggle = null!;
        private OsuSpriteText gainText = null!;
        private HearingLabel hearing = null!;
        private SoloButton solo = null!;

        public MixerStrip(ForgeTrackSet set, ForgeTrack track, Action onChanged)
        {
            this.set = set;
            this.track = track;
            this.onChanged = onChanged;

            Width = 118;
            RelativeSizeAxes = Axes.Y;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            Masking = true;
            CornerRadius = 6;
            CornerExponent = FormControlBackground.CORNER_EXPONENT;

            InternalChildren = new Drawable[]
            {
                background = new Box { RelativeSizeAxes = Axes.Both, Colour = colourProvider.Background5 },
                roleBar = new Box { RelativeSizeAxes = Axes.X, Height = 4 },
                new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(0, 6),
                    Padding = new MarginPadding { Horizontal = 8, Top = 10, Bottom = 8 },
                    Children = new Drawable[]
                    {
                        new TruncatingSpriteText
                        {
                            Text = track.Name,
                            Font = OsuFont.GetFont(size: 13, weight: FontWeight.Bold),
                            RelativeSizeAxes = Axes.X,
                            ShowTooltip = true,
                        },
                        companionToggle = new CompanionToggle(() =>
                        {
                            ForgeTrackSession.History.Record();
                            track.IsCompanion = !track.IsCompanion;
                            onChanged();
                        })
                        {
                            RelativeSizeAxes = Axes.X,
                            Height = 24,
                        },
                        new FillFlowContainer
                        {
                            AutoSizeAxes = Axes.Both,
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            Direction = FillDirection.Horizontal,
                            Spacing = new Vector2(10, 0),
                            Children = new Drawable[]
                            {
                                new ChannelFader(track, onChanged) { Size = new Vector2(30, 200) },
                                new LevelMeter(track) { Size = new Vector2(8, 200) },
                            },
                        },
                        gainText = new OsuSpriteText
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            Font = OsuFont.GetFont(size: 12, weight: FontWeight.SemiBold),
                        },
                        hearing = new HearingLabel(track)
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                        },
                        new FillFlowContainer
                        {
                            AutoSizeAxes = Axes.Both,
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            Direction = FillDirection.Horizontal,
                            Spacing = new Vector2(4, 0),
                            Children = new Drawable[]
                            {
                                solo = new SoloButton(track, onChanged),
                                new MuteButton(track, onChanged),
                            },
                        },
                    },
                },
            };
        }

        protected override void Update()
        {
            base.Update();

            var role = ForgeRoles.Primary(track);
            roleBar.Colour = role != null ? ForgeRoles.Colour(colours, role.Value) : colourProvider.Background3;
            background.Colour = track.IsCompanion ? colourProvider.Background4 : colourProvider.Background5;

            companionToggle.On = track.IsCompanion;
            gainText.Text = ChannelFader.Decibels(track.Gain);
            gainText.Colour = colourProvider.Content2;

            Alpha = monitor.IsAudible(track) ? 1 : 0.6f;

            // Solo is a listening aid for every track; hearing the level audio,
            // only the companion plays, so it does not apply.
            solo.Alpha = monitor.PreviewLevelAudio ? 0 : 1;
        }

        /// <summary>The companion switch.</summary>
        private partial class CompanionToggle : OsuClickableContainer
        {
            private Box fill = null!;
            private OsuSpriteText label = null!;
            private SpriteIcon icon = null!;
            private bool on;

            [Resolved]
            private OsuColour colours { get; set; } = null!;

            public CompanionToggle(Action toggle)
            {
                Action = toggle;
            }

            public override LocalisableString TooltipText
            {
                get => on ? @"companion: plays under the player (part of the level audio) — click to remove" : @"not a companion — click to make it play under the player";
                set { }
            }

            public bool On
            {
                set
                {
                    if (value == on && IsLoaded) return;

                    on = value;
                    var c = ForgeRoles.Colour(colours, ForgeRole.Companion);
                    fill.FadeColour(on ? c : c.Opacity(0.12f), 150);
                    label.Text = on ? @"companion" : @"not companion";
                    label.Colour = icon.Colour = on ? Color4.Black.Opacity(0.8f) : c;
                }
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                Masking = true;
                CornerRadius = 12;
                var c = ForgeRoles.Colour(colours, ForgeRole.Companion);

                Children = new Drawable[]
                {
                    fill = new Box { RelativeSizeAxes = Axes.Both, Colour = c.Opacity(0.12f) },
                    new FillFlowContainer
                    {
                        AutoSizeAxes = Axes.Both,
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Direction = FillDirection.Horizontal,
                        Spacing = new Vector2(5, 0),
                        Children = new Drawable[]
                        {
                            icon = new SpriteIcon { Icon = FontAwesome.Solid.LayerGroup, Size = new Vector2(10), Colour = c, Anchor = Anchor.CentreLeft, Origin = Anchor.CentreLeft },
                            label = new OsuSpriteText { Text = @"not companion", Font = OsuFont.GetFont(size: 12, weight: FontWeight.Bold), Colour = c, Anchor = Anchor.CentreLeft, Origin = Anchor.CentreLeft },
                        },
                    },
                };
            }
        }
    }

    /// <summary>
    /// Vertical gain fader in dB, silence to +20 dB. Drag or click to set;
    /// double-click resets to 0 dB. A whole drag is one undo step.
    /// </summary>
    internal partial class ChannelFader : CompositeDrawable, IHasTooltip
    {

        private readonly ForgeTrack track;
        private readonly Action onChanged;

        private Box fill = null!;
        private Container knob = null!;
        private Box unityMark = null!;

        public ChannelFader(ForgeTrack track, Action onChanged)
        {
            this.track = track;
            this.onChanged = onChanged;
        }

        public LocalisableString TooltipText => $"volume {Decibels(track.Gain)} · drag to change (up to +20 dB) · double-click for 0 dB";

        public static string Decibels(float gain) => ForgeFormat.Db(gain);

        /// <summary>
        /// Fader travel (0..1) for a gain, and back: linear in dB from silence
        /// (-60 dB, bottom) to +20 dB (top), so 0 dB sits at 3/4 of the travel.
        /// </summary>
        private static float positionOf(float gain)
            => (float)Math.Clamp((ForgeFormat.GainToDb(gain) - ForgeFormat.SILENT_DB) / (20 - ForgeFormat.SILENT_DB), 0, 1);

        private static float gainAt(float position)
            => ForgeFormat.DbToGain(ForgeFormat.SILENT_DB + Math.Clamp(position, 0, 1) * (20 - ForgeFormat.SILENT_DB));

        [BackgroundDependencyLoader]
        private void load(OverlayColourProvider colourProvider, OsuColour colours)
        {
            InternalChildren = new Drawable[]
            {
                new Container
                {
                    RelativeSizeAxes = Axes.Y,
                    Width = 6,
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                    Masking = true,
                    CornerRadius = 3,
                    Children = new Drawable[]
                    {
                        new Box { RelativeSizeAxes = Axes.Both, Colour = colourProvider.Background6 },
                        fill = new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Anchor = Anchor.BottomLeft,
                            Origin = Anchor.BottomLeft,
                            Colour = colourProvider.Highlight1,
                        },
                    },
                },
                unityMark = new Box
                {
                    RelativePositionAxes = Axes.Y,
                    Width = 30,
                    Height = 1,
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.Centre,
                    Colour = colourProvider.Foreground1,
                    Y = 1 - positionOf(1),
                },
                knob = new Container
                {
                    RelativePositionAxes = Axes.Y,
                    Size = new Vector2(26, 12),
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.Centre,
                    Masking = true,
                    CornerRadius = 3,
                    Child = new Box { RelativeSizeAxes = Axes.Both, Colour = colours.Gray9 },
                },
            };
        }

        protected override void Update()
        {
            base.Update();

            float p = positionOf(track.Gain);
            fill.Height = p;
            knob.Y = 1 - p;
            fill.Alpha = track.Muted ? 0.3f : 1;
        }

        private void setFrom(Vector2 screenSpace)
        {
            float y = ToLocalSpace(screenSpace).Y / Math.Max(1, DrawHeight);
            float gain = gainAt(1 - y);

            // Snap to 0 dB near unity (within half a dB).
            if (Math.Abs(ForgeFormat.GainToDb(gain)) < 0.5) gain = 1;

            track.Gain = gain;
            onChanged();
        }

        protected override bool OnMouseDown(MouseDownEvent e) => true;

        protected override bool OnClick(ClickEvent e)
        {
            ForgeTrackSession.History.Record();
            setFrom(e.ScreenSpaceMousePosition);
            return true;
        }

        protected override bool OnDoubleClick(DoubleClickEvent e)
        {
            ForgeTrackSession.History.Record();
            track.Gain = 1;
            onChanged();
            return true;
        }

        protected override bool OnDragStart(DragStartEvent e)
        {
            ForgeTrackSession.History.Record();
            return true;
        }

        protected override void OnDrag(DragEvent e) => setFrom(e.ScreenSpaceMousePosition);
    }
}
