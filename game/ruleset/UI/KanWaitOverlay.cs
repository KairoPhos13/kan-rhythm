// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Linq;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Testing;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Screens.Play;
using osuTK;
using osuTK.Graphics;

namespace Kan.Ruleset.UI
{
    /// <summary>
    /// "Wait for me" while the level stands still (owner 2026-10-15: "the
    /// indicator must not freeze with the level, so the player always sees
    /// what they are playing and why it is wrong: higher, lower"). osu!
    /// updates nothing inside a stopped ruleset, so this lives in the HUD: it
    /// keeps hearing the player, and draws their pitch live on the now line
    /// against the note waited for: a dot with a short comet of what they
    /// just played, a line to the target, and plain words: higher, lower.
    /// </summary>
    public partial class KanWaitOverlay : CompositeDrawable
    {
        private const int comet = 48;
        private const double comet_ms = 1200;

        private static readonly Color4 right_colour = Color4Extensions.FromHex(@"7cdb8a");
        private static readonly Color4 near_colour = Color4Extensions.FromHex(@"ffd54f");
        private static readonly Color4 far_colour = Color4Extensions.FromHex(@"ff5a6e");

        private readonly Func<KanDrawableRuleset?> ruleset;
        private readonly CircularContainer target;
        private readonly Circle dot;
        private readonly Box stem;
        private readonly OsuSpriteText hint;
        private readonly OsuSpriteText wrong;
        private readonly Circle[] tail = new Circle[comet];

        private readonly Container content;
        private KanPlayState? subscribed;
        private Player? player;
        private Drawable? pauseOverlay;

        public KanWaitOverlay(Func<KanDrawableRuleset?> ruleset)
        {
            this.ruleset = ruleset;
            RelativeSizeAxes = Axes.Both;
            AlwaysPresent = true;

            // Drawn at the staff's scale (bigger screens, the note size setting).
            AddInternal(content = new Container { RelativeSizeAxes = Axes.Both });

            for (int i = 0; i < comet; i++)
                content.Add(tail[i] = new Circle { Origin = Anchor.Centre, Blending = BlendingParameters.Additive, Alpha = 0 });

            content.AddRange(new Drawable[]
            {
                stem = new Box { Origin = Anchor.TopCentre, Width = 3, Alpha = 0 },
                target = new CircularContainer
                {
                    Origin = Anchor.Centre,
                    Masking = true,
                    BorderThickness = 4,
                    BorderColour = Color4.White,
                    Alpha = 0,
                    Child = new Box { RelativeSizeAxes = Axes.Both, Alpha = 0, AlwaysPresent = true },
                },
                dot = new Circle { Origin = Anchor.Centre, Size = new Vector2(20), Alpha = 0, Blending = BlendingParameters.Additive },
                hint = new OsuSpriteText
                {
                    Origin = Anchor.CentreLeft,
                    Font = OsuFont.GetFont(size: 22, weight: FontWeight.Bold),
                    Shadow = true,
                    Alpha = 0,
                },
                wrong = new OsuSpriteText
                {
                    Origin = Anchor.BottomLeft,
                    Font = OsuFont.GetFont(size: 18, weight: FontWeight.Bold),
                    Colour = far_colour,
                    Shadow = true,
                    Alpha = 0,
                },
            });
        }

        /// <summary>The player opened the pause menu (the level is stopped for them, not for a note).</summary>
        public bool UserPaused
        {
            get
            {
                player ??= this.FindClosestParent<Player>();
                pauseOverlay ??= player?.ChildrenOfType<PauseOverlay>().FirstOrDefault();
                return pauseOverlay is OverlayContainer { State.Value: Visibility.Visible };
            }
        }

