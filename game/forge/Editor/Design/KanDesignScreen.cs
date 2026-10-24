// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osuTK.Input;
using osu.Framework.Localisation;
using osu.Framework.Platform;
using osu.Game.Beatmaps;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Overlays;
using osu.Game.Screens.Edit;
using osuTK;
using osuTK.Graphics;
using Kan.Core.Design;
using Kan.Forge.Editor.Compose;

namespace Kan.Forge.Editor.Design
{
    /// <summary>
    /// kan!'s design tab (owner 2026-10-15, fork F-15): how the level looks
    /// and rewards, stretch by stretch. A timeline of sections over the
    /// notes (drag a boundary to move it, click to select and seek), the
    /// selected section's settings (what it is, climax, what a rest shows,
    /// its own palette) and the level's (palette, effects). A level starts
    /// from an automatic design (<see cref="AutoDesign"/>); editing a section
    /// makes it the author's. Saved inside the level (<see cref="KanDesign.FILE_NAME"/>).
    /// The notes are heard with the companion by default (owner 2026-10-17:
    /// "it's difficult to understand the song without them"), with the same
    /// toggles as the compose tab (<see cref="ComposeMonitor"/>).
    /// </summary>
    public partial class KanDesignScreen : EditorScreen
    {
        [Resolved]
        private EditorClock clock { get; set; } = null!;

        [Resolved]
        private IBindable<WorkingBeatmap> working { get; set; } = null!;

        [Resolved]
        private BeatmapManager beatmaps { get; set; } = null!;

        [Resolved]
        private Storage storage { get; set; } = null!;

        [Resolved]
        private INotificationOverlay? notifications { get; set; }

        private Timeline timeline = null!;
        private FillFlowContainer levelFields = null!;
        private FillFlowContainer sectionFields = null!;
        private OsuSpriteText status = null!;
        private ForgeButton undoButton = null!, redoButton = null!, splitButton = null!, mergeButton = null!;
        private ForgeButton companionToggle = null!, referenceToggle = null!, notesToggle = null!;
        private ComposeMonitor monitor = null!;

        private DesignSection? selected;
        private int autosaveCountdown;
        private bool dirty;

        public KanDesignScreen()
            : base(EditorScreenMode.Design)
        {
        }

        private double levelLengthMs => LevelSession.LengthOf(working.Value);

        [BackgroundDependencyLoader]
        private void load(OverlayColourProvider colourProvider)
        {
            open();

            Add(monitor = new ComposeMonitor());
            monitor.NotesOn.Value = true;

            Add(new Box { RelativeSizeAxes = Axes.Both, Colour = colourProvider.Background3 });
            Add(new GridContainer
            {
                RelativeSizeAxes = Axes.Both,
                Padding = new MarginPadding(10),
                RowDimensions = new[]
                {
                    new Dimension(GridSizeMode.AutoSize),
                    new Dimension(GridSizeMode.Absolute, 8),
                    new Dimension(GridSizeMode.Absolute, 150),
                    new Dimension(GridSizeMode.Absolute, 10),
                    new Dimension(),
                },
                Content = new[]
                {
                    new Drawable[] { createToolbar() },
                    new Drawable[] { Empty() },
                    new Drawable[]
                    {
                        new Container
                        {
                            RelativeSizeAxes = Axes.Both,
                            Masking = true,
                            CornerRadius = 6,
                            Child = timeline = new Timeline(select, changed),
                        },
                    },
                    new Drawable[] { Empty() },
                    new Drawable[]
                    {
                        new GridContainer
                        {
                            RelativeSizeAxes = Axes.Both,
                            ColumnDimensions = new[] { new Dimension(), new Dimension(GridSizeMode.Absolute, 10), new Dimension() },
                            Content = new[]
                            {
                                new Drawable[]
                                {
                                    panel(colourProvider, @"This section", sectionFields = fields()),
                                    Empty(),
                                    panel(colourProvider, @"The whole level", levelFields = fields()),
                                },
                            },
                        },
                    },
                },
            });
        }

        private void open()
        {
            LevelSession.Open(storage, working.Value); // the design's automatic sections follow the notes as they are now
        }

        private static FillFlowContainer fields() => new FillFlowContainer
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Direction = FillDirection.Vertical,
            Spacing = new Vector2(0, 6),
        };

