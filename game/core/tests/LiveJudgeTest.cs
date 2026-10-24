// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using NUnit.Framework;
using Kan.Core.Play;

namespace Kan.Core.Tests
{
    /// <summary>Judging live play against the score (gameplay MVP, owner 2026-10-14).</summary>
    [TestFixture]
    public class LiveJudgeTest
    {
        [Test]
        public void TimingDecidesTheResultOfTheRightNote()
        {
            // Three grades, shares of the pace (unknown: 500 ms): perfect ±100, good ±225, meh ±400 ms.
            var judge = new LiveJudge();
            double[] offsets = { 30, 150, 350 };
            var expected = new[] { LiveResult.Perfect, LiveResult.Good, LiveResult.Meh };

            for (int i = 0; i < offsets.Length; i++)
            {
                judge.Heard((uint)i + 1, 1000 * (i + 1) + offsets[i]);
                judge.Pitch((uint)i + 1, 72 + i, i == 0 ? 12 : 0, final: i != 0);
            }

            Assert.That(judge.Judge(1000, 72, false, 1100), Is.EqualTo(new LiveVerdict(LiveResult.Perfect, LiveReason.Played, 30, 72, 12, PlayedId: 1)));

            for (int i = 1; i < offsets.Length; i++)
                Assert.That(judge.Judge(1000 * (i + 1), 72 + i, false, 1000 * (i + 1) + 600)!.Value.Result, Is.EqualTo(expected[i]), $"{offsets[i]} ms");
        }

        [Test]
        public void ANoteWaitsWhileItsPitchIsComing()
        {
            var judge = new LiveJudge();
            judge.Heard(1, 1010);

            Assert.That(judge.Judge(1000, 72, false, 1550), Is.Null, "heard, pitch not known yet: wait past the window");

            judge.Pitch(1, 72, -8, final: true);
            Assert.That(judge.Judge(1000, 72, false, 1560)!.Value.Result, Is.EqualTo(LiveResult.Perfect));
        }

        [Test]
        public void WrongNotesAndSilenceAreMisses()
        {
            var judge = new LiveJudge();
            judge.Heard(1, 1000);
            judge.Pitch(1, 70, 0, final: true); // a whole tone off: another note (a semitone is the note out of tune)

            // A confirmed wrong try is a miss at once, while the window is still open (owner 2026-10-17).
            var wrong = judge.Judge(1000, 72, false, 1150)!.Value;
            Assert.That(wrong.Result, Is.EqualTo(LiveResult.Miss));
            Assert.That(wrong.Reason, Is.EqualTo(LiveReason.WrongNote));
            Assert.That(wrong.PlayedMidi, Is.EqualTo(70));

            var silent = judge.Judge(3000, 72, false, 3510)!.Value;
            Assert.That(silent.Reason, Is.EqualTo(LiveReason.NotHeard));

            // A provisional wrong pitch may still turn right: wait for it (but not forever).
            judge.Heard(2, 5000);
            judge.Pitch(2, 74, 0, final: false);
            Assert.That(judge.Judge(5000, 72, false, 5300), Is.Null);
            var stillWrong = judge.Judge(5000, 72, false, 5810)!.Value;
            Assert.That(stillWrong.Reason, Is.EqualTo(LiveReason.WrongNote), "out of time: the provisional pitch says what was played");
            Assert.That(stillWrong.PlayedMidi, Is.EqualTo(74));
        }

        [Test]
        public void TheNotesAroundAreNoWrongTries()
        {
            // Owner 2026-10-17: the previous note still sounding is a learner's lag,
            // not a wrong try — the right note a little late is good, not a miss.
            var judge = new LiveJudge();
            judge.Heard(1, 1010);
            judge.Pitch(1, 70, 0, final: true); // the previous note (70) again, at this one's start
            int[] around = { 70 };

            Assert.That(judge.Judge(1000, 72, false, 1080, expected: around), Is.Null, "not a try: the window runs on");

            judge.Heard(2, 1105);
            judge.Pitch(2, 72, 0, final: true);
            var late = judge.Judge(1000, 72, false, 1120, expected: around)!.Value;
            Assert.That(late.Result, Is.EqualTo(LiveResult.Good));
            Assert.That(late.Reason, Is.EqualTo(LiveReason.Played));

            // Only the previous note: still nothing else heard, so not heard (not "wrong note").
            judge.Heard(3, 3000);
            judge.Pitch(3, 70, 0, final: true);
            Assert.That(judge.Judge(3000, 72, false, 3510, expected: around)!.Value.Reason, Is.EqualTo(LiveReason.NotHeard));

            // A chord's other note is no wrong try at this one either.
            judge.Heard(4, 5000);
            judge.Pitch(4, 76, 0, final: true);
            Assert.That(judge.Judge(5000, 72, false, 5050, expected: new[] { 76 }), Is.Null);
        }

