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
using Kan.Core.Play;

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
        /// <summary>
        /// Time shown to the right of the line: about <see cref="notes_ahead"/>
        /// notes at the pace of what comes, within these bounds (owner
        /// 2026-10-23: "the shorter the notes, the wider the horizontal scale:
        /// the more they are squeezed, the harder they are to play right").
        /// </summary>
        private const double max_ahead_ms = 3200, min_ahead_ms = 1600;

        private const double notes_ahead = 5;

        /// <summary>The time shown to the right of the line now (eased toward the pace of what comes).</summary>
        private double ahead = max_ahead_ms;
        private const double approach_ms = 900;
        private const float head = 36;
        private const float tail_height = 12;
        private const int note_pool = 72;
        private const int trail_pool = 240;
        private const int line_pool = 48;
        private const int bar_pool = 16;
        private const int tick_pool = 96;
        private const double trail_ms = 2200;

        private static readonly Color4 unknown_colour = Color4Extensions.FromHex(@"8a8a99");
        // Three grades: perfect white, radiating faint gold
        // (it was orchid, and before that a near-white that read like the old
        // lilac meh), good green, meh grey. White is "right": a played note's
        // ring is white when its timing was perfect, its inside white when its
        // pitch was in tune.
        private static readonly Color4 perfect_colour = Color4Extensions.FromHex(@"fffaf0");

        /// <summary>The faint gold a perfect radiates (its halo, the inside's lower edge).</summary>
        private static readonly Color4 perfect_glow = Color4Extensions.FromHex(@"ffd98a");

        /// <summary>The target's ring while the perfect window is open: deeper than the perfects' own glow.</summary>
        private static readonly Color4 window_gold = Color4Extensions.FromHex(@"ffb300");

        /// <summary>The target's pulse: two quiet tones, swapped on every line of the grid.</summary>
        private static readonly Color4 pulse_a = Color4Extensions.FromHex(@"5fc9bf");

        private static readonly Color4 pulse_b = Color4Extensions.FromHex(@"9c86e6");

        private static readonly Color4 good_colour = Color4Extensions.FromHex(@"7cdb8a");
        private static readonly Color4 meh_colour = Color4Extensions.FromHex(@"9e9ea8");

        /// <summary>A missed note becomes a shadow: black, half seen (<see cref="missed_alpha"/>).</summary>
        private static readonly Color4 miss_colour = Color4.Black;

        private const float missed_alpha = 0.5f;

        /// <summary>Feedback for a miss: clearly red (the dim <see cref="miss_colour"/> marks notes already missed).</summary>
        private static readonly Color4 miss_feedback_colour = Color4Extensions.FromHex(@"ff5a6e");

        /// <summary>A note's name as the player chose to read it (letters or Do-Re-Mi; <see cref="KanNotation"/>).</summary>
        public static string NoteName(int midi) => KanNotation.Label(midi);

        public static Color4 GradeColour(LiveResult result) => result switch
        {
            LiveResult.Perfect => perfect_colour,
            LiveResult.Good => good_colour,
            LiveResult.Meh => meh_colour,
            _ => miss_feedback_colour,
        };

        // Built once: GetFont spells its weight anew on every call, every frame.
        private static readonly FontUsage name_font = OsuFont.GetFont(size: 14, weight: FontWeight.Black);
        private static readonly FontUsage next_name_font = OsuFont.GetFont(size: 16, weight: FontWeight.Black);
        private static readonly FontUsage feedback_font = OsuFont.GetFont(size: 17, weight: FontWeight.Bold);

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

        private readonly Box[] barBoxes = new Box[tick_pool];
        private readonly OsuSpriteText[] barLabels = new OsuSpriteText[bar_pool];
        private readonly int[] barNumbers = new int[bar_pool]; // what each label shows (its text is set only when it changes)
        private readonly Box[] lineBoxes = new Box[line_pool];
        private readonly OsuSpriteText[] lineLabels = new OsuSpriteText[line_pool];
        private readonly NoteView[] views = new NoteView[note_pool];
        private readonly Circle[] dots = new Circle[trail_pool];

        private KanNowLine nowLine = null!;
        private NextMarker nextMarker = null!;
        private HitTarget hitTarget = null!;
        private Countdown countdown = null!;

        /// <summary>The next note to play (the first not yet judged), or -1.</summary>
        private int next = -1;

        /// <summary>The note held at its pitch this frame (it burns at the line), or -1.</summary>
        private int holding = -1;

        private double lo = 60, hi = 84; // shown pitch range (smoothed)

        /// <summary>The time the staff shows (<see cref="KanPlayState.DisplayTime"/>).</summary>
        private double displayNow;
        private int firstVisible;
        private double lastDrawnMs = double.NegativeInfinity;

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

            for (int i = 0; i < tick_pool; i++)
                lines.Add(barBoxes[i] = new Box { Origin = Anchor.TopCentre, Alpha = 0 });

            for (int i = 0; i < bar_pool; i++)
            {
                lines.Add(barLabels[i] = new OsuSpriteText
                {
                    Origin = Anchor.BottomCentre,
                    Font = OsuFont.GetFont(size: 12, weight: FontWeight.SemiBold),
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
                // Under the notes: the next one slides into it.
                hitTarget = new HitTarget(),
                noteLayer,
                trailLayer,
                // Above the notes: a hit's impact lands on its note.
                nowLine = new KanNowLine(now_fraction, scene),
                nextMarker = new NextMarker(),
                countdown = new Countdown(),
                feedbackLayer,
            };

            // The feedback words' glyphs, loaded before the first note: the
            // first verdict loading them was a stutter on the first note.
            feedbackLayer.Add(new OsuSpriteText
            {
                Text = @"abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789·—♯↑↓?",
                Font = feedback_font,
                Shadow = true,
                Alpha = 0,
                AlwaysPresent = true,
            });

            state.Judged += onJudged;
            state.Unjudged += onUnjudged;
        }

        private float nowX => DrawWidth * now_fraction;

        /// <summary>Where a pitch crosses the now line, in screen space (for overlays outside the staff).</summary>
        public Vector2 LineScreenSpace(double midi) => ToScreenSpace(new Vector2(nowX, yAt(midi)));

        public KanScene Scene => scene;

        private float speed => (float)(DrawWidth * (1 - now_fraction) / ahead);

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
            followPace(now);
            drawLines();
            drawBars(now);
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
            // A replay rewound runs the clock backwards: a negative step once sent the range to infinity (owner 2026-10-22, crash).
            double k = Math.Min(1, Math.Abs(Time.Elapsed) / 600);

            lo += (targetLo - lo) * k;
            hi += (targetHi - hi) * k;

            if (!double.IsFinite(lo) || !double.IsFinite(hi) || hi - lo < 1)
                (lo, hi) = (targetLo, targetHi);
        }

        /// <summary>The time shown ahead follows the shortest pace coming (the notes' <see cref="KanHitObject.PaceMs"/>), eased so the staff zooms smoothly.</summary>
        private void followPace(double now)
        {
            double pace = double.PositiveInfinity;

            for (int i = firstVisible; i < notes.Count && notes[i].StartTime < now + max_ahead_ms; i++)
            {
                if (notes[i].StartTime >= now - 200 && notes[i].PaceMs > 0)
                    pace = Math.Min(pace, notes[i].PaceMs);
            }

            double target = double.IsPositiveInfinity(pace) ? max_ahead_ms : Math.Clamp(pace * notes_ahead, min_ahead_ms, max_ahead_ms);
            double k = Math.Min(1, Math.Abs(Time.Elapsed) / 800);

            ahead += (target - ahead) * k;
            if (!double.IsFinite(ahead)) ahead = target;
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

        private int firstBar;

        /// <summary>
        /// The bars travel with the notes (owner 2026-10-22: "the only way to
        /// know the bars is listening"): a line across the staff where each
        /// bar starts, its number above it; the beats, and the finest part of
        /// a beat the notes use, as shorter lines between.
        /// </summary>
        private void drawBars(double now)
        {
            var ticks = state.Ticks;
            double from = now - nowX / speed, until = now + ahead;

            // The time only moves forward in a play; a seek back starts the search again.
            if (firstBar >= ticks.Count || (firstBar > 0 && ticks[firstBar - 1].TimeMs >= from)) firstBar = 0;
            while (firstBar < ticks.Count && ticks[firstBar].TimeMs < from) firstBar++;

            float top = yAt(hi) - 14, bottom = yAt(lo) + 14;
            float full = Math.Max(1, bottom - top);
            float presence = 1 - 0.75f * scene.Rest;
            int used = 0, labelled = 0;

            // The line of the note whose perfect window is open now (as the judge places an attack) is gold.
            var window = perfectWindowOpen(now);

            if ((window != null) != wasGolden && Dev.KanDevPlay.Enabled)
                osu.Framework.Logging.Logger.Log($"kan! dev perfect window {(window != null ? "opens" : "closes")} at {now:0}");
            wasGolden = window != null;
            nowLine.PerfectWindow = window != null;

            for (int i = firstBar; i < ticks.Count && ticks[i].TimeMs <= until && used < tick_pool; i++)
            {
                var tick = ticks[i];
                float x = xAt(tick.TimeMs, now);

                // Faint coming in on the right, gone soon after the line.
                float fadeIn = Math.Clamp((DrawWidth - x) / 120f, 0, 1);
                float fadeOut = Math.Clamp((x - (nowX - 160)) / 160f, 0, 1);
                float alpha = fadeIn * fadeOut * presence;

                // The beats and their parts as shorter lines, centred on the staff.
                float length = tick.Kind switch
                {
                    Kan.Core.Score.Bars.TickKind.Bar => full,
                    Kan.Core.Score.Bars.TickKind.Beat => full * 0.45f,
                    _ => full * 0.2f,
                };

                var line = barBoxes[used++];
                line.X = x;
                line.Y = top + (full - length) / 2;
                line.Width = tick.Kind == Kan.Core.Score.Bars.TickKind.Bar ? 2 : 1.5f;
                line.Height = length;
                // Two quiet tones, one line each (owner 2026-10-22, after Sayonara Wild Hearts,
                // calmer): the colour changes as each line crosses the now line — the perfect is on the change.
                bool golden = window != null && Math.Abs(tick.TimeMs - window.StartTime) < 2;
                Color4 tone = golden ? window_gold : i % 2 == 0 ? pulse_a : pulse_b;
                float kindAlpha = tick.Kind == Kan.Core.Score.Bars.TickKind.Bar ? 0.35f : tick.Kind == Kan.Core.Score.Bars.TickKind.Beat ? 0.3f : 0.25f;

                line.Width = golden ? 4 : line.Width;
                line.Alpha = (golden ? 0.95f : kindAlpha) * alpha;
                line.Colour = tick.Kind == Kan.Core.Score.Bars.TickKind.Bar && !golden ? ColourInfo.GradientVertical(tone.Opacity(0.4f), tone) : tone;

                if (tick.Kind != Kan.Core.Score.Bars.TickKind.Bar || labelled >= bar_pool) continue;

                var label = barLabels[labelled];
                label.X = x;
                label.Y = top - 2;
                label.Alpha = 0.45f * alpha;

                if (barNumbers[labelled] != tick.Bar)
                {
                    barNumbers[labelled] = tick.Bar;
                    label.Text = tick.Bar.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }

                labelled++;
            }

            for (int i = used; i < tick_pool; i++)
                barBoxes[i].Alpha = 0;

            for (int i = labelled; i < bar_pool; i++)
                barLabels[i].Alpha = 0;
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
            double to = now + ahead + 200;

            // A replay rewound: the notes behind come back (owner 2026-10-22: they never did).
            if (now < lastDrawnMs - 1) firstVisible = 0;
            lastDrawnMs = now;

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

                // "By heart": a note shows only for a moment as it comes into view (the last 150 ms of it fading).
                if (state.SeenForMs is { } seen && state.Verdicts[i] == null)
                    view.Alpha *= (float)Math.Clamp((n.StartTime - now - (ahead - seen)) / Math.Min(150, seen), 0, 1);
            }

            for (int i = used; i < note_pool; i++)
                views[i].Alpha = 0;

            // Where the next note will cross the line, marked on the line itself.
            if (next >= 0 && !state.HideNotes && state.SeenForMs == null)
            {
                nextMarker.Point(nowX - head / 2 - 6, yAt(notes[next].Midi), notes[next], notes[next].StartTime - now, scene.NoteLight);
                pointTarget(now, next);
            }
            else
            {
                nextMarker.Point(0, 0, null, 0, Color4.White);
                hitTarget.Point(0, 0, null, 0, Color4.White);
            }
        }

        /// <summary>
        /// The target circle stays on the note being held until it ends (owner
        /// 2026-10-21: jumping to the next one while the last still sounded
        /// distracted), then goes to the next; into a slurred (legato) note it
        /// glides over the held note's last moments — the slur the judge takes
        /// by its pitch arriving.
        /// </summary>
        private void pointTarget(double now, int next)
        {
            int held = -1;

            for (int i = Math.Max(0, firstVisible - 4); i < next; i++)
            {
                var n = notes[i];
                if (n.StartTime <= now && now < n.EndMs && state.Verdicts[i] is not { Result: LiveResult.Miss })
                    held = i;
            }

            var to = notes[next];

            if (held < 0)
            {
                hitTarget.Point(nowX, yAt(to.Midi), to, to.StartTime - now, scene.NoteLight);
                return;
            }

            var from = notes[held];

            if (to.Articulation == Articulation.Legato)
            {
                double glide = Math.Min(180, from.DurationMs / 2);
                float k = (float)Math.Clamp((now - (from.EndMs - glide)) / glide, 0, 1);
                k = k * k * (3 - 2 * k);
                hitTarget.Point(nowX, yAt(from.Midi) + (yAt(to.Midi) - yAt(from.Midi)) * k, k > 0 ? to : from, 0, scene.NoteLight, follow: true);
                return;
            }

            hitTarget.Point(nowX, yAt(from.Midi), from, 0, scene.NoteLight);
        }

        private bool wasGolden;

        /// <summary>The note whose perfect window is open now, if any.</summary>
        private KanHitObject? perfectWindowOpen(double now)
        {
            for (int i = Math.Max(0, firstVisible - 4); i < notes.Count && notes[i].StartTime <= now + 400; i++)
            {
                if (state.PerfectNow(notes[i], now))
                    return notes[i];
            }

            return null;
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
                countdown.Fade(Math.Abs(Time.Elapsed));
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

        /// <summary>Taken back (a replay rewound): the note is drawn as not yet played, its transition ready to play again.</summary>
        private void onUnjudged(int index)
        {
            judgedAt[index] = double.NaN;
            heldUntil[index] = 0;
        }

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
                    // The grade's own word, as in osu!, and what cost the perfect (owner 2026-10-22:
                    // time and pitch judged together): early/late only when off the perfect's window.
                    text = v.Result switch
                    {
                        LiveResult.Perfect => @"perfect",
                        LiveResult.Good => @"good",
                        _ => @"meh",
                    };
                    if (v.Result != LiveResult.Perfect && !state.Judge.WithinPerfect(v.OffsetMs, n.PaceMs, n.StartTime, state.PreviousStart(n), state.NextStart(n)))
                        text += v.OffsetMs < 0 ? @" · early" : @" · late";
                    if (v.Reason == LiveReason.PitchUnclear) text += @" · pitch unclear";
                    else if (v.Cents > state.Judge.PerfectCents) text += @" · sharp ↑";
                    else if (v.Cents < -state.Judge.PerfectCents) text += @" · flat ↓";
                    break;
            }

            var label = new OsuSpriteText
            {
                Text = text,
                Font = feedback_font,
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
            state.Unjudged -= onUnjudged;
            base.Dispose(isDisposing);
        }
    }
}
