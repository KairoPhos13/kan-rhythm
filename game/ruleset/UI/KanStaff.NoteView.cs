// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Utils;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osuTK;
using osuTK.Graphics;
using Kan.Forge.Design;
using Kan.Forge.Play;
using Kan.Forge.Score;
using Kan.Ruleset.Objects;

namespace Kan.Ruleset.UI
{
    /// <summary>
    /// How the staff draws one note: its head, its length, its playing style, and once judged,
    /// its grade (timing on the ring, pitch inside).
    /// </summary>
    public partial class KanStaff : CompositeDrawable
    {
        /// <summary>What every note shares this frame.</summary>
        private readonly record struct NoteLook(Color4 Note, Color4 NoteLight, double Now, float Width, float NowX);

        /// <summary>One note on the staff (pooled; re-pointed at a note each frame).</summary>
        private partial class NoteView : CompositeDrawable
        {
            private readonly Box legato;
            private readonly Container tail;
            private readonly Box tailBody;
            private readonly Box tailFill;
            private readonly Box tailShine;
            private readonly OsuSpriteText tailMark;
            private readonly Circle glow;
            private readonly Container points;
            private readonly Triangle[] pointShapes = new Triangle[point_count];
            private readonly CircularContainer headCircle;

            /// <summary>A sharp's points around its bead (owner 2026-10-17: "so it is clear they are not the same note").</summary>
            private const int point_count = 8;

            /// <summary>How far a point sticks out of the bead (px, at the bead's own size).</summary>
            private const float point_length = 7;
            private readonly Box headFill;
            private readonly Circle flash;
            private readonly OsuSpriteText name;
            private readonly OsuSpriteText mark;
            private readonly CircularContainer ring;

            public NoteView()
            {
                RelativeSizeAxes = Axes.Both;
                Alpha = 0;

                for (int i = 0; i < point_count; i++)
                {
                    // From the centre outwards, apex out: the bead covers all but the tip.
                    pointShapes[i] = new Triangle
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.BottomCentre,
                        Rotation = (i + 0.5f) * 360f / point_count,
                    };
                }

                InternalChildren = new Drawable[]
                {
                    legato = new Box { Height = 3, Origin = Anchor.CentreLeft, Alpha = 0 },
                    tail = new Container
                    {
                        Height = tail_height,
                        Origin = Anchor.CentreLeft,
                        Masking = true,
                        CornerRadius = tail_height / 2,
                        Children = new Drawable[]
                        {
                            tailBody = new Box { RelativeSizeAxes = Axes.Both },
                            tailFill = new Box { RelativeSizeAxes = Axes.Y },
                            tailShine = new Box { RelativeSizeAxes = Axes.Y, Width = 40, Origin = Anchor.TopCentre, Blending = BlendingParameters.Additive },
                            tailMark = new OsuSpriteText
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                X = head / 2 + 4,
                                Font = OsuFont.GetFont(size: 14, weight: FontWeight.Bold),
                                Colour = Color4.White,
                                Alpha = 0.8f,
                            },
                        },
                    },
                    glow = new Circle
                    {
                        Origin = Anchor.Centre,
                        Blending = BlendingParameters.Additive,
                    },
                    points = new Container { Origin = Anchor.Centre, Alpha = 0, Children = pointShapes },
                    ring = new CircularContainer
                    {
                        Origin = Anchor.Centre,
                        Masking = true,
                        BorderThickness = 3,
                        BorderColour = Color4.White,
                        Child = new Box { RelativeSizeAxes = Axes.Both, Alpha = 0, AlwaysPresent = true },
                    },
                    headCircle = new CircularContainer
                    {
                        Origin = Anchor.Centre,
                        Size = new Vector2(head),
                        Masking = true,
                        BorderColour = Color4.White,
                        Children = new Drawable[]
                        {
                            headFill = new Box { RelativeSizeAxes = Axes.Both },
                            flash = new Circle { RelativeSizeAxes = Axes.Both, Colour = Color4.White, Alpha = 0 },
                        },
                    },
                    // White names, readable on every colour (owner 2026-10-15).
                    name = new OsuSpriteText
                    {
                        Origin = Anchor.Centre,
                        Font = OsuFont.GetFont(size: 14, weight: FontWeight.Black),
                        Colour = Color4.White,
                        Shadow = true,
                        ShadowColour = Color4.Black.Opacity(0.55f),
                        ShadowOffset = new Vector2(0, 0.06f),
                    },
                    mark = new OsuSpriteText
                    {
                        Origin = Anchor.BottomCentre,
                        Font = OsuFont.GetFont(size: 18, weight: FontWeight.Black),
                        Colour = Color4.White,
                        Shadow = true,
                    },
                };
            }

