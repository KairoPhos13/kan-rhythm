// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Shapes;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osuTK;
using osuTK.Graphics;
using Kan.Forge.Play;
using Kan.Forge.Score;
using Kan.Ruleset.Objects;

namespace Kan.Ruleset.UI
{
    /// <summary>
    /// The living staff (gameplay MVP, owner 2026-10-14). Time runs right to
    /// left towards a fixed "now" line; height is pitch, over guide lines for
    /// the natural notes (the treble staff's five lines brighter, every C
    /// named). Each note is a head with its name, a tail as long as it lasts
    /// and the mark of its playing style; a ring closes on the head as it
    /// reaches the line (the moment to play). The player's own pitch is drawn
    /// as a trail, so a miss shows why: above = sharp, after the line = late.
    /// The pitch range follows the music, and everything is drawn from small
    /// pools, so the staff costs the same for any length of score.
    /// </summary>
    public partial class KanStaff : CompositeDrawable
    {
        private const float now_fraction = 0.22f;
        private const double ahead_ms = 3200; // time shown to the right of the line
        private const double approach_ms = 900;
        private const float head = 30;
        private const float tail_height = 10;
        private const int note_pool = 72;
        private const int trail_pool = 240;
        private const int line_pool = 48;
        private const double trail_ms = 2200;

        private static readonly Color4 note_colour = Color4Extensions.FromHex(@"ff66aa");
        private static readonly Color4 unknown_colour = Color4Extensions.FromHex(@"8a8a99");
        private static readonly Color4 great_colour = Color4Extensions.FromHex(@"ffd54f");
        private static readonly Color4 ok_colour = Color4Extensions.FromHex(@"7cdb8a");
        private static readonly Color4 meh_colour = Color4Extensions.FromHex(@"66b3ff");
        private static readonly Color4 miss_colour = Color4Extensions.FromHex(@"6b5560");

        /// <summary>Feedback for a miss: clearly red (the dim <see cref="miss_colour"/> marks notes already missed).</summary>
        private static readonly Color4 miss_feedback_colour = Color4Extensions.FromHex(@"ff5a6e");
        private static readonly Color4 trail_on = Color4Extensions.FromHex(@"b7ffcf");
        private static readonly Color4 trail_off = Color4Extensions.FromHex(@"ffffff");

        private static readonly string[] names = { "C", "C♯", "D", "D♯", "E", "F", "F♯", "G", "G♯", "A", "A♯", "B" };

        public static string NoteName(int midi) => $"{names[((midi % 12) + 12) % 12]}{midi / 12 - 1}";

        private readonly KanPlayState state;
        private readonly IReadOnlyList<KanHitObject> notes;

        /// <summary>Until when the player has held each note at its pitch (sustain fill).</summary>
        private readonly double[] heldUntil;

        private readonly Container lines = new Container { RelativeSizeAxes = Axes.Both };
        private readonly Container noteLayer = new Container { RelativeSizeAxes = Axes.Both };
        private readonly Container trailLayer = new Container { RelativeSizeAxes = Axes.Both };
        private readonly Container feedbackLayer = new Container { RelativeSizeAxes = Axes.Both };

        private readonly Box[] lineBoxes = new Box[line_pool];
        private readonly OsuSpriteText[] lineLabels = new OsuSpriteText[line_pool];
        private readonly NoteView[] views = new NoteView[note_pool];
        private readonly Circle[] dots = new Circle[trail_pool];

        private Box nowLine = null!;
        private Circle nowGlow = null!;

        private double lo = 60, hi = 84; // shown pitch range (smoothed)
        private int firstVisible;

        public KanStaff(KanPlayState state)
        {
            this.state = state;
            notes = state.Notes;
            heldUntil = new double[notes.Count];
            RelativeSizeAxes = Axes.Both;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            for (int i = 0; i < line_pool; i++)
            {
                lines.Add(lineBoxes[i] = new Box { RelativeSizeAxes = Axes.X, Height = 1, Origin = Anchor.CentreLeft, Alpha = 0 });
                lines.Add(lineLabels[i] = new OsuSpriteText
                {
                    Origin = Anchor.CentreLeft,
                    X = 10,
                    Font = OsuFont.GetFont(size: 13, weight: FontWeight.SemiBold),
                    Alpha = 0,
                });
            }

            for (int i = 0; i < note_pool; i++)
                noteLayer.Add(views[i] = new NoteView());

            for (int i = 0; i < trail_pool; i++)
                trailLayer.Add(dots[i] = new Circle { Origin = Anchor.Centre, Size = new Vector2(5), Alpha = 0 });

            InternalChildren = new Drawable[]
            {
                lines,
                nowGlow = new Circle
                {
                    RelativePositionAxes = Axes.X,
                    X = now_fraction,
                    RelativeSizeAxes = Axes.Y,
                    Height = 1.2f,
                    Width = 28,
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.Centre,
                    Colour = Color4.White,
                    Blending = BlendingParameters.Additive,
                    Alpha = 0.05f,
                },
                nowLine = new Box
                {
                    RelativePositionAxes = Axes.X,
                    X = now_fraction,
                    RelativeSizeAxes = Axes.Y,
                    Width = 3,
                    Origin = Anchor.TopCentre,
                    Colour = Color4.White,
                    Alpha = 0.7f,
                },
                noteLayer,
                trailLayer,
                feedbackLayer,
            };

            state.Judged += onJudged;
        }

