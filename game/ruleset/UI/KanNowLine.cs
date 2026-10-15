// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Utils;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osuTK;
using osuTK.Graphics;
using Kan.Forge.Play;

namespace Kan.Ruleset.UI
{
    /// <summary>
    /// The "now" line as the game's reward (owner 2026-10-15: "one of our
    /// best resources to feed the dopamine loop: effects, colours, impact,
    /// rewards"). Every hit lands on it: a burst where the note met the line
    /// (ring, sparks, a flash), bigger the better the hit. A streak heats the
    /// line up — wider, golden, glowing — with a shockwave at each milestone;
    /// a held note burns at the line while its pitch is kept; a miss cools it
    /// at once with a red jolt. Sparks come from a fixed pool.
    /// </summary>
    public partial class KanNowLine : CompositeDrawable
    {
        private const int spark_pool = 160;
        private const int burst_pool = 8;

        /// <summary>The streak at which the line is fully hot.</summary>
        private const float full_heat_streak = 32;

        private static readonly int[] milestones = { 10, 25, 50, 75, 100, 150, 200, 300, 400, 500 };

        private readonly Color4 hot;
        private readonly Container group;
        private readonly Container glow;
        private readonly Box core;
        private readonly Box shockwave;
        private readonly Circle holdGlow;
        private readonly OsuSpriteText milestoneText;
        private readonly Circle[] sparks = new Circle[spark_pool];
        private readonly Burst[] bursts = new Burst[burst_pool];
        private readonly Random random = new Random(1);

        private int nextSpark, nextBurst;
        private float heat, targetHeat;
        private double nextHoldSpark;

        /// <summary>The music's bloom (0..1): the line glows with it.</summary>
        public double Bloom;

        public KanNowLine(float nowFraction, Color4 hot)
        {
            this.hot = hot;
            RelativeSizeAxes = Axes.Both;

            InternalChild = group = new Container
            {
                RelativePositionAxes = Axes.X,
                X = nowFraction,
                RelativeSizeAxes = Axes.Y,
                Children = new Drawable[]
                {
                    shockwave = new Box
                    {
                        RelativeSizeAxes = Axes.Y,
                        Origin = Anchor.TopCentre,
                        Blending = BlendingParameters.Additive,
                        Colour = hot,
                        Alpha = 0,
                    },
                    // A soft column: fades out to both sides.
                    glow = new Container
                    {
                        RelativeSizeAxes = Axes.Y,
                        Width = 40,
                        Origin = Anchor.TopCentre,
                        Colour = Color4.White,
                        Blending = BlendingParameters.Additive,
                        Alpha = 0.05f,
                        Children = new Drawable[]
                        {
                            new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Width = 0.5f,
                                Colour = ColourInfo.GradientHorizontal(Color4.White.Opacity(0), Color4.White),
                            },
                            new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Width = 0.5f,
                                Anchor = Anchor.TopRight,
                                Origin = Anchor.TopRight,
                                Colour = ColourInfo.GradientHorizontal(Color4.White, Color4.White.Opacity(0)),
                            },
                        },
                    },
                    core = new Box
                    {
                        RelativeSizeAxes = Axes.Y,
                        Width = 3,
                        Origin = Anchor.TopCentre,
                        Colour = Color4.White,
                        Alpha = 0.7f,
                    },
                    holdGlow = new Circle
                    {
                        Origin = Anchor.Centre,
                        Size = new Vector2(44),
                        Blending = BlendingParameters.Additive,
                        Alpha = 0,
                    },
                    // Low on the line: the top belongs to the score.
                    milestoneText = new OsuSpriteText
                    {
                        Anchor = Anchor.BottomLeft,
                        Origin = Anchor.BottomCentre,
                        Y = -120,
                        Font = OsuFont.GetFont(size: 26, weight: FontWeight.Black),
                        Colour = hot,
                        Alpha = 0,
                    },
                },
            };

            for (int i = 0; i < burst_pool; i++)
                group.Add(bursts[i] = new Burst());