        protected override void Update()
        {
            base.Update();

            var r = ruleset();
            var state = r?.State;
            if (state == null) return;

            if (!ReferenceEquals(subscribed, state))
            {
                if (subscribed != null) subscribed.WrongWhileWaiting -= onWrong;
                subscribed = state;
                state.WrongWhileWaiting += onWrong;
            }

            // The ruleset is not updated while the level stands still: hear the player from here
            // (but not for the level while the pause menu is open).
            state.UserPaused = UserPaused;
            state.ListenWhileWaiting();

            var playfield = r!.Playfield as KanPlayfield;
            var staff = playfield?.Staff;

            float scale = playfield?.UiScale ?? 1;
            content.Scale = new Vector2(scale);
            content.Size = new Vector2(1 / scale);
            var n = state.WaitingFor;

            if (n == null || staff == null || UserPaused)
            {
                Alpha = Math.Max(0, Alpha - (float)(Math.Abs(Time.Elapsed) / 150));
                return;
            }

            Alpha = Math.Min(1, Alpha + (float)(Math.Abs(Time.Elapsed) / 150));

            Vector2 at(double midi) => content.ToLocalSpace(staff.LineScreenSpace(midi));

            var goal = at(n.Midi);
            double now = state.RealNowMs;
            float pulse = 0.5f + 0.5f * MathF.Sin((float)(Time.Current / 160));

            target.Position = goal;
            target.Size = new Vector2(46 + 10 * pulse);
            target.BorderColour = staff.Scene.NoteLight;
            target.Alpha = 0.6f + 0.4f * pulse;

            // The comet: what the player just played, drifting left in real time.
            int used = 0;

            for (int i = 0; i < state.LiveCount && used < comet; i++)
            {
                var p = state.LiveAt(i);
                double age = now - p.RealMs;
                if (age > comet_ms) break;
                if (p.Midi <= 0) continue;

                var c = tail[used++];
                var pos = at(p.Midi);
                c.Position = new Vector2(goal.X - (float)age * 0.12f, pos.Y);
                c.Size = new Vector2(5 + Math.Min(7, p.Level * 50));
                c.Colour = colourFor(MathF.Abs(p.Midi - n.Midi), n.PitchUnknown);
                c.Alpha = (float)(1 - age / comet_ms) * 0.8f;
            }

            for (int i = used; i < comet; i++)
                tail[i].Alpha = 0;

            // The dot: where the player is now, if they are sounding.
            bool sounding = state.LiveCount > 0 && now - state.LiveAt(0).RealMs < 150 && state.LiveAt(0).Midi > 0;

            if (sounding)
            {
                float midi = state.LiveAt(0).Midi;
                float off = midi - n.Midi;
                var pos = new Vector2(goal.X, at(midi).Y);
                var colour = colourFor(MathF.Abs(off), n.PitchUnknown);

                dot.Position = pos;
                dot.Colour = colour;
                dot.Alpha = 1;

                stem.Position = new Vector2(goal.X, Math.Min(pos.Y, goal.Y));
                stem.Height = Math.Abs(pos.Y - goal.Y);
                stem.Colour = colour;
                stem.Alpha = 0.5f;

                hint.Position = new Vector2(goal.X + 40, pos.Y);
                hint.Colour = colour;
                hint.Text = n.PitchUnknown || MathF.Abs(off) < 0.5f ? @"that's it, hold it"
                    : off < 0 ? $"higher ↑  ({semitones(-off)})"
                    : $"lower ↓  ({semitones(off)})";
            }
            else
            {
                dot.Alpha = 0;
                stem.Alpha = 0;
                hint.Position = new Vector2(goal.X + 40, goal.Y);
                hint.Colour = staff.Scene.NoteLight;
                hint.Text = $"play {(n.PitchUnknown ? "the note" : KanStaff.NoteName(n.Midi))}";
            }

            hint.Alpha = 1;
            wrong.Position = new Vector2(goal.X + 40, goal.Y - 26);
        }

        private static string semitones(float s)
        {
            int n = (int)MathF.Round(s);
            return n <= 1 ? @"a semitone" : n == 2 ? @"a tone" : $"{n} semitones";
        }

        private static Color4 colourFor(float semitonesOff, bool any)
            => any || semitonesOff < 0.5f ? right_colour : semitonesOff < 1.5f ? near_colour : far_colour;

        private void onWrong(int played)
        {
            var n = subscribed?.WaitingFor;
            if (n == null) return;

            wrong.Text = $"{KanStaff.NoteName(played)}: play {(n.PitchUnknown ? "the note" : KanStaff.NoteName(n.Midi))} · combo lost";
            wrong.ClearTransforms();
            wrong.Alpha = 1;
            wrong.Delay(900).FadeOut(400);
        }

        protected override void Dispose(bool isDisposing)
        {
            if (subscribed != null) subscribed.WrongWhileWaiting -= onWrong;
            base.Dispose(isDisposing);
        }
    }
}