        private float nowX => DrawWidth * now_fraction;

        private float speed => (float)(DrawWidth * (1 - now_fraction) / ahead_ms);

        private float xAt(double timeMs, double now) => nowX + (float)(timeMs - now) * speed;

        private float yAt(double midi)
        {
            const float top = 70, bottom = 110;
            float h = Math.Max(1, DrawHeight - top - bottom);
            return top + (float)((hi - midi) / (hi - lo)) * h;
        }

        protected override void Update()
        {
            base.Update();

            if (DrawWidth <= 0) return;

            double now = Clock.CurrentTime;

            followRange(now);
            drawLines();
            trackHolding(now);
            drawNotes(now);
            drawTrail(now);

            // The line glows with the music's bloom.
            nowGlow.Alpha = (float)(0.03 + 0.09 * state.Bloom);
        }

        /// <summary>The range follows the next few seconds of music, eased so it never jumps.</summary>
        private void followRange(double now)
        {
            int min = int.MaxValue, max = int.MinValue;

            for (int i = firstVisible; i < notes.Count && notes[i].StartTime < now + 6000; i++)
            {
                if (notes[i].EndMs < now - 1000) continue;

                min = Math.Min(min, notes[i].Midi);
                max = Math.Max(max, notes[i].Midi);
            }

            if (min == int.MaxValue) return;

            double centre = (min + max) / 2.0;
            double span = Math.Max(14, max - min + 6);
            double targetLo = centre - span / 2, targetHi = centre + span / 2;
            double k = Math.Min(1, Time.Elapsed / 600);

            lo += (targetLo - lo) * k;
            hi += (targetHi - hi) * k;
        }

        private void drawLines()
        {
            int used = 0;

            for (int m = (int)Math.Floor(lo) - 1; m <= (int)Math.Ceiling(hi) + 1 && used < line_pool; m++)
            {
                int pc = ((m % 12) + 12) % 12;
                bool natural = pc is 0 or 2 or 4 or 5 or 7 or 9 or 11;
                if (!natural) continue;

                // The treble staff's five lines: E4 G4 B4 D5 F5.
                bool staffLine = m is 64 or 67 or 71 or 74 or 77;
                float y = yAt(m);

                var line = lineBoxes[used];
                line.Y = y;
                line.Alpha = staffLine ? 0.32f : pc == 0 ? 0.16f : 0.06f;
                line.Height = staffLine ? 2 : 1;

                var label = lineLabels[used];
                label.Text = pc == 0 ? NoteName(m) : string.Empty;
                label.Y = y - 9;
                label.Alpha = pc == 0 ? 0.5f : 0;

                used++;
            }

            for (int i = used; i < line_pool; i++)
            {
                lineBoxes[i].Alpha = 0;
                lineLabels[i].Alpha = 0;
            }
        }

        /// <summary>A hit note fills while the player holds its pitch.</summary>
        private void trackHolding(double now)
        {
            if (state.TrailCount == 0) return;

            var latest = state.TrailAt(0);
            if (latest.Midi <= 0 || now - latest.TimeMs > 120) return;

            for (int i = firstVisible; i < notes.Count && notes[i].StartTime <= now; i++)
            {
                var n = notes[i];
                if (now > n.EndMs || state.Verdicts[i] is not { } v || v.Result == LiveResult.Miss) continue;

                if (n.PitchUnknown || Math.Abs(latest.Midi - n.Midi) < 0.6)
                    heldUntil[i] = Math.Min(now, n.EndMs);
            }
        }

