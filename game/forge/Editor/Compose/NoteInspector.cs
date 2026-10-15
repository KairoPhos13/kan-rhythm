// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Localisation;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Overlays;
using osuTK;
using Kan.Forge.Score;

namespace Kan.Forge.Editor.Compose
{
    /// <summary>
    /// The selected notes' properties — what gameplay judges about each note:
    /// pitch (and intonation in cents), timing, duration, intensity and
    /// playing style. With several notes, pitch and start shift them all;
    /// the other values set them all.
    /// </summary>
    internal partial class NoteInspector : CompositeDrawable
    {
        private readonly PianoRoll roll;
        private readonly Action<string> beforeChange;
        private readonly Action changed;

        [Resolved]
        private OverlayColourProvider colourProvider { get; set; } = null!;

        private FillFlowContainer fields = null!;
        private OsuSpriteText title = null!;
        private string? drawnSelection;

        /// <param name="beforeChange">Record undo before a change (key merges a slider drag into one step).</param>
        private readonly Drawable? top;

        /// <param name="top">Shown above the note properties (the section tools).</param>
        public NoteInspector(PianoRoll roll, Action<string> beforeChange, Action changed, Drawable? top = null)
        {
            this.top = top;
            this.roll = roll;
            this.beforeChange = beforeChange;
            this.changed = changed;

            RelativeSizeAxes = Axes.Y;
            Width = 270;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            InternalChildren = new Drawable[]
            {
                new osu.Framework.Graphics.Shapes.Box { RelativeSizeAxes = Axes.Both, Colour = colourProvider.Background5 },
                new OsuScrollContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    Child = new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Vertical,
                        Spacing = new Vector2(0, 6),
                        Padding = new MarginPadding(10),
                        Children = new Drawable[]
                        {
                            top ?? Empty(),
                            new osu.Framework.Graphics.Shapes.Box
                            {
                                RelativeSizeAxes = Axes.X,
                                Height = 1,
                                Colour = colourProvider.Background3,
                                Margin = new MarginPadding { Vertical = 8 },
                                Alpha = top == null ? 0 : 1,
                            },
                            title = new OsuSpriteText { Font = OsuFont.GetFont(size: 15, weight: FontWeight.Bold) },
                            fields = new FillFlowContainer
                            {
                                RelativeSizeAxes = Axes.X,
                                AutoSizeAxes = Axes.Y,
                                Direction = FillDirection.Vertical,
                                Spacing = new Vector2(0, 5),
                            },
                            new osu.Framework.Graphics.Shapes.Box
                            {
                                RelativeSizeAxes = Axes.X,
                                Height = 1,
                                Colour = colourProvider.Background3,
                                Margin = new MarginPadding { Vertical = 8 },
                            },
                            new RangeFilterPanel(roll, () => beforeChange("range"), changed),
                        },
                    },
                },
            };
        }

        /// <summary>Re-read the selection (after undo or an outside change).</summary>
        public void Invalidate() => drawnSelection = null;

        protected override void Update()
        {
            base.Update();

            string key = string.Join(',', roll.Selected.Select(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode).OrderBy(h => h))
                         + "#" + ScoreSession.Score.GetHashCode();

            if (key == drawnSelection) return;

            drawnSelection = key;
            rebuild();
        }

        private List<ScoreNote> selection => roll.Selected.OrderBy(n => n.StartMs).ToList();

        private void rebuild()
        {
            fields.Clear();
            var notes = selection;

            if (notes.Count == 0)
            {
                title.Text = @"No note selected";
                fields.Add(new OsuTextFlowContainer(t =>
                {
                    t.Font = OsuFont.Style.Caption1;
                    t.Colour = colourProvider.Content2;
                })
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Text = "Click a note to see what gameplay will judge: its pitch, timing, duration, intensity and playing style.\n\n"
                           + "Select: click, Ctrl+click, or drag a box. Move: drag the notes (Shift = off the grid). "
                           + "Resize: drag a note's right edge. Draw tool: click to add. Delete removes, arrows nudge.",
                });
                return;
            }

            var first = notes[0];
            title.Text = notes.Count == 1 ? $"Note {KanScore.NoteName(first.Midi)}" : $"{notes.Count} notes";
            if (notes.Any(n => n.PitchUnknown))
                title.Text += @" · pitch unknown — set it below";

            var pitch = new Bindable<string>(KanScore.NoteName(first.Midi));
            pitch.BindValueChanged(v =>
            {
                if (!KanScore.TryParseNote(v.NewValue, out int midi)) return;

                beforeChange("pitch");
                ScoreSession.Score.Move(notes, 0, midi - notes[0].Midi);
                foreach (var n in notes) n.PitchUnknown = false; // set by hand, even if unchanged
                done();
            });

            var start = new Bindable<string>(ForgeFormat.Seconds(first.StartMs / 1000));
            start.BindValueChanged(v =>
            {
                if (!ForgeFormat.TryParseSeconds(v.NewValue, out double s)) return;

                beforeChange("start");
                ScoreSession.Score.Move(notes, s * 1000 - notes[0].StartMs, 0);
                done();
            });

            var duration = new Bindable<string>(Math.Round(first.DurationMs).ToString(System.Globalization.CultureInfo.InvariantCulture));
            duration.BindValueChanged(v =>
            {
                if (!double.TryParse(v.NewValue, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double ms)) return;

                beforeChange("duration");
                foreach (var n in notes) n.DurationMs = Math.Max(KanScore.MIN_DURATION_MS, ms);
                done();
            });

            var cents = new BindableFloat(first.Cents) { MinValue = -50, MaxValue = 50, Precision = 1 };
            cents.BindValueChanged(v =>
            {
                beforeChange("cents");
                foreach (var n in notes) n.Cents = v.NewValue;
                done();
            });

            var intensity = new BindableFloat(first.Intensity) { MinValue = 0, MaxValue = 1, Precision = 0.05f };
            intensity.BindValueChanged(v =>
            {
                beforeChange("intensity");
                foreach (var n in notes) n.Intensity = v.NewValue;
                done();
            });

            var articulation = new Bindable<Articulation>(first.Articulation);
            articulation.BindValueChanged(v =>
            {
                beforeChange("articulation");
                foreach (var n in notes) n.Articulation = v.NewValue;
                done();
            });

            var vibrato = new BindableBool(first.Vibrato);
            vibrato.BindValueChanged(v =>
            {
                beforeChange("vibrato");
                foreach (var n in notes) n.Vibrato = v.NewValue;
                done();
            });

            fields.AddRange(new Drawable[]
            {
                new FormTextBox
                {
                    Caption = notes.Count == 1 ? @"pitch" : @"pitch (first note; all move together)",
                    HintText = @"A note name such as G4, F#3 or Bb5.",
                    Current = pitch,
                },
                new FormTextBox
                {
                    Caption = notes.Count == 1 ? @"start (s)" : @"start (s) of the first note; all move together",
                    Current = start,
                },
                new FormTextBox
                {
                    Caption = @"duration (ms)",
                    Current = duration,
                },
                new FormSliderBar<float>
                {
                    Caption = @"intonation (cents)",
                    HintText = @"How far from the equal-tempered pitch the note is meant to be (0 = in tune).",
                    Current = cents,
                    KeyboardStep = 1,
                },
                new FormSliderBar<float>
                {
                    Caption = @"intensity",
                    HintText = @"How strongly the note is played: 0 = softest, 1 = loudest.",
                    Current = intensity,
                    KeyboardStep = 0.05f,
                },
                new ArticulationDropdown
                {
                    Caption = @"playing style",
                    HintText = @"How the note is started and how it ends. Hover the list to compare them.",
                    Current = articulation,
                },
                articulationHelp(articulation),
                new FormCheckBox
                {
                    Caption = @"vibrato",
                    HintText = @"A gentle, regular wobble in pitch while the note is held, like a singer's voice on a long note.",
                    Current = vibrato,
                },
            });
        }

        /// <summary>The chosen style in plain words, for players new to music terms.</summary>
        private Drawable articulationHelp(Bindable<Articulation> articulation)
        {
            var text = new OsuTextFlowContainer(t =>
            {
                t.Font = OsuFont.Style.Caption2;
                t.Colour = colourProvider.Content2;
            })
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Padding = new MarginPadding { Horizontal = 4 },
            };

            articulation.BindValueChanged(v => text.Text = ArticulationDropdown.Explain(v.NewValue), true);
            return text;
        }

        /// <summary>Playing styles named with a short meaning (owner 2026-10-14).</summary>
        private partial class ArticulationDropdown : FormDropdown<Articulation>
        {
            public ArticulationDropdown()
            {
                Items = Enum.GetValues<Articulation>();
            }

            protected override LocalisableString GenerateItemText(Articulation item) => item switch
            {
                Articulation.Legato => @"legato — joined to the previous note",
                Articulation.Staccato => @"staccato — short and detached",
                Articulation.Accent => @"accent — played stronger",
                _ => @"normal — a plain note",
            };

            public static string Explain(Articulation a) => a switch
            {
                Articulation.Legato => @"Legato: the note flows out of the previous one without a new start — on a wind instrument you change fingers without tonguing again; on a piano you hold the old key until the new one sounds.",
                Articulation.Staccato => @"Staccato: play the note short and let go early, leaving a small silence after it — a light, bouncy feel.",
                Articulation.Accent => @"Accent: start the note louder or with more attack than the ones around it, to make it stand out.",
                _ => @"Normal: start the note clearly and hold it for its full length.",
            };
        }

        private void done()
        {
            roll.Invalidate();
            changed();
        }
    }
}
