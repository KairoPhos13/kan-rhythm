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
using Kan.Core.Design;
using Kan.Forge.Play;
using Kan.Core.Score;
using Kan.Ruleset.Objects;

namespace Kan.Ruleset.UI
{
    /// <summary>
    /// The staff's guides on the now line: where the next note arrives, the target lit while
    /// its perfect window is open, and the count-in before the first note.
    /// </summary>
    public partial class KanStaff : CompositeDrawable
    {
        /// <summary>
        /// On the now line, where the next note will cross it: an arrow and
        /// its name, gliding from note to note; the trail (the player's
        /// pitch) meets it when the note is played right.
        /// </summary>
        private partial class NextMarker : CompositeDrawable
        {
            private readonly OsuSpriteText label;
            private bool placed;

            public NextMarker()
            {
                AutoSizeAxes = Axes.Both;
                Origin = Anchor.CentreRight;
                Alpha = 0;

                InternalChild = new FillFlowContainer
                {
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Horizontal,
                    Spacing = new Vector2(4, 0),
                    Children = new Drawable[]
                    {
                        label = new OsuSpriteText
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Font = KanTextScale.GameplayFont(18, FontWeight.Black),
                            Shadow = true,
                        },
                        new SpriteIcon
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Icon = FontAwesome.Solid.CaretRight,
                            Size = new Vector2(16),
                            Shadow = true,
                        },
                    },
                };
            }

            public void Point(float x, float y, KanHitObject? note, double toGo, Color4 colour)
            {
                if (note == null)
                {
                    Alpha = Math.Max(0, Alpha - (float)(Math.Abs(Time.Elapsed) / 200));
                    placed = Alpha > 0 && placed;
                    return;
                }

                X = x;

                // Glide to the next note's height (snap the first time).
                Y = placed ? Y + (y - Y) * (float)Math.Min(1, Math.Abs(Time.Elapsed) / 90) : y;
                placed = true;

                label.Text = note.PitchUnknown ? @"?" : NoteName(note.Midi);
                Colour = note.PitchUnknown ? unknown_colour : colour;

                // Appears as the note comes close; full when it is time.
                Alpha = (float)Math.Clamp(1 - (toGo - 400) / 1600, 0.25, 1);
            }
        }

        /// <summary>
        /// A circle on the now line where the next note will cross it (owner
        /// 2026-10-19: easier than judging by eye when a note is on the line):
        /// a little wider than a note, so the moment to play is when the note
        /// sits centred inside it. It fills in as the note arrives.
        /// </summary>
        private partial class HitTarget : CompositeDrawable
        {
            private const float margin = 8;

            private readonly CircularContainer outline;
            private readonly Box fill;
            private bool placed;

            public HitTarget()
            {
                Size = new Vector2(head + margin);
                Origin = Anchor.Centre;
                Alpha = 0;

                InternalChild = outline = new CircularContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    Masking = true,
                    BorderThickness = 3,
                    BorderColour = Color4.White,
                    Child = fill = new Box { RelativeSizeAxes = Axes.Both, Alpha = 0, AlwaysPresent = true },
                };
            }

            /// <param name="follow">Take <paramref name="y"/> as it is (a glide drawn by the caller), not eased to.</param>
            public void Point(float x, float y, KanHitObject? note, double toGo, Color4 colour, bool follow = false)
            {
                if (note == null)
                {
                    Alpha = Math.Max(0, Alpha - (float)(Math.Abs(Time.Elapsed) / 200));
                    placed = Alpha > 0 && placed;
                    return;
                }

                X = x;

                // Glides to the next note's height with the arrow (snap the first time).
                Y = placed && !follow ? Y + (y - Y) * (float)Math.Min(1, Math.Abs(Time.Elapsed) / 90) : y;
                placed = true;

                outline.BorderColour = note.PitchUnknown ? unknown_colour : colour;
                fill.Colour = outline.BorderColour;

                // Shows as the note comes close; the inside lights up over its last half second.
                Alpha = (float)Math.Clamp(1 - (toGo - 400) / 1600, 0.3, 0.9);
                fill.Alpha = (float)Math.Clamp(1 - toGo / 500, 0, 1) * 0.25f;
            }
        }

        /// <summary>3, 2, 1 in a ring closing on where the next note will arrive.</summary>
        private partial class Countdown : CompositeDrawable
        {
            private readonly CircularContainer ring;
            private readonly OsuSpriteText number;
            private int shown;

            public Countdown()
            {
                Size = Vector2.Zero;
                Alpha = 0;

                InternalChildren = new Drawable[]
                {
                    ring = new CircularContainer
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Masking = true,
                        BorderThickness = 4,
                        BorderColour = Color4.White,
                        Blending = BlendingParameters.Additive,
                        Child = new Box { RelativeSizeAxes = Axes.Both, Alpha = 0, AlwaysPresent = true },
                    },
                    number = new OsuSpriteText
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Y = -70,
                        Font = KanTextScale.GameplayFont(44, FontWeight.Black),
                        Shadow = true,
                    },
                };
            }

            public void Count(float x, float y, double toGo, Color4 colour)
            {
                Position = new Vector2(x, y);
                Alpha = Math.Min(1, Alpha + (float)(Math.Abs(Time.Elapsed) / 200));

                int second = (int)Math.Ceiling(toGo / 1000);
                float within = (float)(1 - (toGo - (second - 1) * 1000) / 1000); // 0 → 1 inside each second

                if (second != shown)
                {
                    shown = second;
                    number.Text = second.ToString();
                }

                // Each second the ring closes in on the spot, and the number pops.
                ring.Size = new Vector2(150 - 110 * within);
                ring.BorderColour = colour;
                ring.Alpha = 0.25f + 0.65f * within;
                number.Colour = colour;
                number.Scale = new Vector2(1.35f - 0.35f * Math.Min(1, within * 4));
            }

            public void Fade(double elapsed)
            {
                Alpha = Math.Max(0, Alpha - (float)(elapsed / 150));
                shown = 0;
            }
        }
    }
}