        [Test]
        public void AWrongPitchHeldIsConfirmedWithoutTheFinalPitch()
        {
            // libkan's final pitch comes when the note ends; the readings confirm it sooner.
            var judge = new LiveJudge();
            judge.Heard(1, 1000);
            judge.Pitch(1, 70, 0, final: false);

            for (int i = 0; i < LiveJudge.WRONG_HOLD_READINGS - 1; i++)
                judge.Frame(1010 + i * 10, 70.1f);

            Assert.That(judge.Judge(1000, 72, false, 1150), Is.Null, "not held long enough yet");

            judge.Frame(1010 + LiveJudge.WRONG_HOLD_READINGS * 10, 70.1f);
            var wrong = judge.Judge(1000, 72, false, 1160)!.Value;
            Assert.That(wrong.Reason, Is.EqualTo(LiveReason.WrongNote));
            Assert.That(wrong.PlayedMidi, Is.EqualTo(70));
        }

        [Test]
        public void TheReadingsSettleAnAttacksPitch()
        {
            // The owner's C5 (2026-10-23): attacked out of an F5 114 ms early, its
            // provisional pitch the F's tail, its final pitch only when the
            // 738 ms note ended. The readings held C5, 25–73 cents sharp.
            var judge = new LiveJudge();
            judge.Heard(1, 77156);
            judge.Pitch(1, 77, 43, final: false);
            judge.Frame(77171, 74.19f);

            Assert.That(judge.Judge(77270, 72, false, 77230, expected: new[] { 77, 72 }, prevStartMs: 77024, nextStartMs: 78500), Is.Null, "nothing settled yet");

            float[] held = { 72.68f, 72.73f, 72.57f, 72.54f, 72.48f, 72.36f, 72.35f, 72.37f, 72.49f, 72.51f, 72.54f, 72.53f, 72.51f, 72.44f, 72.37f, 72.26f };
            for (int i = 0; i < held.Length; i++) judge.Frame(77251 + i * 10, held[i]);

            var c5 = judge.Judge(77270, 72, false, 77620, expected: new[] { 77, 72 }, prevStartMs: 77024, nextStartMs: 78500)!.Value;
            Assert.That(c5.Reason, Is.EqualTo(LiveReason.Played));
            Assert.That(c5.PlayedMidi, Is.EqualTo(72));
            Assert.That(c5.OffsetMs, Is.EqualTo(-114).Within(0.01), "timed by its attack");
            Assert.That(c5.Cents, Is.EqualTo(50).Within(0.01), "the readings' median");
            Assert.That(c5.Result, Is.EqualTo(LiveResult.Meh), "early and sharp, but the note");

            // Readings settling on another note leave it to the other paths.
            var other = new LiveJudge();
            other.Heard(1, 1000);
            other.Pitch(1, 77, 0, final: false);
            for (int i = 0; i < 20; i++) other.Frame(1010 + i * 10, 74.9f);
            Assert.That(other.Judge(1000, 72, false, 1210), Is.Null, "a provisional other note: still waiting");

            // The previous note a semitone off, still sounding, is that note.
            var lag = new LiveJudge();
            lag.Heard(1, 1000);
            for (int i = 0; i < 20; i++) lag.Frame(1010 + i * 10, 76.1f);
            Assert.That(lag.Judge(1000, 77, false, 1210, expected: new[] { 76 }), Is.Null, "the E still sounding is no F");
            Assert.That(lag.Judge(1000, 77, false, 1461)!.Value.Result, Is.EqualTo(LiveResult.Meh), "no E around: an F 90 cents flat (once it could no more settle)");

            // A run that starts too long after the attack is another note's.
            var late = new LiveJudge();
            late.Heard(1, 1000);
            for (int i = 0; i < 20; i++) late.Frame(1000 + LiveJudge.SETTLE_WITHIN_MS + 20 + i * 10, 72f);
            Assert.That(late.Judge(1000, 72, false, 1100), Is.Null, "no pitch yet: waiting");
        }

