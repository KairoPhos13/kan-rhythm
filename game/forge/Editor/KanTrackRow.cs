// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
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
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Overlays;
using osuTK;
using osuTK.Graphics;
using Kan.Core.Native;
using Kan.Forge.Tracks;
using Kan.Core.Tracks;

namespace Kan.Forge.Editor
{
    /// <summary>Per-track UI state that survives row rebuilds (expanded, range, new-track name).</summary>
    internal class TrackRowState
    {
        public bool Expanded;
        public readonly EditRange Range = new EditRange();
        public readonly Bindable<string> Name = new Bindable<string>(string.Empty);
        public readonly Bindable<string> StartText = new Bindable<string>(string.Empty);
    }

    /// <summary>
    /// One row of the track tab. Collapsed: name + duration + tags, the
    /// waveform as the hero strip, and the role chips; the whole state
    /// reads at a glance. Expanded on demand: volume and the non-destructive
    /// range editor (drag on the waveform, name the new track, pick how to
    /// create it).
    /// </summary>
    internal partial class KanTrackRow : CompositeDrawable
    {
        public readonly ForgeTrack Track;

        private readonly ForgeTrackSet set;
        private readonly TrackRowState state;
        private readonly Action<ForgeTrack> onRemove;
        private readonly Action onRoleChanged;
        private readonly Action<ForgeTrack, string> onSaveEdits;

        [Resolved]
        private OsuColour colours { get; set; } = null!;

        [Resolved]
        private OverlayColourProvider colourProvider { get; set; } = null!;

        private Container frame = null!;
        private Box background = null!;
        private Box tint = null!;
        private Box accentBar = null!;
        private TruncatingSpriteText nameText = null!;
        private OsuSpriteText durationText = null!;
        private FillFlowContainer info = null!;
        private FillFlowContainer tags = null!;
        private FillFlowContainer controls = null!;
        private FillFlowContainer workingIndicator = null!;
        private SpriteIcon spinner = null!;
        private bool isWorking;
        private string? workingLabel;
        private OsuSpriteText workingText = null!;

        private LevelMeter meter = null!;

        [Resolved]
        private SetMonitor monitor { get; set; } = null!;
        private ForgeButton editToggle = null!;
        private TrackLane lane = null!;
        private FormNumberBox? startBox;
        private double shownStartMs = -1;
        private bool syncingStart;
        private OsuSpriteText volumeText = null!;
        private readonly List<RoleChip> chips = new List<RoleChip>();
        private Container expandedPanel = null!;
        private OsuTextFlowContainer editHint = null!;

        private string? drawnTagKey;

        private static ForgeSetHistory history => ForgeTrackSession.History;

        /// <summary>Stable per-track key for coalescing slider/typing undo steps.</summary>
        private int historyId => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Track);

        private FillFlowContainer pendingList = null!;
        private bool resettingFilters;

