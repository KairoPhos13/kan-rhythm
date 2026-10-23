// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using osu.Framework.Audio.Track;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osuTK;
using osuTK.Graphics;
using Kan.Forge.Design;

namespace Kan.Ruleset.UI
{
    /// <summary>
    /// What fills a rest (owner 2026-10-15: "where nothing is played the
    /// visualizations should change — hide them, or fill the space with
    /// something more dynamic"): the companion as light — its spectrum in
    /// mirrored bars and a soft core that breathes with it — in the level's
    /// colours, plus what comes next. Shown as the scene rests, unless the
    /// section asks for a calm screen (<see cref="BreakStyle.Hide"/>).
    /// </summary>
    public partial class KanInterlude : CompositeDrawable
    {
        private const int bar_count = 56;

        /// <summary>The spectrum's bins shown (of 256): the musical range, low to high.</summary>
        private const int first_bin = 1, last_bin = 160;

        private readonly KanScene scene;
        private readonly KanPlayState state;
        private readonly Func<ITrack?> track;
        private readonly Box[] bars = new Box[bar_count];
        private readonly float[] levels = new float[bar_count];

        private int shownSeconds = -1, shownNote = -1;
        private readonly Container barRow;
        private readonly Circle core;
        private readonly OsuSpriteText upNext;

        private float loudness;

        public KanInterlude(KanScene scene, KanPlayState state, Func<ITrack?> track)
        {
            this.scene = scene;
            this.state = state;
            this.track = track;
            RelativeSizeAxes = Axes.Both;
            Alpha = 0;
            // Hidden most of the time, but it must keep updating to notice a rest.
            AlwaysPresent = true;

            InternalChildren = new Drawable[]
            {
                core = new Circle
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Blending = BlendingParameters.Additive,
                    Size = new Vector2(220),
                },
                barRow = new Container
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    RelativeSizeAxes = Axes.X,
                    Width = 0.7f,
                    Height = 1,
                },
                upNext = new OsuSpriteText
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.TopCentre,
                    Y = 150,
                    Font = OsuFont.GetFont(size: 20, weight: FontWeight.SemiBold),
                    Shadow = true,
                },
            };

            for (int i = 0; i < bar_count; i++)
            {
                barRow.Add(bars[i] = new Box
                {
                    RelativePositionAxes = Axes.X,
                    X = (i + 0.5f) / bar_count,
                    Origin = Anchor.Centre,
                    Anchor = Anchor.CentreLeft,
                    Width = 6,
                    Blending = BlendingParameters.Additive,
                });
            }
        }

        protected override void Update()
        {
            base.Update();

            var section = scene.Section;
            bool show = section == null || section.BreakStyle != BreakStyle.Hide;
            Alpha = show ? scene.Rest : 0;

            if (Alpha <= 0.001f) return;

            readSpectrum();

            float maxHeight = DrawHeight * 0.32f;
            var colour = ColourInfo.GradientVertical(scene.Hot, scene.Note);

            for (int i = 0; i < bar_count; i++)
            {
                bars[i].Height = 4 + levels[i] * maxHeight;
                bars[i].Width = Math.Max(3, barRow.DrawWidth / bar_count * 0.55f);
                bars[i].Colour = colour;
                bars[i].Alpha = 0.35f + 0.65f * Math.Min(1, levels[i] * 2);
            }

            core.Colour = ColourInfo.GradientVertical(scene.NoteLight.Opacity(0.5f), scene.Note.Opacity(0));
            core.Alpha = 0.12f + 0.35f * loudness;
            core.Size = new Vector2(180 + 160 * loudness);

            double toGo = scene.NextNoteMs - Clock.CurrentTime;

            if (scene.NextNote >= 0 && double.IsFinite(toGo))
            {
                var n = state.Notes[scene.NextNote];
                int seconds = (int)Math.Ceiling(toGo / 1000);

                // Only when it reads differently: a new string every frame is garbage.
                if (seconds != shownSeconds || n.Index != shownNote)
                {
                    shownSeconds = seconds;
                    shownNote = n.Index;
                    upNext.Text = $"next: {(n.PitchUnknown ? "?" : KanStaff.NoteName(n.Midi))} in {seconds} s";
                }
                upNext.Colour = scene.NoteLight;
                upNext.Alpha = 0.75f;
            }
            else
                upNext.Alpha = 0;
        }

        /// <summary>The companion's spectrum, on a log scale (each bar an equal musical step), falling smoothly.</summary>
        private void readSpectrum()
        {
            var t = track();
            ReadOnlySpan<float> amplitudes = t != null ? t.CurrentAmplitudes.FrequencyAmplitudes.Span : ReadOnlySpan<float>.Empty;
            float decay = (float)Math.Min(1, Math.Abs(Time.Elapsed) / 220);
            float total = 0;

            for (int i = 0; i < bar_count; i++)
            {
                // Mirrored: the low end in the middle, highs to both sides.
                int fromCentre = Math.Abs(i - bar_count / 2);
                float f = (float)fromCentre / (bar_count / 2);
                int bin = (int)(first_bin * Math.Pow((double)last_bin / first_bin, f));
                float v = amplitudes.Length > bin ? Math.Min(1, amplitudes[bin] * (2 + 6 * f)) : 0;

                levels[i] = v > levels[i] ? v : levels[i] - (levels[i] - v) * decay;
                total += levels[i];
            }

            float target = Math.Min(1, total / bar_count * 2.2f);
            loudness += (target - loudness) * (float)Math.Min(1, Math.Abs(Time.Elapsed) / 120);
        }
    }

    /// <summary>A section's name as it starts ("Chorus"), low key unless it is the climax.</summary>
    public partial class KanSectionBanner : CompositeDrawable
    {
        private readonly KanScene scene;
        private readonly OsuSpriteText title;
        private readonly Box underline;

        public KanSectionBanner(KanScene scene)
        {
            this.scene = scene;
            RelativeSizeAxes = Axes.Both;

            InternalChildren = new Drawable[]
            {
                title = new OsuSpriteText
                {
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                    Y = 130,
                    Font = OsuFont.GetFont(size: 34, weight: FontWeight.Black),
                    Spacing = new Vector2(6, 0),
                    Shadow = true,
                    Alpha = 0,
                },
                underline = new Box
                {
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                    Y = 176,
                    Height = 3,
                    Width = 0,
                    Alpha = 0,
                },
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            scene.SectionStarted += onSection;
        }

        private void onSection(DesignSection section)
        {
            if (!scene.Design.ShowSectionNames || section.IsRest || section.DurationMs < 5000) return;

            // An auto-designed level's sections are only guesses: name the climax only.
            if (scene.Design.Generated && !section.Climax) return;

            Color4 colour = section.Climax ? scene.Hot : scene.NoteLight;

            title.ClearTransforms();
            title.Text = section.DisplayName.ToUpperInvariant();
            title.Colour = colour;
            title.Alpha = 0;
            title.Spacing = new Vector2(18, 0);
            title.FadeIn(250).TransformSpacingTo(new Vector2(6, 0), 700, Easing.OutQuint)
                 .Then(900).FadeOut(500);

            underline.ClearTransforms();
            underline.Colour = colour;
            underline.Alpha = 0.8f;
            underline.Width = 0;
            underline.ResizeWidthTo(section.Climax ? 360 : 200, 600, Easing.OutQuint)
                     .Then(900).FadeOut(500);
        }

        protected override void Dispose(bool isDisposing)
        {
            scene.SectionStarted -= onSection;
            base.Dispose(isDisposing);
        }
    }
}