        [Test]
        public void AVibratoIsGradedByItsCentre()
        {
            // A legato note played with a 6 Hz, ±60 cent vibrato around 10 cents sharp:
            // its swings leave the in-tune band, its centre does not.
            var judge = new LiveJudge();
            for (double t = 800; t < 1000; t += 10) judge.Frame(t, 81f);
            for (double t = 1000; t < 1300; t += 10) judge.Frame(t, 83.1f + 0.6f * MathF.Sin(2 * MathF.PI * (float)(t - 1000) / 160));

            var note = judge.Judge(1000, 83, false, 1160, legato: true)!.Value;
            Assert.That(note.Result, Is.EqualTo(LiveResult.Perfect));
            Assert.That(note.Cents, Is.EqualTo(10).Within(15));

            // So does a held one: its readings settle the attack's pitch.
            var held = new LiveJudge();
            held.Heard(1, 1010);
            for (double t = 1010; t < 1200; t += 10) held.Frame(t, 72.1f + 0.6f * MathF.Sin(2 * MathF.PI * (float)(t - 1010) / 160));
            Assert.That(held.Judge(1000, 72, false, 1200)!.Value.Result, Is.EqualTo(LiveResult.Perfect));
        }

        [Test]
        public void OneSoundIsOneTry()
        {
            // The owner's C6 (2026-10-23): played 258 ms early and held; it swelled
            // twice, a 40 ms break at the deepest, and each swell came back as an attack.
            var judge = new LiveJudge();
            foreach (var (id, at) in new[] { (1u, 41968.5), (2u, 42092.1), (3u, 42327.1) })
            {
                judge.Heard(id, at);
                judge.Pitch(id, 84, -2, final: true);
            }

            for (double t = 41998; t < 42600; t += 10)
                if (t < 42300 || t > 42330) judge.Frame(t, 84f);

            var c6 = judge.Judge(42227, 84, false, 42700, paceMs: 414, prevStartMs: 40984, nextStartMs: 42641)!.Value;
            Assert.That((c6.OffsetMs, c6.PlayedId), Is.EqualTo((41968.5 - 42227, (uint?)1)), "the first attack is the try");
            Assert.That(c6.Result, Is.EqualTo(LiveResult.Meh));

            // A break of a stopped note's length: a new try.
            var stopped = new LiveJudge();
            stopped.Heard(1, 900);
            stopped.Pitch(1, 72, 0, final: true);
            for (double t = 910; t < 940; t += 10) stopped.Frame(t, 72f);
            stopped.Heard(2, 1000);
            stopped.Pitch(2, 72, 0, final: true);
            for (double t = 1010; t < 1200; t += 10) stopped.Frame(t, 72f);
            Assert.That(stopped.Judge(1000, 72, false, 1300)!.Value.PlayedId, Is.EqualTo(2u));

            // A repeated note: its previous attack was the previous note's.
            var repeated = new LiveJudge();
            repeated.Heard(1, 500);
            repeated.Pitch(1, 72, 0, final: true);
            for (double t = 510; t < 1200; t += 10) repeated.Frame(t, 72f);
            repeated.Heard(2, 1000);
            repeated.Pitch(2, 72, 0, final: true);
            Assert.That(repeated.Judge(500, 72, false, 600, nextStartMs: 1000)!.Value.PlayedId, Is.EqualTo(1u));
            Assert.That(repeated.Judge(1000, 72, false, 1100, prevStartMs: 500)!.Value.PlayedId, Is.EqualTo(2u));
        }