            public void Show(KanHitObject n, LiveVerdict? verdict, double sinceJudged, double heldUntil, KanHitObject? previous,
                             KanStaff staff, bool isNext, float emphasis, NoteLook look)
            {
                double now = look.Now;
                float x = staff.xAt(n.StartTime, now);
                float y = staff.yAt(n.Midi);
                bool staccato = n.Articulation == Articulation.Staccato;
                bool accent = n.Articulation == Articulation.Accent;
                bool missed = verdict is { Result: LiveResult.Miss };

                // Each note in its own colour when the player asked for it (KanNotation).
                Color4 noteColour = KanNotation.ColourByNote ? KanNotation.Colour(n.Midi) : look.Note;
                Color4 baseColour = n.PitchUnknown ? unknown_colour : noteColour;
                Color4 lightColour = n.PitchUnknown ? unknown_colour.Lighten(0.4f) : KanNotation.ColourByNote ? noteColour.Lighten(0.45f) : look.NoteLight;
                Color4 colour = baseColour, light = lightColour, ringColour = Color4.White;
                float pop = 0, flashAlpha = 0, shake = 0, drop = 0, scale = 1;
                bool perfect = verdict is { Result: LiveResult.Perfect };

                if (verdict != null)
                {
                    // Transitions play from the moment of the verdict (a seek lands on the end state).
                    double t = double.IsNaN(sinceJudged) ? 10_000 : Math.Max(0, sinceJudged);
                    float k = (float)Math.Clamp(t / 160, 0, 1);

                    if (missed)
                    {
                        colour = Interpolation.ValueAt(k, baseColour, miss_colour, 0, 1);
                        light = Interpolation.ValueAt(k, lightColour, miss_colour, 0, 1);
                    }
                    else
                    {
                        // Timing and pitch told apart (owner 2026-10-21: the player sees
                        // what they got right): the ring is the timing's grade, the inside
                        // white when the pitch was in tune, else the note's own colour.
                        var v = verdict.Value;
                        bool inTune = v.Reason == LiveReason.Played && Math.Abs(v.Cents) <= staff.state.Judge.PerfectCents; // the feedback's "sharp ↑ / flat ↓" bound

                        ringColour = Interpolation.ValueAt(k, Color4.White, GradeColour(v.Result), 0, 1);

                        if (inTune)
                        {
                            colour = Interpolation.ValueAt(k, baseColour, perfect ? perfect_glow : perfect_colour, 0, 1);
                            light = Interpolation.ValueAt(k, lightColour, Color4.White, 0, 1);
                        }
                    }

                    if (missed)
                    {
                        float m = (float)Math.Clamp(t / 380, 0, 1);
                        shake = MathF.Sin((float)t / 16) * 7 * (1 - m);
                        drop = 8 * m;
                        scale = 1 - 0.18f * m;
                    }
                    else
                    {
                        float p = (float)Math.Clamp(t / 300, 0, 1);
                        pop = 0.5f * (1 - p) * (1 - p);
                        flashAlpha = (float)Math.Clamp(1 - t / 220, 0, 1);
                        // Played notes settle a little smaller: history, not the task.
                        scale = 1 - 0.12f * p;
                    }
                }

                // Notes appear at the right edge (fade and grow in) and leave at the left.
                float enter = Math.Clamp((look.Width - x) / (look.Width * 0.1f), 0, 1);
                float leave = x < look.NowX ? Math.Clamp(0.3f + 0.7f * x / look.NowX, 0.3f, 1) : 1;

                Alpha = emphasis * enter * leave * (missed ? missed_alpha : 1);

                // Head: a lit bead, lighter at the top, with a soft halo.
                float size = head * (accent ? 1.22f : 1) * scale * (1 + pop) * (0.75f + 0.25f * enter);
                headCircle.Position = new Vector2(x + shake, y + drop);
                headCircle.Size = new Vector2(size);
                headFill.Colour = ColourInfo.GradientVertical(light, colour);
                flash.Alpha = flashAlpha;

                float pulse = isNext ? 0.5f + 0.5f * MathF.Sin((float)(now / 110)) : 0;
                headCircle.BorderColour = ringColour;
                headCircle.BorderThickness = isNext ? 3 + pulse : missed ? 0 : verdict != null ? 3.5f : 1.5f;

                // A sharp has points: told apart from its neighbours by its shape too, not only by "♯".
                bool sharp = !n.PitchUnknown && KanNotation.IsSharp(n.Midi);
                points.Alpha = sharp ? (missed ? 0.5f : 1) : 0;

                if (sharp)
                {
                    float length = size / 2 + point_length * size / head;
                    points.Position = headCircle.Position;
                    points.Size = new Vector2(size);

                    foreach (var t in pointShapes)
                    {
                        t.Size = new Vector2(length * 0.9f, length);
                        t.Colour = ColourInfo.GradientVertical(light, colour);
                    }
                }

                glow.Position = headCircle.Position;
                glow.Size = new Vector2(size * (isNext ? 2.1f + 0.3f * pulse : 1.7f) + pop * 40);
                glow.Colour = perfect ? perfect_glow : verdict != null && !missed ? ringColour : colour;
                // A perfect keeps a soft halo after its pop: it reads at a glance among the played notes.
                glow.Alpha = missed ? 0 : isNext ? 0.22f + 0.12f * pulse : verdict != null ? (perfect ? 0.3f : 0.1f) + 0.5f * pop : 0.1f;

                name.Position = headCircle.Position;
                name.Text = n.PitchUnknown ? @"?" : KanNotation.Name(n.Midi);
                name.Font = isNext ? next_name_font : name_font;
                name.Scale = new Vector2(scale * (1 + pop * 0.6f));
                name.Alpha = missed ? 0.6f : 1;

                // Style mark above the head: accent ">", staccato "•".
                mark.Position = new Vector2(x + shake, y + drop - size / 2 - 2);
                mark.Text = accent ? @">" : staccato ? @"•" : string.Empty;

                // Tail: how long it lasts (a staccato note has none); vibrato is wavy.
                float width = staccato ? 0 : Math.Max(0, (float)(n.DurationMs * staff.speed));
                tail.Alpha = width > head / 2 ? 1 : 0;
                tail.Position = new Vector2(x, y + drop);
                tail.Width = width;
                tailBody.Colour = ColourInfo.GradientHorizontal(colour.Opacity(0.6f), colour.Opacity(0.2f));
                tailMark.Text = n.Vibrato ? @"~ ~ ~ ~ ~ ~ ~ ~ ~ ~ ~ ~ ~ ~ ~ ~" : string.Empty;

                // The part the player held at the right pitch fills in, with light running along it.
                float held = heldUntil > n.StartTime ? (float)((heldUntil - n.StartTime) * staff.speed) : 0;
                float fill = Math.Min(held, width);
                tailFill.Width = fill;
                tailFill.Colour = ColourInfo.GradientHorizontal(colour.Lighten(0.6f), Color4.White);
                tailFill.Alpha = 0.9f;
                tailShine.Alpha = fill > 8 ? 0.5f : 0;
                tailShine.X = fill > 8 ? (float)(now / 3 % Math.Max(1, fill)) : 0;
                tailShine.Colour = ColourInfo.GradientHorizontal(Color4.White.Opacity(0), Color4.White);

                // Legato: tied to the previous note, no new attack.
                if (n.Articulation == Articulation.Legato && previous != null)
                {
                    var from = new Vector2(staff.xAt(previous.EndMs, now), staff.yAt(previous.Midi));
                    var to = new Vector2(x - size / 2, y);
                    var d = to - from;
                    legato.Alpha = 0.75f;
                    legato.Colour = colour;
                    legato.Position = from;
                    legato.Width = d.Length;
                    legato.Rotation = MathHelper.RadiansToDegrees(MathF.Atan2(d.Y, d.X));
                }
                else
                    legato.Alpha = 0;

                // The ring closes on the next note as it reaches the line.
                double toGo = n.StartTime - now;

                if (isNext && toGo > 0 && toGo < approach_ms)
                {
                    float t = (float)(toGo / approach_ms);
                    ring.Alpha = (1 - t) * 0.9f + 0.1f;
                    ring.Size = new Vector2(size * (1 + 2.2f * t));
                    ring.Position = new Vector2(x, y);
                    ring.BorderColour = light;
                    ring.BorderThickness = 2 + 3 * (1 - t);
                }
                else
                    ring.Alpha = 0;
            }
        }
    }
}
