// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Kan.Forge.Play
{
    /// <summary>
    /// Three grades (owner 2026-10-21: "five are too many — less is more";
    /// osu!mania's great and ok are gone).
    /// </summary>
    public enum LiveResult
    {
        Perfect,
        Good,
        Meh,
        Miss,
    }

    /// <summary>Why a note got its result — what the player is told, in musical words.</summary>
    public enum LiveReason
    {
        /// <summary>The right note, judged by its timing.</summary>
        Played,

        /// <summary>A note was heard on time, but its pitch could not be told: accepted, at most "meh".</summary>
        PitchUnclear,

        /// <summary>Something was played on time, but another note.</summary>
        WrongNote,

        /// <summary>Nothing was heard near the note.</summary>
        NotHeard,
    }

    /// <summary>The judgement of one score note.</summary>
    /// <param name="PlayedId">The heard note it took (its onset), or null when timed by the pitch readings (a slur) or none was heard.</param>
    public readonly record struct LiveVerdict(LiveResult Result, LiveReason Reason, double OffsetMs, int? PlayedMidi, float Cents, uint? PlayedId = null);

    /// <summary>
    /// Pairs what the microphone heard with the score and judges each note
    /// (libkan describes, the game judges). Played notes arrive as facts:
    /// an onset first, its pitch a moment later (provisional within ~45 ms,
    /// final within ~200 ms). A score note is decided by the closest unclaimed
    /// onset within the meh window that has its pitch; while an onset's
    /// pitch is still coming, the note waits (up to <see cref="PITCH_WAIT_MS"/>
    /// past the window). Each played note answers one score note at most.
    /// Times are gameplay times (ms), already corrected for the mic delay.
    /// </summary>
    public class LiveJudge
    {
        /// <summary>
        /// The windows are a share of the note's pace — the time to its nearest
        /// neighbour (<see cref="PaceOf"/>) — either side of it, held within
        /// bounds (ms). It was the tempo grid's beat (owner 2026-10-21: "a
        /// percentage of the period … it should sound good, not stress"), but a
        /// beat is how the music was written, not how fast it goes: the same
        /// song timed at 130 BPM in quarters or 32.5 in whole notes judged four
        /// times apart (owner 2026-10-23: "the current system is no use"). Fast
        /// notes are judged tighter, slow ones looser, and never past what the
        /// ear lets pass. Notes one beat apart at 120 BPM keep their windows:
        /// ±100, ±225, ±400 ms.
        /// </summary>
        public const double PERFECT_SHARE = 0.20;

        public const double GOOD_SHARE = 0.45;

        public const double MEH_SHARE = 0.80;

        /// <summary>
        /// The bounds of each window (ms). A perfect is never wider than an
        /// early or late note still sounds right (~120 ms), nor narrower than a
        /// player can aim (50 ms); a good and a meh keep the old 60 to 200 BPM
        /// beat's bounds (forgiving: a slow note far off is still the note).
        /// </summary>
        public const double PERFECT_MIN_MS = 50, PERFECT_MAX_MS = 120;

        public const double GOOD_MIN_MS = 135, GOOD_MAX_MS = 450;

        public const double MEH_MIN_MS = 240, MEH_MAX_MS = 800;

        /// <summary>The pace assumed for a note alone (no neighbours, or not known).</summary>
        public const double UNKNOWN_PACE_MS = 500;

        /// <summary>Notes starting this close together are a chord: one moment, no pace between them.</summary>
        public const double CHORD_MS = 30;

        /// <summary>
        /// A note's pace: the time to the nearest note before or after it that
        /// starts at another moment (<see cref="CHORD_MS"/>), or 0 when it has
        /// none. <paramref name="starts"/> in order.
        /// </summary>
        public static double PaceOf(IReadOnlyList<double> starts, int index)
        {
            double at = starts[index], pace = double.PositiveInfinity;

            for (int i = index - 1; i >= 0; i--)
            {
                if (at - starts[i] < CHORD_MS) continue;

                pace = at - starts[i];
                break;
            }

            for (int i = index + 1; i < starts.Count; i++)
            {
                if (starts[i] - at < CHORD_MS) continue;

                pace = Math.Min(pace, starts[i] - at);
                break;
            }

            return double.IsPositiveInfinity(pace) ? 0 : pace;
        }

        /// <summary>A grade's window (± ms) at a note's pace (0 = unknown), before the judge's scale and before any neighbour narrows it.</summary>
        public static double WindowMs(LiveResult result, double paceMs)
        {
            double pace = paceMs > 0 ? paceMs : UNKNOWN_PACE_MS;

            return result switch
            {
                LiveResult.Perfect => Math.Clamp(pace * PERFECT_SHARE, PERFECT_MIN_MS, PERFECT_MAX_MS),
                LiveResult.Good => Math.Clamp(pace * GOOD_SHARE, GOOD_MIN_MS, GOOD_MAX_MS),
                _ => Math.Clamp(pace * MEH_SHARE, MEH_MIN_MS, MEH_MAX_MS),
            };
        }

        /// <summary>Scales the timing windows: easier above 1 ("easy"), stricter below ("hard rock").</summary>
        public double WindowScale { get; set; } = 1;

        /// <summary>
        /// The pitch's share of a grade (owner 2026-10-22: "time and pitch are
        /// both judged — out of tune and late may make a meh, a good in pitch
        /// and a perfect in time something between"). A grade is an ellipse
        /// in time × pitch: its timing window on one axis, its pitch budget on
        /// the other — in tune, exactly the timing windows. This is the good's
        /// budget (cents); the perfect's is <see cref="PERFECT_PITCH_SHARE"/> of it, and any pitch within
        /// <see cref="ACCEPT_CENTS"/> is the note, at least a meh. The Easy
        /// mod widens it (owner 2026-10-15). Pitches are read after the
        /// instrument's learned tuning is taken off (any instrument plays fair).
        /// </summary>
        public double PitchToleranceCents { get; set; } = 50;

        /// <summary>
        /// The perfect's share of the pitch budget: 35 cents of the good's 50
        /// (owner 2026-10-23: "a little more lax for a perfect by pitch,
        /// without a bigger ellipse — another note must still be told and
        /// punished"; it was half). The good's budget and the semitone stay.
        /// </summary>
        public const double PERFECT_PITCH_SHARE = 0.7;

        /// <summary>The perfect's pitch budget (cents).</summary>
        public double PerfectCents => PitchToleranceCents * PERFECT_PITCH_SHARE;

        /// <summary>
        /// How far off (cents) a played pitch is still the score's note — a
        /// semitone (owner 2026-10-22: air pressure moves a wind a semitone
        /// either way; learning to place it should not cost the streak). Past
        /// it, it is another note.
        /// </summary>
        public const double ACCEPT_CENTS = 100;

        /// <summary>Whether a pitch (fractional MIDI) is in tune enough to read as <paramref name="midi"/> (a tuner's nearest note, or Easy's wider band).</summary>
        public bool Matches(float pitch, int midi) => Math.Abs(pitch - midi) * 100 < PitchToleranceCents;

        /// <summary>Whether a pitch (fractional MIDI) is taken for <paramref name="midi"/> at all (<see cref="ACCEPT_CENTS"/>).</summary>
        public static bool Accepts(float pitch, int midi) => Math.Abs(pitch - midi) * 100 <= ACCEPT_CENTS + 0.01;

        /// <summary>
        /// Whether a reading is <paramref name="midi"/>: accepted for it, and no
        /// nearer to another note the score has around (a semitone neighbour
        /// still sounding is that neighbour, not this note out of tune).
        /// </summary>
        private static bool readsAs(float pitch, int midi, IReadOnlyCollection<int>? expected)
        {
            if (!Accepts(pitch, midi)) return false;

            if (expected != null)
            {
                foreach (int e in expected)
                {
                    if (e != midi && Math.Abs(pitch - e) < Math.Abs(pitch - midi)) return false;
                }
            }

            return true;
        }

        private static bool accepts(Played p, int midi) => p.Midi is int m && Accepts(m + p.Cents / 100, midi);

        /// <summary>How far (cents, signed) a heard note is from <paramref name="midi"/>.</summary>
        private static float centsFrom(Played p, int midi) => p.Midi is int m ? m == midi ? p.Cents : (m - midi) * 100 + p.Cents : 0;

        /// <summary>A heard note's distance from a perfect, in perfect ellipses (time × pitch; set windows first).</summary>
        private double distance(Played p, double startMs, float cents)
        {
            double offset = p.TimeMs - startMs;
            double reach = side(perfect, offset);
            double t = reach > 0 ? offset / reach : offset == 0 ? 0 : 1e6;
            double c = cents / PerfectCents;
            return t * t + c * c;
        }

        // The note being judged's windows and how far each side may reach
        // (Judge sets them; single-threaded, as the game calls it).
        private double perfect, good, meh;
        private double earlyReach = double.PositiveInfinity, lateReach = double.PositiveInfinity;

        private void setWindows(double paceMs, double startMs, double prevStartMs, double nextStartMs)
        {
            perfect = WindowMs(LiveResult.Perfect, paceMs) * WindowScale;
            good = WindowMs(LiveResult.Good, paceMs) * WindowScale;
            meh = WindowMs(LiveResult.Meh, paceMs) * WindowScale;

            // No window reaches past halfway to the next note or back to the
            // previous one (owner 2026-10-21): a skipped note cannot take its
            // neighbour's breath, and a missed one is told before the next comes.
            earlyReach = (startMs - prevStartMs) / 2;
            lateReach = (nextStartMs - startMs) / 2;
        }

        /// <summary>
        /// Whether an attack <paramref name="offsetMs"/> from a note's start
        /// is within its perfect window, as <see cref="Judge"/> would grade it
        /// (the pace's share, this judge's scale, narrowed by the neighbours).
        /// Leaves the judge as it was: for showing the window (owner 2026-10-22).
        /// </summary>
        public bool WithinPerfect(double offsetMs, double paceMs, double startMs, double prevStartMs = double.NegativeInfinity, double nextStartMs = double.PositiveInfinity)
        {
            double window = WindowMs(LiveResult.Perfect, paceMs) * WindowScale;
            double reach = offsetMs < 0 ? (startMs - prevStartMs) / 2 : (nextStartMs - startMs) / 2;
            return Math.Abs(offsetMs) <= Math.Min(window, reach);
        }

        /// <summary>A window on the side of <paramref name="offset"/>, narrowed by the neighbour there.</summary>
        private double side(double window, double offset) => Math.Min(window, offset < 0 ? earlyReach : lateReach);

        private bool withinMeh(double offset) => Math.Abs(offset) <= side(meh, offset);

        /// <summary>
        /// How long after its window a note waits before it is called not
        /// heard: an attack at the window's edge is still on its way (libkan
        /// confirms an onset some 30–90 ms after it sounds).
        /// </summary>
        public const double HEARING_MS = 100;

        /// <summary>How long after the window a note waits for a heard onset's pitch.</summary>
        public const double PITCH_WAIT_MS = 300;

        /// <summary>A wrong note started this long before a note's start is still a try at it (earlier: it belonged to what came before).</summary>
        public const double TRY_BEFORE_MS = 100;

        /// <summary>
        /// A wrong pitch held this many readings in a row (10 ms each) is
        /// confirmed. libkan's final pitch comes only when a note ends — for a
        /// wrong note held until the right one is found, at the very moment of
        /// finding it (owner 2026-10-17) — so the readings confirm it sooner.
        /// </summary>
        public const int WRONG_HOLD_READINGS = 15;

        /// <summary>
        /// A heard note whose pitch is not final yet is the score's note when
        /// the readings after its attack hold that note this many in a row
        /// (2026-10-23: a C5 attacked out of an F took the F's tail as its
        /// provisional pitch, and its final one came only when the long note
        /// ended — past the judge's wait). 160 ms: about one cycle of a 5–7 Hz
        /// vibrato, so the median is its centre.
        /// </summary>
        public const int SETTLE_READINGS = 16;

        /// <summary>The held run must start this soon after the attack (later, it is another note's).</summary>
        public const double SETTLE_WITHIN_MS = 200;

        /// <summary>
        /// Whether a played pitch is a wrong try at <paramref name="midi"/>,
        /// not one of the ones a learner forgives (owner 2026-10-17): the note
        /// an octave off (the detector's slip, not the player's), or a note
        /// the score expects around it — the previous note still sounding
        /// (a typical learner's lag), a chord's other notes, the next note
        /// come early.
        /// </summary>
        public static bool IsWrongTry(int played, int midi, IReadOnlyCollection<int>? expected)
        {
            if (played <= 0 || sameNote(played, midi)) return false;

            if (expected != null)
            {
                foreach (int e in expected)
                {
                    if (sameNote(played, e)) return false;
                }
            }

            return true;

            static bool sameNote(int a, int b) => ((a - b) % 12 + 12) % 12 == 0;
        }

        private class Played
        {
            public uint Id;
            public double TimeMs;
            public int? Midi;
            public float Cents;
            public bool Final;
            public bool Unresolved;
            public bool Claimed;
        }

        private readonly List<Played> played = new List<Played>();

        /// <summary>Recent pitch readings (gameplay time, fractional MIDI), oldest first.</summary>
        private readonly List<(double TimeMs, float Midi)> frames = new List<(double, float)>();

        /// <summary>A legato note is judged by the pitch sounding around its start, from this long before…</summary>
        public const double LEGATO_FROM_MS = 80;

        /// <summary>…to this long after (a slur has no attack to find).</summary>
        public const double LEGATO_TO_MS = 150;

        /// <summary>A pitch reading (every 10 ms while something sounds).</summary>
        public void Frame(double timeMs, float midi)
        {
            if (midi > 0) frames.Add((timeMs, midi));
        }

        /// <summary>A note started (its onset, in gameplay time).</summary>
        public void Heard(uint id, double timeMs) => played.Add(new Played { Id = id, TimeMs = timeMs });

        /// <summary>The pitch of a heard note. A final pitch replaces a provisional one.</summary>
        public void Pitch(uint id, int midi, float cents, bool final)
        {
            var p = find(id);
            if (p == null || (p.Final && !final)) return;

            p.Midi = midi;
            p.Cents = cents;
            p.Final = final;
            p.Unresolved = false;
        }

        /// <summary>The pitch of a heard note could not be told.</summary>
        public void Unresolved(uint id, bool final)
        {
            var p = find(id);
            if (p == null || p.Midi != null || !final) return;

            p.Unresolved = true;
            p.Final = true;
        }

        /// <summary>Forget played notes long past (they can no longer answer any note).</summary>
        public void Forget(double beforeMs)
        {
            played.RemoveAll(p => p.TimeMs < beforeMs);
            frames.RemoveAll(f => f.TimeMs < beforeMs);
        }

        /// <summary>
        /// Whether a played note that would answer this score note has been heard
        /// (unclaimed, within the window, with its pitch). For the wait mode:
        /// a note already played on time needs no waiting.
        /// </summary>
        public bool HasHeard(double startMs, int midi, bool anyPitch, double paceMs = 0, double prevStartMs = double.NegativeInfinity, double nextStartMs = double.PositiveInfinity)
        {
            setWindows(paceMs, startMs, prevStartMs, nextStartMs);

            foreach (var p in played)
            {
                if (!p.Claimed && withinMeh(p.TimeMs - startMs) && (anyPitch || accepts(p, midi)))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Whether the note's pitch sounded around its start — at least
        /// <paramref name="readings"/> readings in a row at it, from just
        /// before the start up to <paramref name="untilMs"/> — even without an
        /// attack of its own (a short note slurred into, 2026-10-15: the wait
        /// mode stopped for a 123 ms note that had been played).
        /// </summary>
        public bool PitchSounded(double startMs, int midi, double untilMs, int readings = 4)
        {
            int run = 0;

            foreach (var (time, m) in frames)
            {
                if (time < startMs - LEGATO_FROM_MS || time > untilMs) continue;

                run = Matches(m, midi) ? run + 1 : 0;
                if (run >= readings) return true;
            }

            return false;
        }

        /// <summary>
        /// Judge the score note starting at <paramref name="startMs"/>, or null
        /// while it cannot be decided yet. <paramref name="anyPitch"/>: the score
        /// itself does not know the pitch (the transcriber could not tell).
        /// <paramref name="legato"/>: the note is slurred from the previous one
        /// — no attack is expected, so the pitch sounding at its start counts.
        /// <paramref name="expected"/>: the notes the score has around this one
        /// (the previous, a chord's, the next): played, they are not wrong
        /// tries at it (<see cref="IsWrongTry"/>).
        ///
        /// A wrong try — its pitch confirmed, before the right note — is a
        /// miss at once (owner 2026-10-17: reported only once the right note
        /// came, the penalty landed on the moment of finding it). A slip put
        /// right before its pitch is confirmed is no try: the right note wins.
        /// </summary>
        /// <param name="paceMs">The note's pace (ms, <see cref="PaceOf"/>), which sizes the windows (<see cref="WindowMs"/>); 0 = unknown.</param>
        /// <param name="prevStartMs">The previous score note's start: the window reaches back at most halfway to it.</param>
        /// <param name="nextStartMs">The next score note's start: the window reaches on at most halfway to it.</param>
        public LiveVerdict? Judge(double startMs, int midi, bool anyPitch, double nowMs, bool legato = false, IReadOnlyCollection<int>? expected = null, double paceMs = 0,
                                  double prevStartMs = double.NegativeInfinity, double nextStartMs = double.PositiveInfinity)
        {
            setWindows(paceMs, startMs, prevStartMs, nextStartMs);

            Played? best = null, unclear = null, wrong = null, wrongProvisional = null;
            float bestCents = 0;
            bool waiting = false, bestSettling = false;
            double pendingNearest = double.PositiveInfinity; // the nearest attack whose pitch has not come

            foreach (var p in played)
            {
                if (p.Claimed || !withinMeh(p.TimeMs - startMs) || continuesATry(p, startMs)) continue;

                bool known = p.Midi != null;

                // Its pitch: the heard one, or the one its readings settle on — not
                // known yet, or a first reading out of a perfect's budget: a wind's
                // attack takes a moment to find its pitch (the nearer of the two is
                // graded). A final pitch is the whole note's centre already.
                float? cents = 0;
                bool settling = false;

                if (!anyPitch)
                {
                    float? heard = known && accepts(p, midi) ? centsFrom(p, midi) : null;
                    float? settled = heard == null ? (p.Final ? null : settledOn(p, midi, expected, nowMs, out settling))
                        : !p.Final && Math.Abs(heard.Value) > PerfectCents && withinPerfectTime(p.TimeMs - startMs) ? settledOn(p, midi, expected, nowMs, out settling)
                        : null;
                    cents = heard is { } h && (settled is not { } st || Math.Abs(st) >= Math.Abs(h)) ? heard : settled;
                    settling &= !(cents is { } now && Math.Abs(now) <= PerfectCents);
                }

                if (cents is { } c)
                {
                    // The one nearest a perfect in time × pitch (a slip a semitone off loses to the right note a moment later).
                    if (best == null || distance(p, startMs, c) < distance(best, startMs, bestCents))
                    {
                        best = p;
                        bestCents = c;
                        bestSettling = settling;
                    }
                }
                else if (p.Unresolved)
                    unclear ??= p;
                else if (known && (p.Final || held(p)))
                {
                    // The notes around it are no tries at this one: let the window run.
                    if (IsWrongTry(p.Midi!.Value, midi, expected)) wrong ??= p;
                }
                else
                {
                    waiting = true; // no pitch yet, or a provisional one that may still change
                    pendingNearest = Math.Min(pendingNearest, Math.Abs(p.TimeMs - startMs));
                    if (known) wrongProvisional ??= p;
                }
            }

            double closes = startMs + side(meh, 1);

            if (best != null)
                return bestSettling || mayComeNearer(best, startMs, nowMs, pendingNearest, closes) ? null
                    : claim(best, startMs, midi, anyPitch, best.Unresolved ? LiveReason.PitchUnclear : LiveReason.Played, bestCents);

            if (wrong != null && wrong.TimeMs >= startMs - TRY_BEFORE_MS)
            {
                wrong.Claimed = true;
                return new LiveVerdict(LiveResult.Miss, LiveReason.WrongNote, wrong.TimeMs - startMs, wrong.Midi, wrong.Cents, wrong.Id);
            }

            // No attack found, but the note's pitch arrived at its start: a slur
            // (legato), or an attack too soft to mark an onset. A note held over
            // from before does not count — a repeated note needs its own attack.
            if (!anyPitch && nowMs >= startMs + LEGATO_TO_MS && reachedPitch(startMs, midi, !legato, expected) is { } reached)
                return grade(reached.TimeMs - startMs, LiveReason.Played, midi, reached.Cents);

            bool windowOpen = nowMs <= closes + HEARING_MS;
            bool canWait = nowMs <= closes + PITCH_WAIT_MS;

            if (windowOpen || (waiting && canWait))
                return null;

            if (unclear != null)
                return claim(unclear, startMs, midi, anyPitch, LiveReason.PitchUnclear, 0);

            // Out of time: a provisional pitch is the best word we have on what was played.
            if (wrongProvisional != null && IsWrongTry(wrongProvisional.Midi!.Value, midi, expected))
                wrong ??= wrongProvisional;

            if (wrong != null)
                return new LiveVerdict(LiveResult.Miss, LiveReason.WrongNote, wrong.TimeMs - startMs, wrong.Midi, wrong.Cents, wrong.Id);

            return new LiveVerdict(LiveResult.Miss, LiveReason.NotHeard, 0, null, 0);
        }

        /// <summary>
        /// The longest break in the readings a held note may have and still
        /// be the same sound (a breath's swell dips the level; a tongue
        /// stopping the note leaves ~150 ms of silence).
        /// </summary>
        public const double SAME_SOUND_GAP_MS = 50;

        /// <summary>
        /// Whether a played note only goes on with an earlier try at this note
        /// (owner 2026-10-23: a C6 played 258 ms early and held swelled twice;
        /// each swell came back as an attack and the one nearest the line, 100
        /// ms late, was taken — "good · late" for an early note). An attack of
        /// the same pitch that no note took, in this note's window, sounded
        /// without a break up to it: one sound, one try, as in osu!. A repeated
        /// note is no such case — its previous attack was the previous note's.
        /// </summary>
        private bool continuesATry(Played p, double startMs)
        {
            if (p.Midi is not int midi) return false;

            foreach (var q in played)
            {
                if (q == p || q.Claimed || q.TimeMs >= p.TimeMs || q.Midi != midi || !withinMeh(q.TimeMs - startMs)) continue;
                if (soundedThrough(q.TimeMs, p.TimeMs, midi)) return true;
            }

            return false;
        }

        /// <summary>Whether the readings held <paramref name="midi"/> (within a semitone) from one time to the other, with no break longer than <see cref="SAME_SOUND_GAP_MS"/>.</summary>
        private bool soundedThrough(double fromMs, double toMs, int midi)
        {
            double last = fromMs;

            foreach (var (time, m) in frames)
            {
                if (time <= fromMs) continue;
                if (time > toMs) break;
                if (time - last > SAME_SOUND_GAP_MS || Math.Abs(m - midi) * 100 > ACCEPT_CENTS) return false;

                last = time;
            }

            return toMs - last <= SAME_SOUND_GAP_MS;
        }

        /// <summary>
        /// Whether an early attack that is no perfect may still lose to one
        /// nearer the line: it waits until the line is as far behind as it was
        /// ahead (an attack is heard <see cref="HEARING_MS"/> after it sounds),
        /// or for the pitch of an attack nearer than it. Late attacks have no
        /// such rival, so early ones are judged alike (owner 2026-10-23: an
        /// early note felt judged harder than a later one; a tongue cutting a
        /// note had opened one ~160 ms ahead of the next, and the judge took
        /// it at once).
        /// </summary>
        private bool mayComeNearer(Played best, double startMs, double nowMs, double pendingNearest, double closes)
        {
            double ahead = startMs - best.TimeMs;
            if (ahead <= 0 || Math.Abs(ahead) <= side(perfect, -ahead)) return false;

            return nowMs <= startMs + ahead + HEARING_MS && nowMs <= closes + HEARING_MS
                   || pendingNearest < ahead && nowMs <= closes + PITCH_WAIT_MS;
        }

        /// <summary>
        /// When the pitch reached the note around its start, if it sounded there
        /// for most of the readings, or null. <paramref name="mustArrive"/>: it
        /// must not have been sounding just before (it arrived for this note).
        /// A reading counts within <see cref="ACCEPT_CENTS"/> (<see cref="readsAs"/>); the pitch graded
        /// is their median (a vibrato is graded by its centre, not its swings).
        /// </summary>
        private (double TimeMs, float Cents)? reachedPitch(double startMs, int midi, bool mustArrive, IReadOnlyCollection<int>? expected)
        {
            int total = 0, before = 0, beforeOn = 0;
            double? first = null;
            var on = pitchScratch;
            on.Clear();

            foreach (var (time, m) in frames)
            {
                if (time >= startMs - 200 && time < startMs - 60)
                {
                    before++;
                    if (readsAs(m, midi, expected)) beforeOn++;
                }

                if (time < startMs - LEGATO_FROM_MS || time > startMs + LEGATO_TO_MS) continue;

                total++;

                if (readsAs(m, midi, expected))
                {
                    on.Add(m);
                    first ??= time;
                }
            }

            if (mustArrive && before > 0 && beforeOn * 2 >= before) return null; // held over, not arrived

            if (total < 5 || on.Count * 2 < total) return null;

            // Already sounding when the window opened (tied): nothing to time, so on time.
            double at = first!.Value <= startMs - LEGATO_FROM_MS + 10 ? startMs : first.Value;
            return (at, (median(on) - midi) * 100);
        }

        /// <summary>
        /// The cents from <paramref name="midi"/> its readings settled on after
        /// a heard note's attack — <see cref="SETTLE_READINGS"/> in a row that
        /// read as it (<see cref="readsAs"/>), starting within <see cref="SETTLE_WITHIN_MS"/>
        /// and before the next attack — as their median (a vibrato's centre), or null.
        /// Of every such run the nearest to the note (owner 2026-10-23: an
        /// ocarina's attack takes a moment of air pressure to find its pitch,
        /// "and it even sounds nice"). <paramref name="settling"/>: a nearer
        /// one may still come.
        /// </summary>
        private float? settledOn(Played p, int midi, IReadOnlyCollection<int>? expected, double nowMs, out bool settling)
        {
            double next = double.PositiveInfinity;

            foreach (var q in played)
            {
                if (q.TimeMs > p.TimeMs && q.TimeMs < next) next = q.TimeMs;
            }

            var run = runScratch;
            run.Clear();
            float? best = null;

            for (int i = 0; i < frames.Count; i++)
            {
                var (time, m) = frames[i];
                if (time < p.TimeMs) continue;
                if (time >= next) break;

                if (!readsAs(m, midi, expected))
                {
                    run.Clear();
                    continue;
                }

                run.Add(i);
                if (run.Count < SETTLE_READINGS) continue;

                // The last SETTLE_READINGS of the run: one settling, if it began soon enough.
                int from = run.Count - SETTLE_READINGS;
                if (frames[run[from]].TimeMs > p.TimeMs + SETTLE_WITHIN_MS) break;

                pitchScratch.Clear();
                for (int k = from; k < run.Count; k++) pitchScratch.Add(frames[run[k]].Midi);
                float cents = (median(pitchScratch) - midi) * 100;
                if (best is not { } b || Math.Abs(cents) < Math.Abs(b)) best = cents;
            }

            // A later window may still settle nearer: until the last one that may begin has been read.
            settling = double.IsPositiveInfinity(next)
                       && nowMs <= p.TimeMs + SETTLE_WITHIN_MS + SETTLE_READINGS * READING_MS + HEARING_MS;
            return best;
        }

        /// <summary>The readings' spacing (ms).</summary>
        private const double READING_MS = 10;

        /// <summary>Whether an attack's timing alone leaves room for a perfect (set windows first).</summary>
        private bool withinPerfectTime(double offset) => Math.Abs(offset) <= side(perfect, offset);

        /// <summary>A run of readings, as indices into <see cref="frames"/> (reused).</summary>
        private readonly List<int> runScratch = new List<int>();

        /// <summary>Readings gathered by one measurement (reused: the judge runs every frame).</summary>
        private readonly List<float> pitchScratch = new List<float>();

        /// <summary>The median of <paramref name="values"/> (sorts them in place).</summary>
        private static float median(List<float> values)
        {
            values.Sort();
            int half = values.Count / 2;
            return values.Count % 2 == 1 ? values[half] : (values[half - 1] + values[half]) / 2;
        }

        /// <summary>Whether a played note's pitch has held since its onset for <see cref="WRONG_HOLD_READINGS"/> readings in a row.</summary>
        private bool held(Played p)
        {
            float pitch = p.Midi!.Value + p.Cents / 100;
            int run = 0;

            foreach (var (time, m) in frames)
            {
                if (time < p.TimeMs) continue;

                run = Math.Abs(m - pitch) < 0.5f ? run + 1 : 0;
                if (run >= WRONG_HOLD_READINGS) return true;
            }

            return false;
        }

        /// <summary>Take a heard note for the score note <paramref name="midi"/>: graded on its timing and how far off its pitch is (<paramref name="cents"/>).</summary>
        private LiveVerdict claim(Played p, double startMs, int midi, bool anyPitch, LiveReason reason, float cents)
        {
            p.Claimed = true;

            // A note whose pitch the score does not know, or one not understood: its timing alone.
            if (anyPitch || reason == LiveReason.PitchUnclear)
                return grade(p.TimeMs - startMs, reason, p.Midi, p.Cents) with { PlayedId = p.Id };

            // Taken for the score's note: told as that note, sharp or flat by so much.
            return grade(p.TimeMs - startMs, reason, midi, cents) with { PlayedId = p.Id };
        }

        /// <summary>
        /// A grade from the timing <paramref name="offset"/> and the pitch's
        /// distance <paramref name="cents"/> (signed, from the score's note): the
        /// first ellipse it lies in — perfect, then good — else a meh. In tune
        /// (0 cents) the ellipses are the timing windows.
        /// </summary>
        private LiveVerdict grade(double offset, LiveReason reason, int? playedMidi, float cents)
        {
            var result = within(perfect, PerfectCents) ? LiveResult.Perfect
                : within(good, PitchToleranceCents) ? LiveResult.Good
                : LiveResult.Meh;

            // Heard but not understood: credit the timing, not more than "meh"
            // (owner 2026-10-15: noise must not pass for playing well).
            if (reason == LiveReason.PitchUnclear && result != LiveResult.Miss)
                result = LiveResult.Meh;

            return new LiveVerdict(result, reason, offset, playedMidi, cents);

            bool within(double window, double budget)
            {
                double reach = side(window, offset);
                double t = reach > 0 ? offset / reach : offset == 0 ? 0 : double.PositiveInfinity;
                double c = cents / budget;
                return t * t + c * c <= 1 + 1e-9;
            }
        }

        private Played? find(uint id)
        {
            for (int i = played.Count - 1; i >= 0; i--)
            {
                if (played[i].Id == id) return played[i];
            }

            return null;
        }
    }
}