        [Test]
        public void AnAttacksPitchMaySettle()
        {
            // An ocarina's attack (owner 2026-10-23): 45 cents sharp for 60 ms as
            // the air pressure finds the note, then 5 cents. The settled pitch is graded.
            var judge = new LiveJudge();
            judge.Heard(1, 1000);
            judge.Pitch(1, 72, 45, final: false);
            for (double t = 1000; t < 1060; t += 10) judge.Frame(t, 72.45f);
            Assert.That(judge.Judge(1000, 72, false, 1070), Is.Null, "a first pitch out of a perfect's budget waits to settle");

            for (double t = 1060; t < 1300; t += 10) judge.Frame(t, 72.05f);
            var settled = judge.Judge(1000, 72, false, 1300)!.Value;
            Assert.That((settled.Result, settled.Cents), Is.EqualTo((LiveResult.Perfect, 5f)).Using<(LiveResult, float)>((a, b) => a.Item1 == b.Item1 && Math.Abs(a.Item2 - b.Item2) < 0.01f));

            // One that stays out of tune is graded so once the wait is over.
            var sharp = new LiveJudge();
            sharp.Heard(1, 1000);
            sharp.Pitch(1, 72, 45, final: false);
            for (double t = 1000; t < 1300; t += 10) sharp.Frame(t, 72.45f);
            Assert.That(sharp.Judge(1000, 72, false, 1300), Is.Null);
            Assert.That(sharp.Judge(1000, 72, false, 1461)!.Value.Result, Is.EqualTo(LiveResult.Good));

            // Another note settles as nothing: still the wrong note.
            var wrong = new LiveJudge();
            wrong.Heard(1, 1000);
            wrong.Pitch(1, 74, 0, final: true);
            for (double t = 1000; t < 1300; t += 10) wrong.Frame(t, 74f);
            Assert.That(wrong.Judge(1000, 72, false, 1700)!.Value.Reason, Is.EqualTo(LiveReason.WrongNote));
        }

        [Test]
        public void AnEarlyAttackWaitsForANearerOne()
        {
            // Repeated D5s (owner's take 2026-10-23): an attack 160 ms early
            // with the note's pitch, then the real one on the line.
            var judge = new LiveJudge();
            judge.Heard(1, 840);
            judge.Pitch(1, 74, 0, final: true);
            Assert.That(judge.Judge(1000, 74, false, 900), Is.Null, "an early good is not taken before the line");
            Assert.That(judge.Judge(1000, 74, false, 1050), Is.Null, "nor as long as a nearer one may come");

            judge.Heard(2, 1007);
            Assert.That(judge.Judge(1000, 74, false, 1070), Is.Null, "the nearer one's pitch is awaited");
            judge.Pitch(2, 74, 0, final: false);
            var verdict = judge.Judge(1000, 74, false, 1110)!.Value;
            Assert.That((verdict.Result, verdict.OffsetMs), Is.EqualTo((LiveResult.Perfect, 7.0)));

            // Alone, it is graded as it was played once the line is as far behind.
            var alone = new LiveJudge();
            alone.Heard(1, 840);
            alone.Pitch(1, 74, 0, final: true);
            Assert.That(alone.Judge(1000, 74, false, 1200), Is.Null);
            var early = alone.Judge(1000, 74, false, 1261)!.Value;
            Assert.That((early.Result, early.OffsetMs), Is.EqualTo((LiveResult.Good, -160.0)));

            // An early perfect is taken at once.
            var perfect = new LiveJudge();
            perfect.Heard(1, 960);
            perfect.Pitch(1, 74, 0, final: true);
            Assert.That(perfect.Judge(1000, 74, false, 970)!.Value.Result, Is.EqualTo(LiveResult.Perfect));
        }

        [Test]
        public void ASlipPutRightBeforeItIsConfirmedIsNoTry()
        {
            var judge = new LiveJudge();
            judge.Heard(1, 990);
            judge.Pitch(1, 71, 0, final: false); // a slip, still provisional
            judge.Heard(2, 1040);
            judge.Pitch(2, 72, 0, final: true);
            judge.Pitch(1, 71, 0, final: true); // confirmed after the right note came

            Assert.That(judge.Judge(1000, 72, false, 1060)!.Value.Result, Is.EqualTo(LiveResult.Perfect), "the right note wins (40 ms: within perfect)");
        }

        /// <summary>
        /// The windows are a share of the note's pace, the time to its nearest
        /// neighbour (owner 2026-10-23): perfect 20 % within 50–120 ms, good
        /// 45 % within 135–450, meh 80 % within 240–800.
        /// </summary>
        [TestCase(500, 95, LiveResult.Perfect)]   // a beat apart at 120 BPM: perfect ±100
        [TestCase(500, 105, LiveResult.Good)]
        [TestCase(1657, 118, LiveResult.Perfect)] // slow notes: perfect held at ±120
        [TestCase(1657, 125, LiveResult.Good)]
        [TestCase(150, 48, LiveResult.Perfect)]   // fast notes: perfect held at ±50
        [TestCase(150, 55, LiveResult.Good)]
        [TestCase(0, 95, LiveResult.Perfect)]     // pace unknown: 500 ms
        [TestCase(1000, 790, LiveResult.Meh)]     // meh reaches ±800
        public void TheWindowsFollowThePace(double paceMs, double offset, LiveResult expected)
        {
            var judge = new LiveJudge();
            judge.Heard(1, 1000 + offset);
            judge.Pitch(1, 72, 0, final: true);

            Assert.That(judge.Judge(1000, 72, false, 2000, paceMs: paceMs)!.Value.Result, Is.EqualTo(expected));
        }