        private void drawNotes(double now)
        {
            double from = now - nowX / speed - 200; // what has scrolled off to the left
            double to = now + ahead_ms + 200;

            while (firstVisible < notes.Count && notes[firstVisible].EndMs < from)
                firstVisible++;

            int used = 0;

            for (int i = firstVisible; i < notes.Count && used < note_pool; i++)
            {
                var n = notes[i];
                if (n.StartTime > to) break;
                if (n.EndMs < from) continue;

                var previous = i > 0 ? notes[i - 1] : null;
                var view = views[used++];
                view.Show(n, state.Verdicts[i], heldUntil[i], previous, now, this);

                // "Hidden": a note fades out on its way to the line (gone 300 ms before it).
                if (state.HideNotes && state.Verdicts[i] == null)
                    view.Alpha = (float)Math.Clamp((n.StartTime - now - 300) / 500, 0, 1);
            }

            for (int i = used; i < note_pool; i++)
                views[i].Alpha = 0;
        }

        private void drawTrail(double now)
        {
            int used = 0;

            for (int i = 0; i < state.TrailCount && used < trail_pool; i++)
            {
                var p = state.TrailAt(i);
                if (now - p.TimeMs > trail_ms) break;
                if (p.Midi <= 0) continue;

                var dot = dots[used++];
                float age = (float)((now - p.TimeMs) / trail_ms);
                bool onNote = soundingAt(p.TimeMs, p.Midi);

                dot.Position = new Vector2(xAt(p.TimeMs, now), yAt(p.Midi));
                dot.Size = new Vector2(4 + Math.Min(8, p.Level * 60));
                dot.Colour = onNote ? trail_on : trail_off;
                dot.Alpha = (1 - age) * (onNote ? 0.95f : 0.5f);
            }

            for (int i = used; i < trail_pool; i++)
                dots[i].Alpha = 0;
        }

        private bool soundingAt(double timeMs, float midi)
        {
            for (int i = Math.Max(0, firstVisible - 8); i < notes.Count && notes[i].StartTime <= timeMs + 60; i++)
            {
                var n = notes[i];
                if (timeMs >= n.StartTime - 60 && timeMs <= n.EndMs && Math.Abs(midi - n.Midi) < 0.5)
                    return true;
            }

            return false;
        }

        // ------------------------------------------------------------------
        // Feedback: short, in musical words, where the player is looking
        // ------------------------------------------------------------------

        private void onJudged(int index, LiveVerdict v)
        {
            var n = notes[index];
            string text;
            Color4 colour;

            switch (v.Reason)
            {
                case LiveReason.NotHeard:
                    text = @"not heard";
                    colour = miss_feedback_colour;
                    break;

                case LiveReason.WrongNote:
                    text = v.PlayedMidi is int played ? $"{NoteName(played)} — play {NoteName(n.Midi)}" : @"another note";
                    colour = miss_feedback_colour;
                    break;

                default:
                    text = v.Result == LiveResult.Great ? @"on time"
                        : v.OffsetMs < 0 ? @"a bit early" : @"a bit late";
                    if (v.Reason == LiveReason.PitchUnclear) text += @" · pitch unclear";
                    else if (v.Cents > 25) text += @" · sharp ↑";
                    else if (v.Cents < -25) text += @" · flat ↓";

                    colour = v.Result == LiveResult.Great ? great_colour : v.Result == LiveResult.Ok ? ok_colour : meh_colour;
                    break;
            }

            var label = new OsuSpriteText
            {
                Text = text,
                Font = OsuFont.GetFont(size: 17, weight: FontWeight.Bold),
                Colour = colour,
                Origin = Anchor.BottomCentre,
                Position = new Vector2(nowX, yAt(n.Midi) - head),
            };

            feedbackLayer.Add(label);
            label.MoveToOffset(new Vector2(0, -26), 700, Easing.OutQuint)
                 .FadeOut(700, Easing.InQuad)
                 .Expire();

            // Every judgement is seen at the line: a miss flashes it red.
            nowLine.FlashColour(v.Result == LiveResult.Miss ? miss_feedback_colour : colour, v.Result == LiveResult.Miss ? 450 : 300, Easing.OutQuint);
            nowGlow.FadeTo(v.Result == LiveResult.Miss ? 0.45f : 0.3f, 40).Then().FadeTo(0.08f, 400);
        }

        protected override void Dispose(bool isDisposing)
        {
            state.Judged -= onJudged;
            base.Dispose(isDisposing);
        }

        /// <summary>One note on the staff (pooled; re-pointed at a note each frame).</summary>
        private partial class NoteView : CompositeDrawable
        {
            private readonly Box legato;
            private readonly Container tail;
            private readonly Box tailBody;
            private readonly Box tailFill;
            private readonly OsuSpriteText tailMark;
            private readonly CircularContainer headCircle;
            private readonly Box headFill;
            private readonly OsuSpriteText name;
            private readonly OsuSpriteText mark;
            private readonly CircularContainer ring;

