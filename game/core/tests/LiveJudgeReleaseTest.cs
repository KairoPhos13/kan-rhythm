// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using NUnit.Framework;
using Kan.Core.Play;

namespace Kan.Core.Tests
{
    /// <summary>
    /// A replay rewind takes a verdict back (2026-11-12 audit B10): the
    /// heard note it took must go back on offer, or the note re-judges as
    /// "not heard" while its own attack is still claimed. Before the fix
    /// this file does not compile: LiveJudge had no way to give a claim
    /// back, and that missing way is the bug.
    /// </summary>
    [TestFixture]
    public class LiveJudgeReleaseTest
    {
        [Test]
        public void ARevertedVerdictPutsItsPlayedNoteBackOnOffer()
        {
            var judge = new LiveJudge();
            judge.Heard(1, 1010);
            judge.Pitch(1, 72, -5, final: true);

            var verdict = judge.Judge(1000, 72, false, 1510)!.Value;
            Assert.That(verdict.Result, Is.EqualTo(LiveResult.Perfect));
            Assert.That(verdict.PlayedId, Is.EqualTo(1));

            // The game takes the verdict back (a replay rewound past the note).
            judge.Unclaim(verdict.PlayedId!.Value);

            var again = judge.Judge(1000, 72, false, 1510)!.Value;
            Assert.That(again.Result, Is.EqualTo(LiveResult.Perfect), "the note re-judges on the same evidence");
            Assert.That(again.PlayedId, Is.EqualTo(1));
        }

        [Test]
        public void GivingBackAnUnknownNoteIsHarmless()
        {
            var judge = new LiveJudge();
            judge.Heard(1, 1010);
            judge.Pitch(1, 72, 0, final: true);

            Assert.DoesNotThrow(() => judge.Unclaim(42));
            Assert.That(judge.Judge(1000, 72, false, 1510)!.Value.Result, Is.EqualTo(LiveResult.Perfect));
        }
    }
}