        [Test]
        public void APaceIsTheTimeToTheNearestNeighbour()
        {
            // The same notes, however the tempo was written (owner 2026-10-23).
            double[] starts = { 0, 461, 922, 2579, 2590, 4236 };
            Assert.That(LiveJudge.PaceOf(starts, 0), Is.EqualTo(461), "the first note: its next");
            Assert.That(LiveJudge.PaceOf(starts, 2), Is.EqualTo(461), "the nearer of the two");
            Assert.That(LiveJudge.PaceOf(starts, 3), Is.EqualTo(1657), "a chord mate is no neighbour");
            Assert.That(LiveJudge.PaceOf(starts, 5), Is.EqualTo(1646));
            Assert.That(LiveJudge.PaceOf(new[] { 1000.0 }, 0), Is.EqualTo(0), "alone: unknown");
        }

        [Test]
        public void TheNeighboursNarrowTheWindows()
        {
            // Eighths at 120 BPM (250 ms apart): no window reaches past halfway (±125).
            var judge = new LiveJudge();
            judge.Heard(1, 1130);
            judge.Pitch(1, 72, 0, final: true);

            Assert.That(judge.Judge(1000, 72, false, 1200, nextStartMs: 1250), Is.Null, "an attack at the edge may still be coming");
            Assert.That(judge.Judge(1000, 72, false, 1230, nextStartMs: 1250)!.Value.Reason, Is.EqualTo(LiveReason.NotHeard),
                "130 ms late is closer to the next note: missed, told before the next comes");

            var next = judge.Judge(1250, 72, false, 1471, prevStartMs: 1000)!.Value; // early: decided once the line is as far behind
            Assert.That(next.PlayedId, Is.EqualTo(1u), "the next note keeps its own breath");
            Assert.That(next.Result, Is.EqualTo(LiveResult.Good), "120 ms early: good, up to the halfway mark");

            var alone = new LiveJudge();
            alone.Heard(1, 1130);
            alone.Pitch(1, 72, 0, final: true);
            Assert.That(alone.Judge(1000, 72, false, 1200)!.Value.Result, Is.EqualTo(LiveResult.Good), "a note alone keeps the beat's windows");
        }

        [Test]
        public void AFlatNoteIsTheRightNoteOutOfTune()
        {
            // The owner's D6 (2026-10-22), read as C#6 +41 cents: 59 under D6, on time.
            LiveVerdict judge(float cents, int heardMidi, int[]? expected = null)
            {
                var j = new LiveJudge();
                j.Heard(1, 1000);
                j.Pitch(1, heardMidi, cents, final: true);
                return j.Judge(1000, 86, false, 2000, expected: expected)!.Value;
            }

            var flat = judge(41, 85);
            Assert.That(flat.Result, Is.EqualTo(LiveResult.Meh), "played, the streak kept: past the good's 50 cents");
            Assert.That(flat.Reason, Is.EqualTo(LiveReason.Played));
            Assert.That(flat.PlayedMidi, Is.EqualTo(86));
            Assert.That(flat.Cents, Is.EqualTo(-59).Within(0.5), "told as flat");

            Assert.That(judge(41, 85, new[] { 85, 86 }).Result, Is.EqualTo(LiveResult.Meh), "within a semitone it is the note, whatever is around");
            Assert.That(judge(0, 85).Result, Is.EqualTo(LiveResult.Meh), "a whole semitone under (air pressure): still the note");
            Assert.That(judge(-10, 85).Reason, Is.EqualTo(LiveReason.WrongNote), "past a semitone: another note");
            Assert.That(judge(-45, 86).Result, Is.EqualTo(LiveResult.Good), "45 cents: the good's budget");
            Assert.That(judge(20, 86).Result, Is.EqualTo(LiveResult.Perfect), "20 cents: in tune for a perfect");
        }

