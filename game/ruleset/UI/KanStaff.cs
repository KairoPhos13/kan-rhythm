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
    /// The living staff (gameplay MVP, owner 2026-10-14). Time runs right to
    /// left towards a fixed "now" line; height is pitch, over guide lines for
    /// the natural notes. Each note is a head with its name, a tail as long as
    /// it lasts and the mark of its playing style; a ring closes on the next
    /// note as it reaches the line. The player's own pitch is drawn as a
    /// trail, so a miss shows why: above = sharp, after the line = late.
    ///
    /// Owner 2026-10-15 ("not osu!'s level yet in effects, smooth transitions,
    /// clarity, flashes, colours and impact"): notes are lit beads in the
    /// level's palette with white names; every change is animated from the
    /// time it happened (a hit pops and flashes into its grade's colour, a
    /// miss shudders and greys); notes fade in at the right edge and out at
    /// the left; rests dim the staff and count the player back in.
    /// Everything is drawn from small pools, so the staff costs the same for
    /// any length of score.
    /// </summary>
    public partial class KanStaff : CompositeDrawable
    {
        private const float now_fraction = 0.22f;
        private const double ahead_ms = 3200; // time shown to the right of the line
        private const double approach_ms = 900;
        private const float head = 36;
        private const float tail_height = 12;
        private const int note_pool = 72;
        private const int trail_pool = 240;
        private const int line_pool = 48;
        private const double trail_ms = 2200;

        private static readonly Color4 unknown_colour = Color4Extensions.FromHex(@"8a8a99");
        private static readonly Color4 great_colour = Color4Extensions.FromHex(@"ffd54f");
        private static readonly Color4 ok_colour = Color4Extensions.FromHex(@"7cdb8a");
        private static readonly Color4 meh_colour = Color4Extensions.FromHex(@"66b3ff");
        private static readonly Color4 miss_colour = Color4Extensions.FromHex(@"5c4a55");

        /// <summary>Feedback for a miss: clearly red (the dim <see cref="miss_colour"/> marks notes already missed).</summary>
        private static readonly Color4 miss_feedback_colour = Color4Extensions.FromHex(@"ff5a6e");

        private static readonly string[] names = { "C", "C♯", "D", "D♯", "E", "F", "F♯", "G", "G♯", "A", "A♯", "B" };

        public static string NoteName(int midi) => $"{names[((midi % 12) + 12) % 12]}{midi / 12 - 1}";

        public static Color4 GradeColour(LiveResult result) => result switch
        {
            LiveResult.Great => great_colour,
            LiveResult.Ok => ok_colour,
            LiveResult.Meh => meh_colour,
            _ => miss_feedback_colour,
        };

        private readonly KanPlayState state;
        private readonly KanScene scene;
        private readonly IReadOnlyList<KanHitObject> notes;

        /// <summary>Until when the player has held each note at its pitch (sustain fill).</summary>
        private readonly double[] heldUntil;

        /// <summary>When each note was judged (its pop or shudder plays from there); NaN = not yet.</summary>
        private readonly double[] judgedAt;

        private readonly Container lines = new Container { RelativeSizeAxes = Axes.Both };
        private readonly Container noteLayer = new Container { RelativeSizeAxes = Axes.Both };
        private readonly Container trailLayer = new Container { RelativeSizeAxes = Axes.Both };
        private readonly Container feedbackLayer = new Container { RelativeSizeAxes = Axes.Both };

        private readonly Box[] lineBoxes = new Box[line_pool];
        private readonly OsuSpriteText[] lineLabels = new OsuSpriteText[line_pool];
        private readonly NoteView[] views = new NoteView[note_pool];
        private readonly Circle[] dots = new Circle[trail_pool];

        private KanNowLine nowLine = null!;
        private NextMarker nextMarker = null!;
        private Countdown countdown = null!;

        /// <summary>The next note to play (the first not yet judged), or -1.</summary>
        private int next = -1;

        /// <summary>The note held at its pitch this frame (it burns at the line), or -1.</summary>
        private int holding = -1;

        private double lo = 60, hi = 84; // shown pitch range (smoothed)

        /// <summary>The time the staff shows (<see cref="KanPlayState.DisplayTime"/>).</summary>
        private double displayNow;
        private int firstVisible;

        public KanStaff(KanPlayState state, KanScene scene)
        {
            this.state = state;
            this.scene = scene;
            notes = state.Notes;
            heldUntil = new double[notes.Count];
            judgedAt = new double[notes.Count];
            Array.Fill(judgedAt, double.NaN);
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
                trailLayer.Add(dots[i] = new Circle { Origin = Anchor.Centre, Size = new Vector2(5), Alpha = 0, Blending = BlendingParameters.Additive });

            InternalChildren = new Drawable[]
            {
                lines,
                noteLayer,
                trailLayer,
                // Above the notes: a hit's impact lands on its note.
                nowLine = new KanNowLine(now_fraction, scene),
                nextMarker = new NextMarker(),
                countdown = new Countdown(),
                feedbackLayer,
            };

            state.Judged += onJudged;
        }

        private float nowX => DrawWidth * now_fraction;

        /// <summary>Where a pitch crosses the now line, in screen space (for overlays outside the staff).</summary>
        public Vector2 LineScreenSpace(double midi) => ToScreenSpace(new Vector2(nowX, yAt(midi)));

        public KanScene Scene => scene;

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

            // In wait mode the staff glides to a halt on the note instead of showing the level run past it.
            double now = displayNow = state.DisplayTime(Clock.CurrentTime, Time.Elapsed);

            followRange(now);
            drawLines();
            trackHolding(now);
            drawNotes(now);
            drawTrail(now);
            drawCountdown(now);

            // The line glows with the music's bloom, and burns with a held note.
            nowLine.Bloom = state.Bloom;
            nowLine.Holding(holding >= 0 ? yAt(notes[holding].Midi) : null, holding >= 0 ? colourOf(state.Verdicts[holding]) : Color4.White, now);
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

            // A rest steps the staff back (owner 2026-10-15: change the screen where nothing is played).
            float presence = 1 - 0.75f * scene.Rest;

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
                line.Alpha = (staffLine ? 0.28f : pc == 0 ? 0.16f : 0.06f) * presence;
                line.Height = staffLine ? 2 : 1;
                line.Colour = ColourInfo.GradientHorizontal(scene.NoteLight.Opacity(0.5f), Color4.White);

                // Every natural line is named (owner 2026-10-15: notes were hard to
                // read); the Cs, where octaves start, stand out.
                var label = lineLabels[used];
                label.Text = NoteName(m);
                label.Y = y - 9;
                label.Alpha = (pc == 0 ? 0.55f : 0.22f) * presence;

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
            holding = -1;

            if (state.TrailCount == 0) return;

            var latest = state.TrailAt(0);
            if (latest.Midi <= 0 || now - latest.TimeMs > 120) return;

            for (int i = firstVisible; i < notes.Count && notes[i].StartTime <= now; i++)
            {
                var n = notes[i];
                if (now > n.EndMs || state.Verdicts[i] is not { } v || v.Result == LiveResult.Miss) continue;

                if (n.PitchUnknown || state.Judge.Matches(latest.Midi, n.Midi))
                {
                    heldUntil[i] = Math.Min(now, n.EndMs);

                    // A short note's hit is its reward; burning is for sustains.
                    if (n.DurationMs >= 250 && now - n.StartTime > 120) holding = i;
                }
            }
        }

        private void drawNotes(double now)
        {
            double from = now - nowX / speed - 200; // what has scrolled off to the left
            double to = now + ahead_ms + 200;

            while (firstVisible < notes.Count && notes[firstVisible].EndMs < from)
                firstVisible++;

            // The next note to play: the first not judged yet (a note being
            // waited on for its pitch stays next for a moment after the line).
            next = -1;

            for (int i = firstVisible; i < notes.Count; i++)
            {
                if (state.Verdicts[i] == null && notes[i].StartTime > now - 250)
                {
                    next = i;
                    break;
                }
            }

            double nextStart = next >= 0 ? notes[next].StartTime : double.MaxValue;
            int used = 0;

            var look = new NoteLook(scene.Note, scene.NoteLight, now, DrawWidth, nowX);

            for (int i = firstVisible; i < notes.Count && used < note_pool; i++)
            {
                var n = notes[i];
                if (n.StartTime > to) break;
                if (n.EndMs < from) continue;

                // The next note (or chord) is the one to look at; later ones step
                // back with distance, so the eye lands on what comes first.
                bool isNext = state.Verdicts[i] == null && n.StartTime - nextStart < 60;
                float emphasis = state.Verdicts[i] != null || isNext ? 1 : (float)Math.Clamp(1 - (n.StartTime - nextStart) / 3000, 0.45, 0.85);

                var previous = i > 0 ? notes[i - 1] : null;
                var view = views[used++];
                view.Show(n, state.Verdicts[i], now - judgedAt[i], heldUntil[i], previous, this, isNext, emphasis, look);

                // "Hidden": a note fades out on its way to the line (gone 300 ms before it).
                if (state.HideNotes && state.Verdicts[i] == null)
                    view.Alpha *= (float)Math.Clamp((n.StartTime - now - 300) / 500, 0, 1);
            }

            for (int i = used; i < note_pool; i++)
                views[i].Alpha = 0;

            // Where the next note will cross the line, marked on the line itself.
            if (next >= 0 && !state.HideNotes)
                nextMarker.Point(nowX - head / 2 - 6, yAt(notes[next].Midi), notes[next], notes[next].StartTime - now, scene.NoteLight);
            else
                nextMarker.Point(0, 0, null, 0, Color4.White);
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
                dot.Colour = onNote ? scene.Trail : Color4.White;
                dot.Alpha = (1 - age) * (1 - age) * (onNote ? 0.95f : 0.45f);
            }

            for (int i = used; i < trail_pool; i++)
                dots[i].Alpha = 0;
        }

        /// <summary>At the end of a rest, 3-2-1 where the next note will arrive (BreakStyle.Countdown).</summary>
        private void drawCountdown(double now)
        {
            var section = scene.Section;
            double toGo = scene.NextNoteMs - now;
            bool counting = section != null && (section.IsRest || section.Kind == SectionKind.Intro)
                            && section.BreakStyle == BreakStyle.Countdown
                            && scene.NextNote >= 0 && toGo > 0 && toGo <= KanScene.COUNTDOWN_MS
                            && section.DurationMs >= KanScene.COUNTDOWN_MS;

            if (!counting)
            {
                countdown.Fade(Time.Elapsed);
                return;
            }

            var n = notes[scene.NextNote];
            countdown.Count(nowX, yAt(n.Midi), toGo, scene.Hot);
        }

        private bool soundingAt(double timeMs, float midi)
        {
            for (int i = Math.Max(0, firstVisible - 8); i < notes.Count && notes[i].StartTime <= timeMs + 60; i++)
            {
                var n = notes[i];
                if (timeMs >= n.StartTime - 60 && timeMs <= n.EndMs && state.Judge.Matches(midi, n.Midi))
                    return true;
            }

            return false;
        }

        // ------------------------------------------------------------------
        // Feedback: short, in musical words, where the player is looking
        // ------------------------------------------------------------------

        private void onJudged(int index, LiveVerdict v)
        {
            judgedAt[index] = displayNow; // the staff's own time: transitions play from what is seen

            var n = notes[index];
            string text;
            Color4 colour = GradeColour(v.Result);

            switch (v.Reason)
            {
                case LiveReason.NotHeard:
                    text = @"not heard";
                    break;

                case LiveReason.WrongNote:
                    text = v.PlayedMidi is int played ? $"{NoteName(played)} — play {NoteName(n.Midi)}" : @"another note";
                    break;

                default:
                    text = v.Result == LiveResult.Great ? @"on time"
                        : v.OffsetMs < 0 ? @"a bit early" : @"a bit late";
                    if (v.Reason == LiveReason.PitchUnclear) text += @" · pitch unclear";
                    else if (v.Cents > 25) text += @" · sharp ↑";
                    else if (v.Cents < -25) text += @" · flat ↓";
                    break;
            }

            var label = new OsuSpriteText
            {
                Text = text,
                Font = OsuFont.GetFont(size: 17, weight: FontWeight.Bold),
                Colour = colour,
                Shadow = true,
                Origin = Anchor.BottomCentre,
                Position = new Vector2(nowX, yAt(n.Midi) - head * 0.75f),
                Scale = new Vector2(0.8f),
            };

            feedbackLayer.Add(label);
            label.ScaleTo(1, 250, Easing.OutBack)
                 .MoveToOffset(new Vector2(0, -26), 700, Easing.OutQuint)
                 .Delay(250).FadeOut(450, Easing.InQuad)
                 .Expire();

            // Every judgement lands on the line: an impact for a hit, a red jolt for a miss.
            nowLine.Judged(v.Result, yAt(n.Midi), colour, state.Streak);
        }

        private static Color4 colourOf(LiveVerdict? v) => v is { } verdict ? GradeColour(verdict.Result) : Color4.White;

        protected override void Dispose(bool isDisposing)
        {
            state.Judged -= onJudged;
            base.Dispose(isDisposing);
        }

        /// <summary>What every note shares this frame.</summary>
        private readonly record struct NoteLook(Color4 Note, Color4 NoteLight, double Now, float Width, float NowX);

        /// <summary>
        /// On the now line, where the next note will cross it: an arrow and
        /// its name, gliding from note to note — the trail (the player's
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
                            Font = OsuFont.GetFont(size: 18, weight: FontWeight.Black),
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
                    Alpha = Math.Max(0, Alpha - (float)(Time.Elapsed / 200));
                    placed = Alpha > 0 && placed;
                    return;
                }

                X = x;

                // Glide to the next note's height (snap the first time).
                Y = placed ? Y + (y - Y) * (float)Math.Min(1, Time.Elapsed / 90) : y;
                placed = true;

                label.Text = note.PitchUnknown ? @"?" : NoteName(note.Midi);
                Colour = note.PitchUnknown ? unknown_colour : colour;

                // Appears as the note comes close; full when it is time.
                Alpha = (float)Math.Clamp(1 - (toGo - 400) / 1600, 0.25, 1);
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
                        Font = OsuFont.GetFont(size: 44, weight: FontWeight.Black),
                        Shadow = true,
                    },
                };
            }

            public void Count(float x, float y, double toGo, Color4 colour)
            {
                Position = new Vector2(x, y);
                Alpha = Math.Min(1, Alpha + (float)(Time.Elapsed / 200));

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
            private readonly CircularContainer headCircle;
            private readonly Box headFill;
            private readonly Circle flash;
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

                Color4 baseColour = n.PitchUnknown ? unknown_colour : look.Note;
                Color4 lightColour = n.PitchUnknown ? unknown_colour.Lighten(0.4f) : look.NoteLight;
                Color4 colour = baseColour, light = lightColour;
                float pop = 0, flashAlpha = 0, shake = 0, drop = 0, scale = 1;

                if (verdict != null)
                {
                    Color4 target = missed ? miss_colour : GradeColour(verdict.Value.Result);

                    // Transitions play from the moment of the verdict (a seek lands on the end state).
                    double t = double.IsNaN(sinceJudged) ? 10_000 : Math.Max(0, sinceJudged);
                    float k = (float)Math.Clamp(t / 160, 0, 1);
                    colour = Interpolation.ValueAt(k, baseColour, target, 0, 1);
                    light = Interpolation.ValueAt(k, lightColour, missed ? target : target.Lighten(0.5f), 0, 1);

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

                Alpha = emphasis * enter * leave * (missed ? 0.6f : 1);

                // Head: a lit bead, lighter at the top, with a soft halo.
                float size = head * (accent ? 1.22f : 1) * scale * (1 + pop) * (0.75f + 0.25f * enter);
                headCircle.Position = new Vector2(x + shake, y + drop);
                headCircle.Size = new Vector2(size);
                headFill.Colour = ColourInfo.GradientVertical(light, colour);
                flash.Alpha = flashAlpha;

                float pulse = isNext ? 0.5f + 0.5f * MathF.Sin((float)(now / 110)) : 0;
                headCircle.BorderThickness = isNext ? 3 + pulse : missed ? 0 : 1.5f;

                glow.Position = headCircle.Position;
                glow.Size = new Vector2(size * (isNext ? 2.1f + 0.3f * pulse : 1.7f) + pop * 40);
                glow.Colour = colour;
                glow.Alpha = missed ? 0 : isNext ? 0.22f + 0.12f * pulse : verdict != null ? 0.1f + 0.5f * pop : 0.1f;

                name.Position = headCircle.Position;
                name.Text = n.PitchUnknown ? @"?" : NoteName(n.Midi);
                name.Font = OsuFont.GetFont(size: isNext ? 16 : 14, weight: FontWeight.Black);
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
