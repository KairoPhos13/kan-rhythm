// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System.Linq;
using NUnit.Framework;
using Kan.Core.Score;
using Kan.Forge.Editor;

namespace Kan.Forge.Tests
{
    [TestFixture]
    public class ScoreSessionTest
    {
        [Test]
        public void AGridFittedToTheMusicKeepsTheNotes()
        {
            string a = KanScore.GridKey(grid((0, 500))), b = KanScore.GridKey(grid((0, 510))), c = KanScore.GridKey(grid((0, 520)));
            Assert.That(ScoreSession.KeepsNotes(a, b), Is.False);

            ScoreSession.GridChangeKeepsNotes(a, b);
            ScoreSession.GridChangeKeepsNotes(b, c);
            Assert.That(ScoreSession.KeepsNotes(c, a), Is.True, "undone in one go, still fitted");
            Assert.That(ScoreSession.KeepsNotes(a, KanScore.GridKey(grid((0, 600)))), Is.False);
        }

        private static GridPoint[] grid(params (double time, double beat)[] points) => points.Select(p => new GridPoint { TimeMs = p.time, BeatMs = p.beat }).ToArray();
    }
}