        [Test]
        public void TimeAndPitchAreJudgedTogether()
        {
            // 120 BPM: perfect ±100 ms × ±35 cents, good ±225 ms × ±50 cents (owner 2026-10-22: ellipses; 2026-10-23: a wider perfect).
            LiveResult judge(double offset, float cents)
            {
                var j = new LiveJudge();
                j.Heard(1, 1000 + offset);
                j.Pitch(1, 72, cents, final: true);
                return j.Judge(1000, 72, false, 3000, paceMs: 500)!.Value.Result;
            }

            Assert.That(judge(95, 0), Is.EqualTo(LiveResult.Perfect), "in tune: the timing windows as they were");
            Assert.That(judge(0, 34), Is.EqualTo(LiveResult.Perfect), "on time: the pitch budget");
            Assert.That(judge(0, 36), Is.EqualTo(LiveResult.Good), "on time, past the budget: a good");
            Assert.That(judge(50, 28), Is.EqualTo(LiveResult.Perfect), "half the window, 28 cents: 0.25 + 0.64");
            Assert.That(judge(70, 28), Is.EqualTo(LiveResult.Good), "0.7 of the window, 28 cents: 0.49 + 0.64 — both near the edge make a good");
            Assert.That(judge(150, 35), Is.EqualTo(LiveResult.Good), "2/3 of the good window, 35 cents: 0.44 + 0.49");
            Assert.That(judge(180, 35), Is.EqualTo(LiveResult.Meh), "0.8 of the good window, 35 cents: 0.64 + 0.49 — out of tune and late, a meh");
            Assert.That(judge(-180, -35), Is.EqualTo(LiveResult.Meh), "early and flat alike");
        }

        [Test]
        public void TheShownPerfectWindowIsTheJudgesOwn()
        {
            // The target's gold (owner 2026-10-22) must light exactly where the judge gives a perfect.
            foreach (double scale in new[] { 1.0, 0.7 })
            {
                foreach (double next in new[] { double.PositiveInfinity, 1120 })
                {
                    for (int offset = -150; offset <= 150; offset += 5)
                    {
                        var judge = new LiveJudge { WindowScale = scale };
                        judge.Heard(1, 1000 + offset);
                        judge.Pitch(1, 72, 0, final: true);

                        var verdict = judge.Judge(1000, 72, false, 2000, paceMs: 500, prevStartMs: 500, nextStartMs: next);
                        bool shown = judge.WithinPerfect(offset, 500, 1000, 500, next);
                        Assert.That(shown, Is.EqualTo(verdict?.Result == LiveResult.Perfect), $"offset {offset}, scale {scale}, next {next}");
                    }
                }
            }
        }

        [Test]
        public void WrongTriesForgiveTheOctaveAndTheNotesAround()
        {
            Assert.That(LiveJudge.IsWrongTry(71, 72, null), Is.True);
            Assert.That(LiveJudge.IsWrongTry(84, 72, null), Is.False, "an octave off is the detector's");
            Assert.That(LiveJudge.IsWrongTry(70, 72, new[] { 70, 74 }), Is.False, "the previous note");
            Assert.That(LiveJudge.IsWrongTry(82, 72, new[] { 70 }), Is.False, "the previous note an octave off");
            Assert.That(LiveJudge.IsWrongTry(0, 72, null), Is.False, "nothing played");
        }

        [Test]
        public void EachPlayedNoteAnswersOneScoreNote()
        {
            var judge = new LiveJudge();
            judge.Heard(1, 1000);
            judge.Pitch(1, 72, 0, final: true);

            Assert.That(judge.Judge(1000, 72, false, 1100)!.Value.Result, Is.EqualTo(LiveResult.Perfect));
            Assert.That(judge.Judge(1120, 72, false, 1630)!.Value.Reason, Is.EqualTo(LiveReason.NotHeard), "a repeated note needs its own attack");
        }

        [Test]
        public void UnclearPitchAndUnknownScorePitch()
        {
            var judge = new LiveJudge();
            judge.Heard(1, 1000);
            judge.Unresolved(1, final: true);

            var unclear = judge.Judge(1000, 72, false, 1510)!.Value;
            Assert.That(unclear.Reason, Is.EqualTo(LiveReason.PitchUnclear));
            Assert.That(unclear.Result, Is.EqualTo(LiveResult.Meh), "on time, but the pitch could not be told: at most meh");

            // The score does not know this note's pitch: any note on time counts.
            judge.Heard(2, 2000);
            judge.Pitch(2, 60, 0, final: true);
            Assert.That(judge.Judge(2000, 72, true, 2010)!.Value.Result, Is.EqualTo(LiveResult.Perfect));
        }