        public KanTrackRow(ForgeTrackSet set, ForgeTrack track, TrackRowState state, Action<ForgeTrack> onRemove,
                           Action onRoleChanged,
                           Action<ForgeTrack, string> onSaveEdits)
        {
            this.set = set;
            Track = track;
            this.state = state;
            this.onRemove = onRemove;
            this.onRoleChanged = onRoleChanged;
            this.onSaveEdits = onSaveEdits;

            RelativeSizeAxes = Axes.X;
            AutoSizeAxes = Axes.Y;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            // Volume in dB: silence (-60) up to +20 dB.
            var gain = new BindableDouble(ForgeFormat.GainToDb(Track.Gain))
            {
                MinValue = ForgeFormat.SILENT_DB,
                MaxValue = 20,
                Precision = 0.5,
            };
            state.StartText.BindValueChanged(v =>
            {
                if (syncingStart || !ForgeFormat.TryParseSeconds(v.NewValue, out double seconds)) return;

                history.Record($"start:{historyId}");
                // Typed: where the audible part starts (the trimmed start stays hidden).
                Track.OffsetMs = Math.Round(seconds * 1000) - Track.ClipStartMs;
                shownStartMs = Track.TimelineStartMs;
                onRoleChanged();
            });

            gain.BindValueChanged(v =>
            {
                history.Record($"gain:{historyId}");
                Track.Gain = ForgeFormat.DbToGain(v.NewValue);
                updateVolumeText();
            });

            foreach (var role in ForgeRoles.ALL)
            {
                var chip = new RoleChip(role);
                chip.Action = () => toggle(chip.Role);
                chips.Add(chip);
            }

            InternalChild = frame = new Container
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Masking = true,
                CornerRadius = 6,
                CornerExponent = FormControlBackground.CORNER_EXPONENT,
                BorderThickness = 2,
                Children = new Drawable[]
                {
                    background = new Box { RelativeSizeAxes = Axes.Both, Colour = colourProvider.Background5 },
                    tint = new Box { RelativeSizeAxes = Axes.Both, Alpha = 0 },
                    accentBar = new Box { RelativeSizeAxes = Axes.Y, Width = 5, Alpha = 0 },
                    new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Vertical,
                        Spacing = new Vector2(0, 6),
                        Padding = new MarginPadding { Left = 16, Right = 10, Vertical = 9 },
                        Children = new Drawable[]
                        {
                            createHeader(),
                            lane = new TrackLane(Track, state.Range)
                            {
                                RelativeSizeAxes = Axes.X,
                                Height = 70,
                                Moved = onRoleChanged,
                            },
                            new Container
                            {
                                RelativeSizeAxes = Axes.X,
                                AutoSizeAxes = Axes.Y,
                                Children = new Drawable[]
                                {
                                    new FillFlowContainer
                                    {
                                        AutoSizeAxes = Axes.Both,
                                        Direction = FillDirection.Horizontal,
                                        Spacing = new Vector2(6, 0),
                                        ChildrenEnumerable = chips,
                                    },
                                    volumeText = new OsuSpriteText
                                    {
                                        Anchor = Anchor.CentreRight,
                                        Origin = Anchor.CentreRight,
                                        Font = OsuFont.GetFont(size: 12, weight: FontWeight.SemiBold),
                                        Colour = colourProvider.Foreground1,
                                    },
                                },
                            },
                            expandedPanel = createExpandedPanel(gain),
                        },
                    },
                },
            };

            updateVolumeText();
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            refreshPending();

            // Looping transforms need the drawable's clock: start after load.
            spinner.Spin(1200, RotationDirection.Clockwise);

            setExpanded(state.Expanded, false);
            Refresh(isWorking, workingLabel);

            if (flashPending)
                flash();
        }

        private Drawable createHeader()
        {
            return new Container
            {
                RelativeSizeAxes = Axes.X,
                Height = 28,
                Children = new Drawable[]
                {
                    info = new FillFlowContainer
                    {
                        AutoSizeAxes = Axes.Both,
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Direction = FillDirection.Horizontal,
                        Spacing = new Vector2(8, 0),
                        Children = new Drawable[]
                        {
                            nameText = new TruncatingSpriteText
                            {
                                Text = Track.Name,
                                Font = OsuFont.GetFont(size: 17, weight: FontWeight.Bold),
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                            },
                            durationText = new OsuSpriteText
                            {
                                Font = OsuFont.GetFont(size: 12, weight: FontWeight.SemiBold),
                                Colour = colourProvider.Foreground1,
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                            },
                            tags = new FillFlowContainer
                            {
                                AutoSizeAxes = Axes.Both,
                                Direction = FillDirection.Horizontal,
                                Spacing = new Vector2(4, 0),
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                            },
                        },
                    },
                    controls = new FillFlowContainer
                    {
                        AutoSizeAxes = Axes.Both,
                        Anchor = Anchor.CentreRight,
                        Origin = Anchor.CentreRight,
                        Direction = FillDirection.Horizontal,
                        Spacing = new Vector2(6, 0),
                        Children = new Drawable[]
                        {
                            workingIndicator = new FillFlowContainer
                            {
                                AutoSizeAxes = Axes.Both,
                                Direction = FillDirection.Horizontal,
                                Spacing = new Vector2(5, 0),
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Alpha = 0,
                                Children = new Drawable[]
                                {
                                    spinner = new SpriteIcon
                                    {
                                        Icon = FontAwesome.Solid.Spinner,
                                        Size = new Vector2(12),
                                        Anchor = Anchor.CentreLeft,
                                        Origin = Anchor.Centre,
                                        Colour = colourProvider.Highlight1,
                                    },
                                    workingText = new OsuSpriteText
                                    {
                                        Font = OsuFont.GetFont(size: 12, weight: FontWeight.SemiBold),
                                        Colour = colourProvider.Highlight1,
                                        Anchor = Anchor.CentreLeft,
                                        Origin = Anchor.CentreLeft,
                                    },
                                },
                            },
                            meter = new LevelMeter(Track)
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                            },
                            new HearingLabel(Track)
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                            },
                            new SoloButton(Track, onRoleChanged)
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                            },
                            new MuteButton(Track, onRoleChanged)
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                            },
                            editToggle = new ForgeButton(FontAwesome.Solid.SlidersH, @"edit")
                            {
                                RelativeSizeAxes = Axes.None,
                                Width = 78,
                                Height = 26,
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                TooltipText = @"volume and range edits (create a new track)",
                                Action = () => setExpanded(!state.Expanded),
                            },
                            new RemoveButton(() => onRemove(Track))
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                            },
                        },
                    },
                },
            };
        }

        private Container createExpandedPanel(BindableDouble gain)
        {
            var from = new FormNumberBox(true)
            {
                Caption = @"from (s)",
                PlaceholderText = @"0.00",
                Current = state.Range.FromText,
            };
            var to = new FormNumberBox(true)
            {
                Caption = @"to (s)",
                PlaceholderText = @"0.00",
                Current = state.Range.ToText,
            };
            var name = new FormTextBox
            {
                Caption = @"new track name",
                PlaceholderText = $@"{Track.Name} (edit)",
                Current = state.Name,
            };

            var lowCut = new BindableDouble(Track.Edits.LowCutHz)
            {
                MinValue = 0,
                MaxValue = 2000,
                Precision = 10,
            };
            lowCut.BindValueChanged(v =>
            {
                if (resettingFilters) return;

                history.Record($"lowcut:{historyId}");
                Track.Edits.LowCutHz = v.NewValue;
                onRoleChanged();
            });

            const double high_cut_off = 20000;
            var highCut = new BindableDouble(Track.Edits.HighCutHz > 0 ? Track.Edits.HighCutHz : high_cut_off)
            {
                MinValue = 500,
                MaxValue = high_cut_off,
                Precision = 100,
            };
            highCut.BindValueChanged(v =>
            {
                if (resettingFilters) return;

                history.Record($"highcut:{historyId}");
                Track.Edits.HighCutHz = v.NewValue >= high_cut_off ? 0 : v.NewValue;
                onRoleChanged();
            });

            var pitch = new BindableInt(Track.Edits.PitchSemitones)
            {
                MinValue = -TrackEdits.MAX_PITCH_SEMITONES,
                MaxValue = TrackEdits.MAX_PITCH_SEMITONES,
            };
            pitch.BindValueChanged(v =>
            {
                if (resettingFilters) return;

                history.Record($"pitch:{historyId}");
                Track.Edits.PitchSemitones = v.NewValue;
                onRoleChanged();
            });

            ForgeButton op(IconUsage icon, string text, string tooltip, KanForgeEditMode mode) => new ForgeButton(icon, text)
            {
                TooltipText = tooltip,
                Action = () => addRange(mode),
            };

            Drawable row(params Drawable[] cells)
            {
                var dims = new List<Dimension>();
                var content = new List<Drawable>();

                for (int i = 0; i < cells.Length; i++)
                {
                    if (i > 0)
                    {
                        dims.Add(new Dimension(GridSizeMode.Absolute, 6));
                        content.Add(Empty());
                    }

                    dims.Add(new Dimension());
                    content.Add(cells[i]);
                }

                return new GridContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    ColumnDimensions = dims.ToArray(),
                    RowDimensions = new[] { new Dimension(GridSizeMode.AutoSize) },
                    Content = new[] { content.ToArray() },
                };
            }

            Drawable heading(IconUsage icon, string title, string subtitle) => new FillFlowContainer
            {
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Horizontal,
                Spacing = new Vector2(7, 0),
                Margin = new MarginPadding { Top = 8, Left = 2 },
                Children = new Drawable[]
                {
                    new SpriteIcon
                    {
                        Icon = icon,
                        Size = new Vector2(12),
                        Colour = colourProvider.Highlight1,
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                    },
                    new OsuSpriteText
                    {
                        Text = title,
                        Font = OsuFont.GetFont(size: 14, weight: FontWeight.Bold),
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                    },
                    new OsuSpriteText
                    {
                        Text = subtitle,
                        Font = OsuFont.Style.Caption2,
                        Colour = colourProvider.Foreground1,
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                    },
                },
            };

            return new Container
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Masking = true,
                Child = new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(0, 6),
                    Padding = new MarginPadding { Top = 4 },
                    Children = new Drawable[]
                    {
                        row(
                            startBox = new FormNumberBox(true)
                            {
                                Caption = @"starts at (s)",
                                HintText = @"Where the audible part of the track starts on the set's timeline. You can also drag the strip on top of the track.",
                                PlaceholderText = @"0.00",
                                Current = state.StartText,
                            },
                            new FormSliderBar<double>
                            {
                                Caption = @"volume (dB) · −60 = silent · up to +20",
                                HintText = @"0 dB plays the file as it is. Above 0 dB amplifies it (up to +20 dB, ten times louder).",
                                Current = gain,
                                KeyboardStep = 0.5f,
                            }),
                        WindowBar.ForTrack(Track, monitor, () =>
                        {
                            showIssues();
                            onRoleChanged();
                        }),
                        heading(FontAwesome.Solid.Headphones, @"Edits", @"heard live while playing: nothing is written until you save"),
                        new OsuTextFlowContainer(t =>
                        {
                            t.Font = OsuFont.Style.Caption2;
                            t.Colour = colourProvider.Content2;
                        })
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Margin = new MarginPadding { Left = 2 },
                            Text = @"Drag across the track above to select a range (or type it), then choose what happens to it. Silenced parts are shaded on the waveform.",
                        },
                        row(from, to),
                        row(
                            op(FontAwesome.Solid.Crop, @"keep this",
                                @"This part stays audible. Everything outside every kept part goes silent (add several keeps to keep several parts). Timing is preserved.",
                                KanForgeEditMode.Keep),
                            op(FontAwesome.Solid.VolumeMute, @"silence this",
                                @"The range goes silent; everything else stays. Timing is preserved.",
                                KanForgeEditMode.Mute),
                            op(FontAwesome.Solid.Cut, @"trim to this",
                                @"The saved file is cut down to this part (it starts where the part started). Several trims keep only what they share.",
                                KanForgeEditMode.Trim)),
                        pendingList = new FillFlowContainer
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Direction = FillDirection.Full,
                            Spacing = new Vector2(5),
                            Margin = new MarginPadding { Left = 2 },
                        },
                        row(
                            new FormSliderBar<double>
                            {
                                Caption = @"low cut (Hz) · 0 = off",
                                HintText = @"Removes rumble and low instruments below this frequency.",
                                Current = lowCut,
                                KeyboardStep = 10,
                            },
                            new FormSliderBar<double>
                            {
                                Caption = @"high cut (Hz) · max = off",
                                HintText = @"Removes hiss and high content above this frequency.",
                                Current = highCut,
                                KeyboardStep = 100,
                            },
                            new FormSliderBar<int>
                            {
                                Caption = @"pitch (semitones) · ±12 = an octave",
                                HintText = @"Moves every note up or down at the same speed: e.g. a piano part written for another range, moved to your instrument's.",
                                Current = pitch,
                                KeyboardStep = 1,
                            }),
                        heading(FontAwesome.Solid.Save, @"Save", @"as a new track: this one is never modified"),
                        row(
                            name,
                            new ForgeButton(FontAwesome.Solid.Save, @"save as new track", colours.GreenLight)
                            {
                                Height = 52,
                                TooltipText = @"Render this track with its edits, filters and pitch into a new track of the set.",
                                Action = () =>
                                {
                                    editHint.FadeOut(100);
                                    onSaveEdits(Track, state.Name.Value ?? string.Empty);
                                },
                            },
                            new ForgeButton(FontAwesome.Solid.Undo, @"discard edits")
                            {
                                Height = 52,
                                TooltipText = @"Drop the pending edits, filters and pitch (undo brings them back).",
                                Action = () =>
                                {
                                    if (Track.Edits.IsEmpty) return;

                                    history.Record();
                                    Track.Edits = new TrackEdits();
                                    resettingFilters = true;
                                    lowCut.Value = 0;
                                    highCut.Value = high_cut_off;
                                    pitch.Value = 0;
                                    resettingFilters = false;
                                    refreshPending();
                                    onRoleChanged();
                                },
                            }),
                        editHint = new OsuTextFlowContainer(t =>
                        {
                            t.Font = OsuFont.Style.Caption2;
                            t.Colour = colours.Orange1;
                        })
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Margin = new MarginPadding { Left = 2 },
                            Alpha = 0,
                        },
                    },
                },
            };
        }

        private void addRange(KanForgeEditMode mode)
        {
            if (!state.Range.IsSet)
            {
                ShowEditHint("Select a range first: drag across the track above, or type from < to (seconds).");
                return;
            }

            editHint.FadeOut(100);
            history.Record();
            Track.Edits.Ranges.Add(new PendingRange(mode, state.Range.From.Value * 1000, state.Range.To.Value * 1000));
            refreshPending();
            onRoleChanged();
        }

        /// <summary>The pending range edits as removable chips.</summary>
        private void refreshPending()
        {
            pendingList.Clear();

            if (Track.Edits.Ranges.Count == 0)
            {
                pendingList.Add(new OsuSpriteText
                {
                    Text = @"no range edits yet",
                    Font = OsuFont.Style.Caption2,
                    Colour = colourProvider.Foreground1,
                });
                return;
            }

            foreach (var range in Track.Edits.Ranges.ToList())
            {
                string verb = range.Mode switch
                {
                    KanForgeEditMode.Keep => @"keep",
                    KanForgeEditMode.Mute => @"silence",
                    _ => @"trim to",
                };

                pendingList.Add(new PendingChip($"{verb} {ForgeFormat.Time(range.FromMs / 1000)}-{ForgeFormat.Time(range.ToMs / 1000)}", () =>
                {
                    history.Record();
                    Track.Edits.Ranges.Remove(range);
                    refreshPending();
                    onRoleChanged();
                }));
            }

            showIssues();
        }

        /// <summary>Flag combinations that make no sense (errors block saving; warnings have no effect).</summary>
        private void showIssues()
        {
            var issues = Track.EffectiveEdits().Validate();

            if (issues.Count == 0)
            {
                editHint.FadeOut(100);
                return;
            }

            editHint.Clear();

            foreach (var issue in issues)
            {
                editHint.AddIcon(issue.IsError ? FontAwesome.Solid.TimesCircle : FontAwesome.Solid.ExclamationTriangle,
                    i => i.Colour = issue.IsError ? colours.Red1 : colours.Orange1);
                editHint.AddText($"  {issue.Message}", t => t.Colour = issue.IsError ? colours.Red1 : colours.Orange1);
                editHint.NewParagraph();
            }

            editHint.FadeIn(150);
        }

        protected override void Update()
        {
            base.Update();

            // Long names truncate instead of colliding with the controls.
            float others = info.DrawWidth - nameText.DrawWidth;
            nameText.MaxWidth = Math.Max(80, DrawWidth - 26 - controls.DrawWidth - others - 16);

            double? duration = TrackDurations.Get(Track.Path);
            string length = duration is double d ? ForgeFormat.Time(d) : string.Empty;
            durationText.Text = Track.TimelineStartMs > 0 ? $"at {ForgeFormat.Time(Track.TimelineStartMs / 1000)} · {length}" : length;

            // Keep the typed start in step with drags. Typing records what it
            // set (shownStartMs), so only outside moves rewrite the box.
            if (startBox != null && Math.Abs(Track.TimelineStartMs - shownStartMs) > 0.5)
            {
                shownStartMs = Track.TimelineStartMs;
                syncingStart = true;
                state.StartText.Value = ForgeFormat.Seconds(Track.TimelineStartMs / 1000);
                syncingStart = false;
            }
        }

        // ------------------------------------------------------------------
        // Roles (same semantics as the model: music/reference exclusive,
        // accompaniment free, demux refused on separated/mixed material)
        // ------------------------------------------------------------------

        private void toggle(ForgeRole role)
        {
            history.Record();

            switch (role)
            {
                case ForgeRole.Reference:
                    if (!Track.IsReference)
                        set.SetReference(Track);
                    else
                        Track.IsReference = false;

                    break;

                case ForgeRole.Companion:
                    Track.IsCompanion = !Track.IsCompanion;
                    break;
            }

            onRoleChanged();
        }

        // ------------------------------------------------------------------
        // State → visuals
        // ------------------------------------------------------------------

        /// <summary>Reflect the model (roles, flags, analysis) and whether a job is working on this track.</summary>
        public void Refresh(bool working, string? label)
        {
            isWorking = working;
            workingLabel = label;

            if (LoadState < LoadState.Ready) return;

            var primary = ForgeRoles.Primary(Track);
            Color4 highlight = primary != null ? ForgeRoles.Colour(colours, primary.Value) : colourProvider.Background5;

            frame.TransformTo(nameof(BorderColour), (osu.Framework.Graphics.Colour.ColourInfo)(primary != null ? highlight : colourProvider.Background5), 200, Easing.OutQuint);
            accentBar.FadeColour(highlight, 200).FadeTo(primary != null ? 1 : 0, 200);
            tint.FadeColour(highlight, 200).FadeTo(primary != null ? 0.07f : 0, 200);
            background.FadeColour(primary != null ? colourProvider.Background4 : colourProvider.Background5, 200);

            foreach (var chip in chips)
                chip.SetState(ForgeRoles.Has(Track, chip.Role));

            lane.AccentColour = WaveformPreview.ColourFor(Track, colours, colourProvider);
            bool audible = monitor.IsAudible(Track);
            lane.SetAudible(audible);
            nameText.FadeTo(Track.LikelyEmpty ? 0.6f : 1, 200);

            string tagKey = $"{Track.LikelyEmpty}{Track.IsSeparated}{Track.IsMixdown}{ForgeTrackSession.IsVideoAudio(Track)}{Track.Edits.IsEmpty}{Track.AnalysisFailed}";

            if (tagKey != drawnTagKey)
            {
                drawnTagKey = tagKey;

                var pills = TagPill.For(Track, colours, colourProvider).ToList();
                if (!Track.Edits.IsEmpty)
                {
                    pills.Insert(0, new TagPill(@"unsaved edits", FontAwesome.Solid.Headphones, colourProvider.Highlight1,
                        @"This track has edits you can hear but haven't saved. Open 'edit' to save them as a new track or discard them."));
                }

                tags.ChildrenEnumerable = pills;
            }

            showIssues();

            workingIndicator.FadeTo(isWorking ? 1 : 0, 150);
            workingText.Text = workingLabel ?? string.Empty;

            updateVolumeText();
        }

        private void updateVolumeText()
        {
            volumeText.Text = Track.Muted
                ? @"muted"
                : Math.Abs(Track.Gain - 1) < 0.001f ? string.Empty : $"volume {ForgeFormat.Db(Track.Gain)}";
        }

        private void setExpanded(bool expanded, bool animate = true)
        {
            state.Expanded = expanded;
            lane.EditMode = expanded;

            if (expanded)
            {
                expandedPanel.Show();
                expandedPanel.FadeInFromZero(animate ? 200 : 0);
            }
            else
                expandedPanel.Hide();
        }

        /// <summary>Show an inline message under the range editor (validation, not lost in a status line).</summary>
        public void ShowEditHint(string message)
        {
            setExpanded(true);
            editHint.Text = message;
            editHint.FadeIn(150);
            editHint.FlashColour(Color4.White, 400);
        }

        /// <summary>Brief highlight when the row first appears (new import, stem or edit).</summary>
        public void FlashNew()
        {
            if (IsLoaded)
                flash();
            else
                flashPending = true;
        }

        private bool flashPending;

        private void flash()
        {
            flashPending = false;
            this.FadeInFromZero(300);
            tint.FadeColour(colourProvider.Highlight1).FadeTo(0.35f).FadeTo(0, 1200, Easing.OutQuint);
        }
    }
}
