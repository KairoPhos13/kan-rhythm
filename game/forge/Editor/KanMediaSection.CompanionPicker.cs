// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Development;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Extensions;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osu.Game.Beatmaps;
using osu.Game.Extensions;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Overlays;
using osu.Game.Screens.Edit.Setup;
using osu.Game.Utils;
using Kan.Forge.Media;
using Kan.Forge.Tracks;
using osuTK.Graphics;
using Kan.Core.Tracks;

namespace Kan.Forge.Editor
{
    /// <summary>
    /// The level audio row's link to the companion track, and the popover that picks it.
    /// </summary>
    public partial class KanMediaSection : ResourcesSection
    {
        /// <summary>
        /// The editor's "Audio Track", shown where the stock row was: not a
        /// second import; it is the level audio, the mix of the companion
        /// tracks (owner naming 2026-10-14). Shows which tracks it mixes, the
        /// file it plays as and whether it is up to date; the picker toggles
        /// companion tracks, and closing it renders the level audio if the
        /// mix changed.
        /// </summary>
        private partial class AudioTrackLink : CompositeDrawable, IHasPopover
        {
            private readonly ForgeTrackSet set;
            private readonly IBindable<WorkingBeatmap> working;
            private readonly Bindable<Visibility> popoverState = new Bindable<Visibility>();

            [Resolved]
            private BeatmapManager beatmaps { get; set; } = null!;

            [Resolved]
            private MusicController music { get; set; } = null!;

            [Resolved]
            private osu.Game.Screens.Edit.Editor? editor { get; set; }

            [Resolved]
            private INotificationOverlay? notifications { get; set; }

            private FormControlBackground background = null!;
            private OsuTextFlowContainer text = null!;
            private SpriteIcon linkIcon = null!;
            private string? drawnKey;
            private int countdown;

            [Resolved]
            private OsuColour colours { get; set; } = null!;

            [Resolved]
            private OverlayColourProvider colourProvider { get; set; } = null!;

            public AudioTrackLink(ForgeTrackSet set, IBindable<WorkingBeatmap> working)
            {
                this.set = set;
                this.working = working;

                RelativeSizeAxes = Axes.X;
                AutoSizeAxes = Axes.Y;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                InternalChildren = new Drawable[]
                {
                    background = new FormControlBackground(),
                    new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Vertical,
                        Spacing = new osuTK.Vector2(0, 4),
                        Padding = new MarginPadding { Left = 9, Right = 32, Vertical = 9 },
                        Children = new Drawable[]
                        {
                            new FormFieldCaption
                            {
                                Caption = @"Audio Track (level audio)",
                                TooltipText = @"What plays under the player: the mix of the companion tracks. Click to choose them; new audio is imported in 'Audio tracks' below.",
                            },
                            text = new OsuTextFlowContainer(t => t.Font = OsuFont.Default.With(size: 16))
                            {
                                RelativeSizeAxes = Axes.X,
                                AutoSizeAxes = Axes.Y,
                            },
                        },
                    },
                    linkIcon = new SpriteIcon
                    {
                        Anchor = Anchor.BottomRight,
                        Origin = Anchor.BottomRight,
                        Margin = new MarginPadding(9),
                        Icon = FontAwesome.Solid.ChevronDown,
                        Size = new osuTK.Vector2(13),
                    },
                };
            }

            protected override void LoadComplete()
            {
                base.LoadComplete();
                popoverState.BindValueChanged(v =>
                {
                    updateStyle();

                    // Closing the picker brings the level audio up to date.
                    if (v.NewValue == Visibility.Hidden && set.Companions.Any() && !BeatmapAudio.IsUpToDate(set))
                        render();
                });
            }

            private void render()
            {
                if (BeatmapAudio.Rendering) return;

                renderTask = BeatmapAudio.RenderAsync(set, working.Value, beatmaps, music, editor, Scheduler);
                renderTask.ContinueWith(t => Schedule(() =>
                {
                    if (t.IsFaulted)
                        ForgeNotify.Error(notifications, $"Rendering the level audio failed: {t.Exception?.GetBaseException().Message}");
                    else
                        ForgeNotify.Info(notifications, $"The level audio is up to date: \"{t.Result}\".", FontAwesome.Solid.LayerGroup);

                    countdown = 0;
                }));
                countdown = 0;
            }

