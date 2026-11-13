// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using NUnit.Framework;
using Kan.Core.Score;

namespace Kan.Core.Tests
{
    /// <summary>
    /// The load-time repair report (2026-11-12 audit B4: "drop or clamp and
    /// report"). Before the fix this file does not compile: there was no
    /// report, the bad values simply loaded.
    /// </summary>
    [TestFixture]
    public class KanScoreRepairReportTest
    {
        [Test]
        public void EveryRepairedValueIsCounted()
        {
            const string json = @"{ ""Version"": 1, ""Notes"": [
                { ""StartMs"": 1e400, ""DurationMs"": 250, ""Midi"": 60 },
                { ""StartMs"": -500, ""DurationMs"": -5, ""Midi"": 200 },
                { ""StartMs"": 50, ""DurationMs"": 100, ""Midi"": -3 } ] }";

            var score = KanScore.FromJson(json, out int repaired);

            Assert.That(score, Is.Not.Null);
            Assert.That(score!.Notes, Has.Count.EqualTo(2), "one note dropped, two repaired");
            Assert.That(repaired, Is.EqualTo(5), "the dropped note, and the clamped time, duration and pitches");
        }

        [Test]
        public void ACleanFileReportsNothing()
        {
            var score = new KanScore();
            score.Notes.Add(new ScoreNote { StartMs = 100, DurationMs = 250, Midi = 60 });

            Assert.That(KanScore.FromJson(score.ToJson(), out int repaired), Is.Not.Null);
            Assert.That(repaired, Is.EqualTo(0));
        }
    }
}