            for (int i = 0; i < spark_pool; i++)
                group.Add(sparks[i] = new Circle { Origin = Anchor.Centre, Blending = BlendingParameters.Additive, Alpha = 0 });
        }

        protected override void Update()
        {
            base.Update();

            // Heat follows the streak, easing up and (after a miss) snapping down.
            heat += (targetHeat - heat) * (float)Math.Min(1, Time.Elapsed / (targetHeat > heat ? 350 : 120));

            // At rest (no pulse running) the line shows its heat.
            if (core.LatestTransformEndTime <= Time.Current)
            {
                core.Width = restingWidth;
                core.Colour = restingColour;
                core.Alpha = 0.7f + 0.3f * heat;
            }

            restingGlowAlpha = (float)(0.03 + 0.09 * Bloom) + 0.16f * heat;
            glow.Width = 40 + 90 * heat;

            if (glow.LatestTransformEndTime <= Time.Current)
            {
                glow.Alpha = restingGlowAlpha;
                glow.Colour = restingColour;
            }
        }

        private float restingGlowAlpha;

        private float restingWidth => 3 + 3.5f * heat;

        private Color4 restingColour => Interpolation.ValueAt(heat, Color4.White, hot, 0, 1);

        /// <summary>A judgement reached the line at height <paramref name="y"/>.</summary>
        public void Judged(LiveResult result, float y, Color4 colour, int streak)
        {
            if (result == LiveResult.Miss)
            {
                miss(y, colour);
                return;
            }

            targetHeat = Math.Min(1, streak / full_heat_streak);

            float power = result switch
            {
                LiveResult.Great => 1f,
                LiveResult.Ok => 0.65f,
                _ => 0.4f,
            };

            // The line kicks: wider, the hit's colour, then back to its heat.
            core.ClearTransforms();
            core.Width = restingWidth + 7 * power;
            core.Colour = colour;
            core.Alpha = 1;
            core.ResizeWidthTo(restingWidth + 0.01f, 420, Easing.OutElastic)
                .FadeColour(restingColour, 380, Easing.OutQuint)
                .FadeTo(0.7f + 0.3f * heat, 380);

            glow.ClearTransforms();
            glow.Colour = colour;
            glow.Alpha = Math.Max(restingGlowAlpha, 0.22f + 0.25f * power);
            glow.FadeTo(restingGlowAlpha, 450, Easing.OutQuint).FadeColour(restingColour, 450);

            bursts[nextBurst++ % burst_pool].Fire(y, colour, power, result == LiveResult.Great);

            int count = result switch
            {
                LiveResult.Great => 14,
                LiveResult.Ok => 8,
                _ => 4,
            };

            for (int i = 0; i < count; i++)
            {
                double angle = random.NextDouble() * Math.PI * 2;
                float distance = (40 + (float)random.NextDouble() * 80) * (0.6f + 0.6f * power);
                spark(new Vector2(0, y), new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * distance,
                    colour, 3 + (float)random.NextDouble() * 3 * (0.5f + power), 320 + random.NextDouble() * 260);
            }

            if (Array.IndexOf(milestones, streak) >= 0 || (streak > 500 && streak % 100 == 0))
                milestone(streak);
        }

        /// <summary>
        /// Called every frame: the pitch of a note being held right (null when
        /// none) burns at the line.
        /// </summary>
        public void Holding(float? y, Color4 colour, double now)
        {
            if (y == null)
            {
                if (holdGlow.Alpha > 0 && holdGlow.LatestTransformEndTime <= Time.Current)
                    holdGlow.FadeOut(150);
                return;
            }

            holdGlow.ClearTransforms();
            holdGlow.Y = y.Value;
            holdGlow.Colour = colour;
            float pulse = 0.5f + 0.5f * MathF.Sin((float)(now / 90));
            holdGlow.Alpha = 0.35f + 0.25f * pulse + 0.2f * heat;
            holdGlow.Size = new Vector2(38 + 10 * pulse + 14 * heat);

            // The sustain sheds sparks that drift off with the music.
            if (Time.Current >= nextHoldSpark)
            {
                nextHoldSpark = Time.Current + 45 - 20 * heat;
                float dy = ((float)random.NextDouble() - 0.5f) * 30;
                spark(new Vector2(0, y.Value + dy * 0.3f), new Vector2(-40 - (float)random.NextDouble() * 50, dy),
                    colour, 2.5f + (float)random.NextDouble() * 2.5f, 380 + random.NextDouble() * 200);
            }
        }

        private void miss(float y, Color4 red)
        {
            targetHeat = 0;
            heat = Math.Min(heat, 0.15f); // cools at once

            core.ClearTransforms();
            core.Colour = red;
            core.Alpha = 1;
            core.Width = 4;
            core.FadeColour(Color4.White, 450, Easing.OutQuint).FadeTo(0.7f, 450);

            // A jolt: the line shakes.
            core.MoveToX(5, 30).Then().MoveToX(-4, 40).Then().MoveToX(2, 40).Then().MoveToX(0, 60);

            glow.ClearTransforms();
            glow.Colour = red;
            glow.Alpha = 0.45f;
            glow.FadeTo(restingGlowAlpha, 400).FadeColour(Color4.White, 400);

            for (int i = 0; i < 5; i++)
            {
                // A few dull sparks fall away.
                spark(new Vector2(0, y), new Vector2(((float)random.NextDouble() - 0.5f) * 40, 30 + (float)random.NextDouble() * 40),
                    red.Darken(0.5f), 3, 420);
            }
        }

        private void milestone(int streak)
        {
            shockwave.ClearTransforms();
            shockwave.Width = 8;
            shockwave.Alpha = 0.4f;
            shockwave.ResizeWidthTo(900, 650, Easing.OutQuint).FadeOut(650, Easing.OutQuad);

            milestoneText.ClearTransforms();
            milestoneText.Text = $"{streak} in a row";
            milestoneText.Alpha = 1;
            milestoneText.Scale = new Vector2(1.6f);
            milestoneText.ScaleTo(1, 400, Easing.OutBack).Then(500).FadeOut(400);

            for (int i = 0; i < 24; i++)
            {
                float yy = (float)random.NextDouble() * DrawHeight;
                spark(new Vector2(0, yy), new Vector2(((float)random.NextDouble() - 0.5f) * 260, ((float)random.NextDouble() - 0.5f) * 40),
                    hot, 3 + (float)random.NextDouble() * 3, 500 + random.NextDouble() * 300);
            }
        }

        private void spark(Vector2 from, Vector2 travel, Color4 colour, float size, double duration)
        {
            var s = sparks[nextSpark++ % spark_pool];
            s.ClearTransforms();
            s.Position = from;
            s.Size = new Vector2(size);
            s.Colour = colour;
            s.Alpha = 1;
            s.Scale = Vector2.One;
            s.MoveTo(from + travel, duration, Easing.OutQuint)
             .ScaleTo(0.2f, duration, Easing.InQuad)
             .FadeOut(duration, Easing.InQuad);
        }

        /// <summary>The impact where a note met the line: an expanding ring and a flash.</summary>
        private partial class Burst : CompositeDrawable
        {
            private readonly CircularContainer ring;
            private readonly Circle flash;

            public Burst()
            {
                // A point on the line; the ring and the flash are centred on it.
                Size = Vector2.Zero;

                InternalChildren = new Drawable[]
                {
                    ring = new CircularContainer
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Masking = true,
                        BorderThickness = 4,
                        Blending = BlendingParameters.Additive,
                        Alpha = 0,
                        Child = new Box { RelativeSizeAxes = Axes.Both, Alpha = 0, AlwaysPresent = true },
                    },
                    flash = new Circle
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Blending = BlendingParameters.Additive,
                        Colour = Color4.White,
                        Alpha = 0,
                    },
                };
            }

            public void Fire(float y, Color4 colour, float power, bool great)
            {
                Y = y;

                ring.ClearTransforms();
                ring.BorderColour = colour;
                ring.BorderThickness = 3 + 4 * power;
                ring.Size = new Vector2(30);
                ring.Alpha = 0.9f;
                ring.ResizeTo(30 + 90 * power, 420, Easing.OutQuint)
                    .TransformTo(nameof(CircularContainer.BorderThickness), 0.5f, 420, Easing.OutQuad)
                    .FadeOut(420, Easing.InQuad);

                flash.ClearTransforms();

                if (!great) return;

                flash.Size = new Vector2(34);
                flash.Alpha = 0.85f;
                flash.ResizeTo(56, 180, Easing.OutQuint).FadeOut(180, Easing.OutQuad);
            }
        }
    }
}