            private System.Threading.Tasks.Task<string>? renderTask;

            protected override bool OnHover(HoverEvent e)
            {
                updateStyle();
                return true;
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                base.OnHoverLost(e);
                updateStyle();
            }

            protected override bool OnClick(ClickEvent e)
            {
                this.ShowPopover();
                return true;
            }

            private void updateStyle()
            {
                if (popoverState.Value == Visibility.Visible)
                    background.VisualStyle = VisualStyle.Focused;
                else
                    background.VisualStyle = IsHovered ? VisualStyle.Hovered : VisualStyle.Normal;
            }

            public Popover GetPopover()
            {
                var popover = new CompanionPickerPopover(set, toggle);
                popoverState.UnbindBindings();
                popoverState.BindTo(popover.State);
                return popover;
            }

            private void toggle(ForgeTrack track)
            {
                ForgeTrackSession.History.Record();
                track.IsCompanion = !track.IsCompanion;
                countdown = 0;
            }

            protected override void Update()
            {
                base.Update();

                if (--countdown > 0) return;
                countdown = 15;

                ForgeTrack[] companions;

                try
                {
                    companions = set.Companions.ToArray();
                }
                catch (InvalidOperationException)
                {
                    return; // the demux worker is adding stems; next poll
                }

                string audioFile = working.Value.Metadata.AudioFile ?? string.Empty;
                bool upToDate = BeatmapAudio.IsUpToDate(set);
                bool rendering = BeatmapAudio.Rendering;
                string key = $"{string.Join('|', companions.Select(c => c.Name))}#{audioFile}#{upToDate}#{rendering}";
                if (key == drawnKey) return;

                bool changed = drawnKey != null;
                drawnKey = key;

                text.Clear();
                var colour = ForgeRoles.Colour(colours, ForgeRole.Companion);

                if (companions.Length > 0)
                {
                    text.AddIcon(FontAwesome.Solid.LayerGroup, i => i.Colour = colour);
                    text.AddText(@"  ");
                    text.AddText(companions.Length == 1 ? companions[0].Name : $"mix of {companions.Length} companion tracks",
                        t => t.Font = OsuFont.Default.With(size: 16, weight: FontWeight.SemiBold));

                    string state = rendering ? @"rendering…"
                        : upToDate && !string.IsNullOrEmpty(audioFile) ? $"plays as {audioFile}"
                        : @"changed: updates when you close this or leave the track tab";
                    text.AddText($"   {state}", t => t.Colour = colourProvider.Foreground1);

                    if (companions.Length > 1)
                    {
                        text.NewParagraph();
                        text.AddText(string.Join(@" · ", companions.Select(c => c.Name)), t =>
                        {
                            t.Font = OsuFont.Style.Caption2;
                            t.Colour = colourProvider.Content2;
                        });
                    }
                }
                else if (!string.IsNullOrEmpty(audioFile))
                {
                    text.AddText(audioFile);
                    text.AddText(@"   no companion tracks yet: click to choose them", t => t.Colour = colourProvider.Foreground1);
                }
                else
                    text.AddText(@"No level audio yet: click to choose the companion tracks", t => t.Colour = colourProvider.Foreground1);

                linkIcon.Colour = companions.Length > 0 ? colour : colourProvider.Light1;

                if (changed)
                    background.FlashOnCommit();
            }
        }

        /// <summary>The level audio picker: toggle companion tracks (selection only; nothing is imported here).</summary>
        private partial class CompanionPickerPopover : OsuPopover
        {
            private readonly ForgeTrackSet set;
            private readonly Action<ForgeTrack> onChoose;

            public CompanionPickerPopover(ForgeTrackSet set, Action<ForgeTrack> onChoose)
                : base(false)
            {
                this.set = set;
                this.onChoose = onChoose;
            }

            [BackgroundDependencyLoader]
            private void load(OverlayColourProvider colourProvider)
            {
                var tracks = set.Tracks.ToArray().Where(t => t != null).ToArray();

                var list = new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Vertical,
                    Spacing = new osuTK.Vector2(0, 4),
                };