        [Test]
        public void HasHeardTellsTheWaitModeANoteWasPlayed()
        {
            var judge = new LiveJudge();
            Assert.That(judge.HasHeard(1000, 72, false), Is.False);

            judge.Heard(1, 990);
            judge.Pitch(1, 70, 0, final: true);
            Assert.That(judge.HasHeard(1000, 72, false), Is.False, "another note (a whole tone off)");

            judge.Heard(2, 1010);
            judge.Pitch(2, 72, 0, final: false);
            Assert.That(judge.HasHeard(1000, 72, false), Is.True);
            Assert.That(judge.HasHeard(1500, 72, false), Is.False, "too far");
        }

        [Test]
        public void EasyForgivesIntonation()
        {
            // C5 played 70 cents sharp: heard as C♯5 (72 + 70 cents rounds to 73, -30).
            LiveVerdict judge(double tolerance)
            {
                var j = new LiveJudge { PitchToleranceCents = tolerance };
                j.Heard(1, 1000);
                j.Pitch(1, 73, -30, final: true);
                return j.Judge(1000, 72, false, 1400)!.Value;
            }

            Assert.That(judge(50).Result, Is.EqualTo(LiveResult.Meh), "normal: C5, 70 cents out (past the good's 50), the streak kept");
            Assert.That(judge(75).Result, Is.EqualTo(LiveResult.Good), "easy: the budgets widen (good up to 75 cents)");

            var easy = new LiveJudge { PitchToleranceCents = 75 };
            Assert.That(easy.Matches(72.74f, 72), Is.True);
            Assert.That(easy.Matches(73.0f, 72), Is.False, "a whole semitone is another note, even on easy");
        }

        [Test]
        public void AShortSlurredNoteSounded()
        {
            var judge = new LiveJudge();

            // E5 (76), then F5 (77) for 120 ms from 1000 ms without an attack, then silence.
            for (double t = 800; t < 1000; t += 10) judge.Frame(t, 76.05f);
            for (double t = 1000; t < 1120; t += 10) judge.Frame(t, 77.1f);

            Assert.That(judge.PitchSounded(1000, 77, 1200), Is.True);
            Assert.That(judge.PitchSounded(1000, 79, 1200), Is.False, "another note");
            Assert.That(judge.PitchSounded(1000, 77, 1020), Is.False, "too little of it yet");
            Assert.That(judge.PitchSounded(1400, 77, 1600), Is.False, "too late for this note");
        }

        [Test]
        public void ALegatoNoteCountsWithoutAnAttack()
        {
            var judge = new LiveJudge();

            // A5 held, then slurred up to B5 at 1020 ms: the readings change, no new onset.
            for (double t = 800; t < 1300; t += 10)
                judge.Frame(t, t < 1020 ? 81.1f : 83.05f);

            Assert.That(judge.Judge(1000, 83, false, 1100, legato: true), Is.Null, "not enough of the slur heard yet");

            var slur = judge.Judge(1000, 83, false, 1160, legato: true)!.Value;
            Assert.That(slur.Result, Is.EqualTo(LiveResult.Perfect));
            Assert.That(slur.OffsetMs, Is.EqualTo(20).Within(0.01));

            // A note that needs an attack: the pitch arriving at its start counts too
            // (an attack too soft to mark an onset)…
            Assert.That(judge.Judge(1000, 83, false, 1210)!.Value.Result, Is.EqualTo(LiveResult.Perfect));

            // …but a note held over from before does not: a repeated note needs its own attack.
            Assert.That(judge.Judge(1200, 83, false, 1710)!.Value.Reason, Is.EqualTo(LiveReason.NotHeard));
            Assert.That(judge.Judge(1200, 83, false, 1410, legato: true)!.Value.Result, Is.EqualTo(LiveResult.Perfect), "tied: no attack expected");

            // Nor a legato note whose pitch never came.
            Assert.That(judge.Judge(1000, 86, false, 1510, legato: true)!.Value.Result, Is.EqualTo(LiveResult.Miss));
        }
    }
}