        private static Drawable panel(OverlayColourProvider colourProvider, string title, Drawable content) => new Container
        {
            RelativeSizeAxes = Axes.Both,
            Masking = true,
            CornerRadius = 6,
            Children = new Drawable[]
            {
                new Box { RelativeSizeAxes = Axes.Both, Colour = colourProvider.Background4 },
                new OsuScrollContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    Child = new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Vertical,
                        Padding = new MarginPadding(12),
                        Spacing = new Vector2(0, 10),
                        Children = new[]
                        {
                            new OsuSpriteText { Text = title, Font = OsuFont.GetFont(size: 18, weight: FontWeight.Bold) },
                            content,
                        },
                    },
                },
            },
        };

        private Drawable createToolbar()
        {
            ForgeButton button(IconUsage icon, string text, Action action, float width, string? tooltip = null) => new ForgeButton(icon, text)
            {
                RelativeSizeAxes = Axes.None,
                Width = width,
                Action = action,
                TooltipText = tooltip ?? string.Empty,
            };

            return new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Horizontal,
                Spacing = new Vector2(8, 0),
                Children = new Drawable[]
                {
                    button(FontAwesome.Solid.Magic, @"Automatic design", regenerate, 170),
                    splitButton = button(FontAwesome.Solid.Cut, @"Split at playhead", split, 160),
                    mergeButton = button(FontAwesome.Solid.CompressArrowsAlt, @"Join with next", merge, 150),
                    undoButton = button(FontAwesome.Solid.Undo, @"Undo", () => history(DesignSession.Undo), 90),
                    redoButton = button(FontAwesome.Solid.Redo, @"Redo", () => history(DesignSession.Redo), 90),
                    button(FontAwesome.Solid.Save, @"Save into level", () => saveToLevel(true), 150),
                    companionToggle = button(FontAwesome.Solid.LayerGroup, @"companion", () => monitor.CompanionOn.Toggle(), 128, @"Hear the companion (the level audio) — M"),
                    referenceToggle = button(FontAwesome.Solid.Microphone, @"reference", () => monitor.ReferenceOn.Toggle(), 124, @"Hear the reference (the full performance) — R"),
                    notesToggle = button(FontAwesome.Solid.Music, @"notes", () => monitor.NotesOn.Toggle(), 96, @"Hear the notes the player plays — P"),
                    status = new OsuSpriteText
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Font = OsuFont.GetFont(size: 14),
                    },
                },
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            rebuildLevelFields();
            select(DesignSession.Design.SectionAt(clock.CurrentTime));
            monitor.Active.Value = true;
        }

        protected override void Update()
        {
            base.Update();

            undoButton.Enabled.Value = DesignSession.CanUndo;
            redoButton.Enabled.Value = DesignSession.CanRedo;
            splitButton.Enabled.Value = DesignSession.Design.SectionAt(clock.CurrentTime) != null;
            mergeButton.Enabled.Value = selected != null && DesignSession.Design.Sections.IndexOf(selected) < DesignSession.Design.Sections.Count - 1;

            bool hasReference = monitor.Reference != null;
            referenceToggle.Enabled.Value = hasReference;
            companionToggle.Alpha = monitor.CompanionOn.Value ? 1 : 0.55f;
            referenceToggle.Alpha = monitor.ReferenceOn.Value && hasReference ? 1 : 0.55f;
            notesToggle.Alpha = monitor.NotesOn.Value ? 1 : 0.55f;

            var d = DesignSession.Design;
            status.Text = $"{d.Sections.Count} sections · {(d.Generated ? "automatic (from the notes)" : "designed by hand")}"
                          + (DesignSession.SavedInLevel ? @" · saved" : @" · not saved into the level yet");

            if (dirty && --autosaveCountdown <= 0)
            {
                dirty = false;
                DesignSession.AutoSave();
            }

            timeline.Playhead = clock.CurrentTime;
            timeline.LengthMs = Math.Max(levelLengthMs, d.Sections.Count > 0 ? d.Sections[^1].EndMs : 0);
        }

        // ------------------------------------------------------------------
        // Actions
        // ------------------------------------------------------------------

        private void changed()
        {
            dirty = true;
            autosaveCountdown = 60;
            timeline.Invalidate();
        }

        private void regenerate()
        {
            DesignSession.Regenerate(ScoreSession.Score.Notes, levelLengthMs);
            afterReplace();
        }

        private void split()
        {
            DesignSession.Record();

            if (!DesignSession.Design.SplitAt(clock.CurrentTime))
            {
                DesignSession.Undo();
                ForgeNotify.Info(notifications, $"A section can't be shorter than {KanDesign.MIN_SECTION_MS / 1000:0} s.", FontAwesome.Solid.Cut);
                return;
            }

            changed();
            select(DesignSession.Design.SectionAt(clock.CurrentTime));
        }

        private void merge()
        {
            if (selected == null) return;

            DesignSession.Record();
            DesignSession.Design.MergeWithNext(selected);
            changed();
            select(selected);
        }

        private void history(Action step)
        {
            step();
            afterReplace();
        }

        /// <summary>The design object was replaced (undo, regenerate): re-point everything at it.</summary>
        private void afterReplace()
        {
            changed();
            rebuildLevelFields();
            select(DesignSession.Design.SectionAt(selected?.StartMs ?? clock.CurrentTime));
        }

        private void saveToLevel(bool notify)
        {
            if (DesignSession.SavedInLevel && !notify) return;

            string? error = DesignSession.SaveToLevel(working.Value, beatmaps);

            if (error != null)
                ForgeNotify.Error(notifications, $"Could not save the design into the level: {error}");
            else if (notify)
                ForgeNotify.Info(notifications, "Saved the design into the level.", FontAwesome.Solid.Save);
        }

        protected override void PopIn()
        {
            base.PopIn();

            // The notes may have changed in the compose tab: automatic sections follow them.
            if (IsLoaded)
            {
                open();
                afterReplace();
                monitor.NotesChanged();
                monitor.Active.Value = true;
            }
        }

        protected override void PopOut()
        {
            base.PopOut();
            if (!IsLoaded) return;

            monitor.Active.Value = false;
            saveToLevel(false);
        }

        protected override bool OnKeyDown(KeyDownEvent e)
        {
            if (e.ControlPressed || e.AltPressed || e.Repeat) return base.OnKeyDown(e);

            switch (e.Key)
            {
                case Key.P:
                    monitor.NotesOn.Toggle();
                    return true;

                case Key.M:
                    monitor.CompanionOn.Toggle();
                    return true;

                case Key.R:
                    if (monitor.Reference != null) monitor.ReferenceOn.Toggle();
                    return true;
            }

            return base.OnKeyDown(e);
        }

        public override void OnExiting(osu.Framework.Screens.ScreenExitEvent e)
        {
            base.OnExiting(e);
            saveToLevel(false);
        }

        // ------------------------------------------------------------------
        // Fields
        // ------------------------------------------------------------------

        private void select(DesignSection? section)
        {
            selected = section;
            timeline.Selected = section;
            rebuildSectionFields();
        }

        /// <summary>Edit a section's setting: undo snapshot, and the sections become the author's.</summary>
        private void editSection(Action<DesignSection> edit)
        {
            if (selected == null) return;

            DesignSession.Record();

            // The snapshot cloned the design; keep editing the live object.
            edit(selected);
            DesignSession.Design.Generated = false;
            changed();
        }

        private void editLevel(Action<KanDesign> edit)
        {
            DesignSession.Record();
            edit(DesignSession.Design);
            changed();
        }

        private void rebuildSectionFields()
        {
            sectionFields.Clear();

            var s = selected;

            if (s == null)
            {
                sectionFields.Add(new OsuSpriteText { Text = @"Click a section on the timeline.", Font = OsuFont.GetFont(size: 14) });
                return;
            }

            sectionFields.Add(new OsuSpriteText
            {
                Text = $"{ForgeFormat.Time(s.StartMs / 1000)} – {ForgeFormat.Time(s.EndMs / 1000)}  ({s.DurationMs / 1000:0.0} s)",
                Font = OsuFont.GetFont(size: 14, weight: FontWeight.SemiBold),
            });

            var kind = new Bindable<SectionKind>(s.Kind);
            kind.BindValueChanged(v =>
            {
                editSection(x => x.Kind = v.NewValue);
                rebuildSectionFields();
            });

            var label = new Bindable<string>(s.Label ?? string.Empty);
            label.BindValueChanged(v => editSection(x => x.Label = string.IsNullOrWhiteSpace(v.NewValue) ? null : v.NewValue.Trim()));

            var climax = new BindableBool(s.Climax);
            climax.BindValueChanged(v => editSection(x => x.Climax = v.NewValue));

            var restStyle = new Bindable<BreakStyle>(s.BreakStyle);
            restStyle.BindValueChanged(v => editSection(x => x.BreakStyle = v.NewValue));

            var palette = new Bindable<string>(s.Palette ?? string.Empty);
            palette.BindValueChanged(v => editSection(x => x.Palette = string.IsNullOrEmpty(v.NewValue) ? null : v.NewValue));

            sectionFields.AddRange(new Drawable[]
            {
                new KindDropdown { Caption = @"what it is", HintText = @"Shown as the section starts. A break is a rest: nothing to play.", Current = kind },
                new FormTextBox { Caption = @"name (optional)", HintText = @"Shown instead of the kind, e.g. ""Second verse"".", Current = label },
                new FormCheckBox
                {
                    Caption = @"climax",
                    HintText = @"The level's high point: hotter colours, bigger sparks, the background glows with the music.",
                    Current = climax,
                },
            });

            if (s.IsRest || s.Kind is SectionKind.Intro or SectionKind.Outro)
                sectionFields.Add(new RestStyleDropdown { Caption = @"while there is nothing to play", Current = restStyle });

            sectionFields.Add(new PaletteDropdown(true) { Caption = @"colours of this section", HintText = @"Leave on the level's palette, or give this section its own.", Current = palette });
        }

        private void rebuildLevelFields()
        {
            levelFields.Clear();

            var d = DesignSession.Design;

            var palette = new Bindable<string>(d.Palette);
            var sparkle = new BindableFloat(d.Sparkle) { MinValue = 0, MaxValue = 2, Precision = 0.1f };
            var glow = new BindableFloat(d.Glow) { MinValue = 0, MaxValue = 2, Precision = 0.1f };
            var names = new BindableBool(d.ShowSectionNames);
            var picture = new BindableFloat(d.Picture) { MinValue = 0, MaxValue = 1, Precision = 0.05f };
            var preview = new PaletteCard();

            palette.BindValueChanged(v =>
            {
                editLevel(x => x.Palette = v.NewValue);
                preview.Show(KanPalette.Get(v.NewValue));
            });
            sparkle.BindValueChanged(v => editLevel(x => x.Sparkle = v.NewValue));
            glow.BindValueChanged(v => editLevel(x => x.Glow = v.NewValue));
            names.BindValueChanged(v => editLevel(x => x.ShowSectionNames = v.NewValue));
            picture.BindValueChanged(v => editLevel(x => x.Picture = v.NewValue));
            preview.Show(KanPalette.Get(d.Palette));

            levelFields.AddRange(new Drawable[]
            {
                new PaletteDropdown(false) { Caption = @"palette", HintText = @"The notes, background, trail and the hot line. Grades keep their colours in every palette.", Current = palette },
                preview,
                new FormSliderBar<float> { Caption = @"sparkle", HintText = @"Hit effects: 0 = none, 1 = normal, 2 = double.", Current = sparkle, KeyboardStep = 0.1f },
                new FormSliderBar<float> { Caption = @"glow", HintText = @"How much playing well lights the screen: 0 = none, 1 = normal, 2 = double.", Current = glow, KeyboardStep = 0.1f },
                new FormSliderBar<float>
                {
                    Caption = @"picture",
                    HintText = @"How much of the level's background picture or video shows behind the staff: 0 = none, 1 = all. Playing well and rests show a little more.",
                    Current = picture,
                    KeyboardStep = 0.05f,
                },
                new FormCheckBox { Caption = @"show section names", HintText = @"Each section's name as it starts (automatic designs name only the climax).", Current = names },
            });
        }

        private partial class KindDropdown : FormDropdown<SectionKind>
        {
            public KindDropdown()
            {
                Items = Enum.GetValues<SectionKind>();
            }

            protected override LocalisableString GenerateItemText(SectionKind item) => item switch
            {
                SectionKind.PreChorus => @"pre-chorus",
                SectionKind.Break => @"break — nothing to play",
                _ => item.ToString().ToLowerInvariant(),
            };
        }

        private partial class RestStyleDropdown : FormDropdown<BreakStyle>
        {
            public RestStyleDropdown()
            {
                Items = Enum.GetValues<BreakStyle>();
            }

            protected override LocalisableString GenerateItemText(BreakStyle item) => item switch
            {
                BreakStyle.Countdown => @"the music as light, then 3-2-1 to the next note",
                BreakStyle.Visualizer => @"the music as light",
                _ => @"a calm, empty screen",
            };
        }

        private partial class PaletteDropdown : FormDropdown<string>
        {
            private readonly bool allowLevel;

            public PaletteDropdown(bool allowLevel)
            {
                this.allowLevel = allowLevel;
                var names = KanPalette.Presets.Select(p => p.Name);
                Items = allowLevel ? names.Prepend(string.Empty).ToArray() : names.ToArray();
            }

            protected override LocalisableString GenerateItemText(string item)
                => allowLevel && string.IsNullOrEmpty(item) ? @"the level's palette" : KanPalette.Get(item).Title;
        }

        /// <summary>A small picture of a palette: the background, a few notes, the hot line.</summary>
        private partial class PaletteCard : CompositeDrawable
        {
            private readonly Box background;
            private readonly Box line;
            private readonly Circle[] notes = new Circle[4];

            public PaletteCard()
            {
                RelativeSizeAxes = Axes.X;
                Height = 70;
                Masking = true;
                CornerRadius = 6;

                InternalChildren = new Drawable[]
                {
                    background = new Box { RelativeSizeAxes = Axes.Both },
                    line = new Box { RelativePositionAxes = Axes.X, X = 0.2f, RelativeSizeAxes = Axes.Y, Width = 4, Origin = Anchor.TopCentre },
                };

                for (int i = 0; i < notes.Length; i++)
                {
                    AddInternal(notes[i] = new Circle
                    {
                        RelativePositionAxes = Axes.Both,
                        Position = new Vector2(0.32f + 0.17f * i, 0.3f + 0.13f * (i % 3)),
                        Origin = Anchor.Centre,
                        Size = new Vector2(22),
                        BorderColour = Color4.White,
                        BorderThickness = 1.5f,
                        Masking = true,
                    });
                }
            }

            public void Show(KanPalette p)
            {
                Color4 hex(string h) => Color4Extensions.FromHex(h);

                background.Colour = ColourInfo.GradientVertical(hex(p.BackgroundLit), hex(p.BackgroundDark));
                line.Colour = hex(p.Hot);

                foreach (var n in notes)
                    n.Colour = ColourInfo.GradientVertical(hex(p.NoteLight), hex(p.Note));
            }
        }

        // ------------------------------------------------------------------
        // Timeline
        // ------------------------------------------------------------------

        /// <summary>The level's sections over its notes; click to select and seek, drag a boundary to move it.</summary>
        private partial class Timeline : CompositeDrawable
        {
            private const float boundary_grab_px = 7;

            private readonly Action<DesignSection?> select;
            private readonly Action changed;

            private readonly Container sections = new Container { RelativeSizeAxes = Axes.Both };
            private readonly Container notes = new Container { RelativeSizeAxes = Axes.Both, Padding = new MarginPadding { Vertical = 34 } };
            private readonly Box playhead;

            [Resolved]
            private EditorClock clock { get; set; } = null!;

            [Resolved]
            private OverlayColourProvider colourProvider { get; set; } = null!;

            public double Playhead;
            public double LengthMs;

            private DesignSection? selected;
            private double drawnLength = -1;
            private bool invalid = true;
            private DesignSection? dragging;

            public DesignSection? Selected
            {
                get => selected;
                set
                {
                    selected = value;
                    invalid = true;
                }
            }

            public Timeline(Action<DesignSection?> select, Action changed)
            {
                this.select = select;
                this.changed = changed;
                RelativeSizeAxes = Axes.Both;

                InternalChildren = new Drawable[]
                {
                    sections,
                    notes,
                    playhead = new Box { RelativeSizeAxes = Axes.Y, Width = 2, Origin = Anchor.TopCentre, Colour = Color4.White },
                };
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                AddInternal(new Box { RelativeSizeAxes = Axes.Both, Colour = colourProvider.Background5, Depth = float.MaxValue });
            }

            public void Invalidate() => invalid = true;

            private float xOf(double ms) => LengthMs > 0 ? (float)(ms / LengthMs) * DrawWidth : 0;

            private double msAt(float x) => LengthMs > 0 ? Math.Clamp(x / DrawWidth, 0, 1) * LengthMs : 0;

            protected override void Update()
            {
                base.Update();

                if (invalid || drawnLength != LengthMs)
                    rebuild();

                playhead.X = xOf(Playhead);
            }

            private void rebuild()
            {
                invalid = false;
                drawnLength = LengthMs;
                sections.Clear();
                notes.Clear();

                if (LengthMs <= 0) return;

                var design = DesignSession.Design;
                var level = KanPalette.Get(design.Palette);

                foreach (var s in design.Sections)
                {
                    var p = KanPalette.Get(s.Palette ?? level.Name);
                    Color4 colour = s.IsRest ? new Color4(0.35f, 0.35f, 0.42f, 1)
                        : s.Climax ? Color4Extensions.FromHex(p.Climax)
                        : Color4Extensions.FromHex(p.Note).Darken(0.6f);
                    bool isSelected = ReferenceEquals(s, selected);

                    sections.Add(new Container
                    {
                        RelativePositionAxes = Axes.X,
                        RelativeSizeAxes = Axes.Both,
                        X = (float)(s.StartMs / LengthMs),
                        Width = (float)(s.DurationMs / LengthMs),
                        Padding = new MarginPadding { Horizontal = 1 },
                        Child = new Container
                        {
                            RelativeSizeAxes = Axes.Both,
                            Masking = true,
                            CornerRadius = 4,
                            BorderThickness = isSelected ? 3 : 0,
                            BorderColour = Color4.White,
                            Children = new Drawable[]
                            {
                                new Box { RelativeSizeAxes = Axes.Both, Colour = colour, Alpha = isSelected ? 0.95f : 0.7f },
                                new TruncatingSpriteText
                                {
                                    Text = s.DisplayName + (s.Climax ? @" ★" : string.Empty),
                                    Font = OsuFont.GetFont(size: 13, weight: FontWeight.Bold),
                                    Padding = new MarginPadding(6),
                                    RelativeSizeAxes = Axes.X,
                                },
                            },
                        },
                    });
                }

                // The notes, as a tiny piano roll: where the music is.
                var score = ScoreSession.Score.Notes;

                if (score.Count > 0)
                {
                    int lo = score.Min(n => n.Midi), hi = Math.Max(lo + 1, score.Max(n => n.Midi));

                    foreach (var n in score)
                    {
                        notes.Add(new Box
                        {
                            RelativePositionAxes = Axes.Both,
                            X = (float)(n.StartMs / LengthMs),
                            Y = 1 - (float)(n.Midi - lo) / (hi - lo),
                            Width = Math.Max(2, xOf(n.DurationMs)),
                            Height = 3,
                            Origin = Anchor.CentreLeft,
                            Colour = Color4.White,
                            Alpha = 0.75f,
                        });
                    }
                }
            }

            /// <summary>The section whose end is under <paramref name="x"/> (a boundary to drag), if any.</summary>
            private DesignSection? boundaryAt(float x)
            {
                var list = DesignSession.Design.Sections;

                for (int i = 0; i < list.Count - 1; i++)
                {
                    if (Math.Abs(xOf(list[i].EndMs) - x) <= boundary_grab_px)
                        return list[i];
                }

                return null;
            }

            protected override bool OnHover(HoverEvent e) => true;

            protected override bool OnMouseDown(MouseDownEvent e) => true;

            protected override bool OnClick(ClickEvent e)
            {
                double ms = msAt(ToLocalSpace(e.ScreenSpaceMousePosition).X);
                clock.SeekSmoothlyTo(ms);
                select(DesignSession.Design.SectionAt(ms));
                return true;
            }

            protected override bool OnDragStart(DragStartEvent e)
            {
                dragging = boundaryAt(ToLocalSpace(e.ScreenSpaceMouseDownPosition).X);
                if (dragging == null) return false;

                DesignSession.Record();
                DesignSession.Design.Generated = false;
                return true;
            }

            protected override void OnDrag(DragEvent e)
            {
                if (dragging == null) return;

                DesignSession.Design.MoveBoundary(dragging, msAt(ToLocalSpace(e.ScreenSpaceMousePosition).X));
                changed();
            }

            protected override void OnDragEnd(DragEndEvent e)
            {
                dragging = null;
                changed();
            }
        }
    }
}
