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
            var judge = new LiveJudge();
            judge.Heard(1, 1030);
            judge.Pitch(1, 72, 12, final: false);
            judge.Heard(2, 2100);
            judge.Pitch(2, 74, 0, final: true);
            judge.Heard(3, 3180);
            judge.Pitch(3, 76, 0, final: true);

            Assert.That(judge.Judge(1000, 72, false, 1100), Is.EqualTo(new LiveVerdict(LiveResult.Great, LiveReason.Played, 30, 72, 12)));
            Assert.That(judge.Judge(2000, 74, false, 2200)!.Value.Result, Is.EqualTo(LiveResult.Ok));
            Assert.That(judge.Judge(3000, 76, false, 3200)!.Value.Result, Is.EqualTo(LiveResult.Meh));
        }

        [Test]
        public void ANoteWaitsWhileItsPitchIsComing()
        {
            var judge = new LiveJudge();
            judge.Heard(1, 1010);

            Assert.That(judge.Judge(1000, 72, false, 1250), Is.Null, "heard, pitch not known yet: wait");

            judge.Pitch(1, 72, -8, final: true);
            Assert.That(judge.Judge(1000, 72, false, 1260)!.Value.Result, Is.EqualTo(LiveResult.Great));
        }

        [Test]
        public void WrongNotesAndSilenceAreMisses()
        {
            var judge = new LiveJudge();
            judge.Heard(1, 1000);
            judge.Pitch(1, 71, 0, final: true);

            Assert.That(judge.Judge(1000, 72, false, 1150), Is.Null, "the window is still open");

            var wrong = judge.Judge(1000, 72, false, 1210)!.Value;
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
        public void EachPlayedNoteAnswersOneScoreNote()
        {
            var judge = new LiveJudge();
            judge.Heard(1, 1000);
            judge.Pitch(1, 72, 0, final: true);

            Assert.That(judge.Judge(1000, 72, false, 1100)!.Value.Result, Is.EqualTo(LiveResult.Great));
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
            Assert.That(judge.Judge(2000, 72, true, 2010)!.Value.Result, Is.EqualTo(LiveResult.Great));
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
            Assert.That(judge(75).Result, Is.EqualTo(LiveResult.Great), "easy: still C5");

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
            Assert.That(slur.Result, Is.EqualTo(LiveResult.Great));
            Assert.That(slur.OffsetMs, Is.EqualTo(20).Within(0.01));

            // A note that needs an attack: the pitch arriving at its start counts too
            // (an attack too soft to mark an onset)…
            Assert.That(judge.Judge(1000, 83, false, 1210)!.Value.Result, Is.EqualTo(LiveResult.Great));

            // …but a note held over from before does not: a repeated note needs its own attack.
            Assert.That(judge.Judge(1200, 83, false, 1410)!.Value.Reason, Is.EqualTo(LiveReason.NotHeard));
            Assert.That(judge.Judge(1200, 83, false, 1410, legato: true)!.Value.Result, Is.EqualTo(LiveResult.Great), "tied: no attack expected");

            // Nor a legato note whose pitch never came.
            Assert.That(judge.Judge(1000, 86, false, 1210, legato: true)!.Value.Result, Is.EqualTo(LiveResult.Miss));
        }
    }
}