            public NoteView()
            {
                RelativeSizeAxes = Axes.Both;
                Alpha = 0;

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
                            tailBody = new Box { RelativeSizeAxes = Axes.Both, Alpha = 0.45f },
                            tailFill = new Box { RelativeSizeAxes = Axes.Y, Colour = Color4.White, Alpha = 0.85f },
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
                        EdgeEffect = new EdgeEffectParameters { Type = EdgeEffectType.Glow, Radius = 8, Colour = note_colour.Opacity(0.4f) },
                        Child = headFill = new Box { RelativeSizeAxes = Axes.Both },
                    },
                    name = new OsuSpriteText
                    {
                        Origin = Anchor.Centre,
                        Font = OsuFont.GetFont(size: 10, weight: FontWeight.Bold),
                        Colour = Color4.Black,
                    },
                    mark = new OsuSpriteText
                    {
                        Origin = Anchor.BottomCentre,
                        Font = OsuFont.GetFont(size: 18, weight: FontWeight.Black),
                        Colour = Color4.White,
                    },
                };
            }

            public void Show(KanHitObject n, LiveVerdict? verdict, double heldUntil, KanHitObject? previous, double now, KanStaff staff)
            {
                Alpha = 1;

                float x = staff.xAt(n.StartTime, now);
                float y = staff.yAt(n.Midi);
                bool staccato = n.Articulation == Articulation.Staccato;
                bool accent = n.Articulation == Articulation.Accent;

                Color4 colour = verdict switch
                {
                    { Result: LiveResult.Great } => great_colour,
                    { Result: LiveResult.Ok } => ok_colour,
                    { Result: LiveResult.Meh } => meh_colour,
                    { Result: LiveResult.Miss } => miss_colour,
                    _ => n.PitchUnknown ? unknown_colour : note_colour,
                };

                // Head: the moment and the note.
                float size = head * (accent ? 1.25f : 1);
                headCircle.Position = new Vector2(x, y);
                headCircle.Size = new Vector2(size);
                headFill.Colour = colour;
                headCircle.Alpha = verdict is { Result: LiveResult.Miss } ? 0.5f : 1;
                name.Position = new Vector2(x, y);
                name.Text = n.PitchUnknown ? @"?" : NoteName(n.Midi);

                // Style mark above the head: accent ">", staccato "•".
                mark.Position = new Vector2(x, y - size / 2 - 2);
                mark.Text = accent ? @">" : staccato ? @"•" : string.Empty;

                // Tail: how long it lasts (a staccato note has none); vibrato is wavy.
                float width = staccato ? 0 : Math.Max(0, (float)(n.DurationMs * staff.speed));
                tail.Alpha = width > head / 2 ? 1 : 0;
                tail.Position = new Vector2(x, y);
                tail.Width = width;
                tailBody.Colour = colour;
                tailMark.Text = n.Vibrato ? @"~ ~ ~ ~ ~ ~ ~ ~ ~ ~ ~ ~ ~ ~ ~ ~" : string.Empty;

                // The part the player held at the right pitch fills in.
                float held = heldUntil > n.StartTime ? (float)((heldUntil - n.StartTime) * staff.speed) : 0;
                tailFill.Width = Math.Min(held, width);

                // Legato: tied to the previous note, no new attack.
                if (n.Articulation == Articulation.Legato && previous != null)
                {
                    var from = new Vector2(staff.xAt(previous.EndMs, now), staff.yAt(previous.Midi));
                    var to = new Vector2(x - size / 2, y);
                    var d = to - from;
                    legato.Alpha = 0.7f;
                    legato.Colour = colour;
                    legato.Position = from;
                    legato.Width = d.Length;
                    legato.Rotation = MathHelper.RadiansToDegrees(MathF.Atan2(d.Y, d.X));
                }
                else
                    legato.Alpha = 0;

                // The ring closes on the head as it reaches the line.
                double toGo = n.StartTime - now;

                if (verdict == null && toGo > 0 && toGo < approach_ms)
                {
                    float t = (float)(toGo / approach_ms);
                    ring.Alpha = 0.25f + 0.75f * (1 - t);
                    ring.Size = new Vector2(size * (1 + 2.2f * t));
                    ring.Position = new Vector2(x, y);
                    ring.BorderColour = colour;
                }
                else
                    ring.Alpha = 0;
            }
        }
    }
}