                if (tracks.Length == 0)
                {
                    list.Add(new OsuTextFlowContainer(t =>
                    {
                        t.Font = OsuFont.Style.Caption1;
                        t.Colour = colourProvider.Content2;
                    })
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Padding = new MarginPadding(8),
                        Text = @"The set is empty: add audio with 'Audio tracks' below, then choose the companion tracks here.",
                    });
                }

                foreach (var track in tracks)
                    list.Add(new PickerRow(track, () => onChoose(track)));

                Child = new OsuScrollContainer
                {
                    Width = 380,
                    Height = Math.Clamp(tracks.Length * 48f + 8, 60, 320),
                    Child = list,
                };
            }
        }

        private partial class PickerRow : OsuClickableContainer
        {
            private readonly ForgeTrack track;
            private Box hover = null!;
            private SpriteIcon check = null!;
            private Color4 on;
            private Color4 off;

            public override LocalisableString TooltipText
            {
                get => track.IsCompanion ? @"companion: click to remove it from the level audio" : @"click to make it a companion (part of the level audio)";
                set { }
            }

            protected override void Update()
            {
                base.Update();
                check.Icon = track.IsCompanion ? FontAwesome.Solid.CheckSquare : FontAwesome.Regular.Square;
                check.Colour = track.IsCompanion ? on : off;
            }

            public PickerRow(ForgeTrack track, Action onChoose)
            {
                this.track = track;
                Action = onChoose;
                RelativeSizeAxes = Axes.X;
                Height = 44;
            }

            [BackgroundDependencyLoader]
            private void load(OverlayColourProvider colourProvider, OsuColour colours)
            {
                Masking = true;
                CornerRadius = 5;
                on = ForgeRoles.Colour(colours, ForgeRole.Companion);
                off = colourProvider.Content2;

                var details = new FillFlowContainer
                {
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Horizontal,
                    Spacing = new osuTK.Vector2(4, 0),
                };

                foreach (var role in ForgeRoles.ALL)
                {
                    if (!ForgeRoles.Has(track, role)) continue;

                    var chip = new RoleChip(role, compact: true);
                    chip.SetState(true);
                    details.Add(chip);
                }

                details.AddRange(TagPill.For(track, colours, colourProvider));

                if (TrackDurations.Get(track.Path) is double seconds)
                {
                    details.Add(new OsuSpriteText
                    {
                        Text = ForgeFormat.Time(seconds),
                        Font = OsuFont.GetFont(size: 11, weight: FontWeight.SemiBold),
                        Colour = colourProvider.Foreground1,
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                    });
                }

                Children = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both, Colour = colourProvider.Background5 },
                    new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Padding = new MarginPadding { Left = 140, Right = 6 },
                        Alpha = 0.4f,
                        Child = new WaveformPreview(track)
                        {
                            RelativeSizeAxes = Axes.Both,
                            AccentColour = WaveformPreview.ColourFor(track, colours, colourProvider),
                        },
                    },
                    hover = new Box { RelativeSizeAxes = Axes.Both, Colour = colourProvider.Highlight1, Alpha = 0 },
                    check = new SpriteIcon
                    {
                        Size = new osuTK.Vector2(13),
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.Centre,
                        X = 16,
                    },
                    new FillFlowContainer
                    {
                        AutoSizeAxes = Axes.Both,
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Direction = FillDirection.Vertical,
                        Spacing = new osuTK.Vector2(0, 3),
                        Padding = new MarginPadding { Left = 32 },
                        Children = new Drawable[]
                        {
                            new TruncatingSpriteText
                            {
                                Text = track.Name,
                                MaxWidth = 320,
                                Font = OsuFont.GetFont(size: 14, weight: FontWeight.Bold),
                                Alpha = track.LikelyEmpty ? 0.6f : 1,
                            },
                            details,
                        },
                    },
                };
            }

            protected override bool OnHover(HoverEvent e)
            {
                hover.FadeTo(0.15f, 100);
                return base.OnHover(e);
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                hover.FadeOut(200);
                base.OnHoverLost(e);
            }
        }
    }
}
