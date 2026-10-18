// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using NUnit.Framework;
using Kan.Forge.Play;

namespace Kan.Forge.Tests
{
    /// <summary>Judging live play against the score (gameplay MVP, owner 2026-10-14).</summary>
    [TestFixture]
    public class LiveJudgeTest
    {
        [Test]
        public void TimingDecidesTheResultOfTheRightNote()
        {
            // osu!mania's steps over kan!'s windows: perfect 35, great 70, good 100, ok 130, meh 200 ms.
            var judge = new LiveJudge();
            double[] offsets = { 30, 60, 95, 120, 180 };
            var expected = new[] { LiveResult.Perfect, LiveResult.Great, LiveResult.Good, LiveResult.Ok, LiveResult.Meh };

            for (int i = 0; i < offsets.Length; i++)
            {
                judge.Heard((uint)i + 1, 1000 * (i + 1) + offsets[i]);
                judge.Pitch((uint)i + 1, 72 + i, i == 0 ? 12 : 0, final: i != 0);
            }

            Assert.That(judge.Judge(1000, 72, false, 1100), Is.EqualTo(new LiveVerdict(LiveResult.Perfect, LiveReason.Played, 30, 72, 12)));

            for (int i = 1; i < offsets.Length; i++)
                Assert.That(judge.Judge(1000 * (i + 1), 72 + i, false, 1000 * (i + 1) + 300)!.Value.Result, Is.EqualTo(expected[i]), $"{offsets[i]} ms");
        }

        [Test]
        public void ANoteWaitsWhileItsPitchIsComing()
        {
            var judge = new LiveJudge();
            judge.Heard(1, 1010);

            Assert.That(judge.Judge(1000, 72, false, 1250), Is.Null, "heard, pitch not known yet: wait");

            judge.Pitch(1, 72, -8, final: true);
            Assert.That(judge.Judge(1000, 72, false, 1260)!.Value.Result, Is.EqualTo(LiveResult.Perfect));
        }

        [Test]
        public void WrongNotesAndSilenceAreMisses()
        {
            var judge = new LiveJudge();
            judge.Heard(1, 1000);
            judge.Pitch(1, 71, 0, final: true);

            // A confirmed wrong try is a miss at once, while the window is still open (owner 2026-10-17).
            var wrong = judge.Judge(1000, 72, false, 1150)!.Value;
            Assert.That(wrong.Result, Is.EqualTo(LiveResult.Miss));
            Assert.That(wrong.Reason, Is.EqualTo(LiveReason.WrongNote));
            Assert.That(wrong.PlayedMidi, Is.EqualTo(71));

            var silent = judge.Judge(3000, 72, false, 3210)!.Value;
            Assert.That(silent.Reason, Is.EqualTo(LiveReason.NotHeard));

            // A provisional wrong pitch may still turn right: wait for it (but not forever).
            judge.Heard(2, 5000);
            judge.Pitch(2, 73, 0, final: false);
            Assert.That(judge.Judge(5000, 72, false, 5300), Is.Null);
            var stillWrong = judge.Judge(5000, 72, false, 5510)!.Value;
            Assert.That(stillWrong.Reason, Is.EqualTo(LiveReason.WrongNote), "out of time: the provisional pitch says what was played");
            Assert.That(stillWrong.PlayedMidi, Is.EqualTo(73));
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

            judge.Heard(2, 1095);
            judge.Pitch(2, 72, 0, final: true);
            var late = judge.Judge(1000, 72, false, 1120, expected: around)!.Value;
            Assert.That(late.Result, Is.EqualTo(LiveResult.Good));
            Assert.That(late.Reason, Is.EqualTo(LiveReason.Played));

            // Only the previous note: still nothing else heard, so not heard (not "wrong note").
            judge.Heard(3, 3000);
            judge.Pitch(3, 70, 0, final: true);
            Assert.That(judge.Judge(3000, 72, false, 3210, expected: around)!.Value.Reason, Is.EqualTo(LiveReason.NotHeard));

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
        public void ASlipPutRightBeforeItIsConfirmedIsNoTry()
        {
            var judge = new LiveJudge();
            judge.Heard(1, 990);
            judge.Pitch(1, 71, 0, final: false); // a slip, still provisional
            judge.Heard(2, 1040);
            judge.Pitch(2, 72, 0, final: true);
            judge.Pitch(1, 71, 0, final: true); // confirmed after the right note came

            Assert.That(judge.Judge(1000, 72, false, 1060)!.Value.Result, Is.EqualTo(LiveResult.Great), "the right note wins");
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
            Assert.That(judge.Judge(1120, 72, false, 1330)!.Value.Reason, Is.EqualTo(LiveReason.NotHeard), "a repeated note needs its own attack");
        }

        [Test]
        public void UnclearPitchAndUnknownScorePitch()
        {
            var judge = new LiveJudge();
            judge.Heard(1, 1000);
            judge.Unresolved(1, final: true);

            var unclear = judge.Judge(1000, 72, false, 1210)!.Value;
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
            judge.Pitch(1, 71, 0, final: true);
            Assert.That(judge.HasHeard(1000, 72, false), Is.False, "another note");

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

            Assert.That(judge(50).Result, Is.EqualTo(LiveResult.Miss), "normal: the next note up");
            Assert.That(judge(75).Result, Is.EqualTo(LiveResult.Perfect), "easy: still C5");

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
            Assert.That(judge.Judge(1200, 83, false, 1410)!.Value.Reason, Is.EqualTo(LiveReason.NotHeard));
            Assert.That(judge.Judge(1200, 83, false, 1410, legato: true)!.Value.Result, Is.EqualTo(LiveResult.Perfect), "tied: no attack expected");

            // Nor a legato note whose pitch never came.
            Assert.That(judge.Judge(1000, 86, false, 1210, legato: true)!.Value.Result, Is.EqualTo(LiveResult.Miss));
        }
    }
}
