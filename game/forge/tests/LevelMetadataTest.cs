// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Formats;
using osu.Game.IO;
using osu.Game.Models;
using Kan.Core.Score;
using Kan.Ruleset;
using Kan.Ruleset.Difficulty;

namespace Kan.Forge.Tests
{
    /// <summary>
    /// A starter level decoded the way the game imports it. Its osu! file
    /// must carry the score's marks (one per note, one at the level's end), so
    /// the length, the tempo and the star rating a player sees in song select
    /// are real numbers and not the 00:00 / 0.00 of an empty osu!standard
    /// beatmap (docs/architecture/level-metadata.md).
    /// </summary>
    [TestFixture]
    public class LevelMetadataTest
    {
        public static IEnumerable<TestCaseData> Levels
        {
            get
            {
                foreach (string level in LevelDirs())
                    yield return new TestCaseData(level).SetName(Path.GetFileName(Path.GetDirectoryName(level))!);
            }
        }

        /// <summary>Every level under charts/official/&lt;song&gt;/level/.</summary>
        private static IEnumerable<string> LevelDirs()
        {
            string? official = FindOfficial();
            if (official == null)
                yield break;

            foreach (string song in Directory.GetDirectories(official).OrderBy(d => d))
            {
                string level = Path.Combine(song, "level");
                if (Directory.Exists(level))
                    yield return level;
            }
        }

        private static string? FindOfficial()
        {
            for (string? dir = AppContext.BaseDirectory; dir != null; dir = Path.GetDirectoryName(dir))
            {
                string candidate = Path.Combine(dir, "charts", "official");
                if (Directory.Exists(candidate))
                    return candidate;
            }

            return null;
        }

        [TestCaseSource(nameof(Levels))]
        public void TheLevelReadsItsRealLengthTempoAndDifficulty(string levelDir)
        {
            string name = Path.GetFileName(Path.GetDirectoryName(levelDir))!;
            Beatmap beatmap = readLevel(levelDir, out KanScore score, out string scoreJson);

            // The .osu carries the score's marks: one per note, one at the end.
            Assert.That(beatmap.HitObjects, Has.Count.EqualTo(score.Notes.Count + 1),
                $"{name}: the .osu does not carry its score's marks");

            // Length: from the first note's start to the last note's end, the
            // span the level is played over (BeatmapInfo.Length at import).
            double expectedLength = score.Notes.Max(n => n.EndMs) - score.Notes.Min(n => n.StartMs);
            Assert.That(beatmap.CalculatePlayableLength(), Is.EqualTo(expectedLength).Within(1),
                $"{name}: the level's length is not its score's span");
            Assert.That(beatmap.CalculatePlayableLength(), Is.GreaterThan(0), $"{name}: the level reads as 00:00");

            // Tempo: the level's own tempo grid.
            Assert.That(score.Grid, Is.Not.Null.And.Not.Empty, $"{name}: the score has no tempo grid");
            double bpm = 60000 / beatmap.GetMostCommonBeatLength();
            Assert.That(bpm, Is.EqualTo(60000 / score.Grid![0].BeatMs).Within(0.01), $"{name}: the tempo reads wrong");
            Assert.That(bpm, Is.GreaterThan(0), $"{name}: the level reads as 0 BPM");

            // Difficulty: the score's, a calm number for a starter level...
            var difficulty = LevelDifficulty.Of(score.Notes);
            Assert.That(difficulty.Stars, Is.GreaterThan(0), $"{name}: the level reads as 0.00 stars");
            Assert.That(difficulty.Stars, Is.InRange(1.0, 4.0), $"{name}: the starter pack stays a calm one");
            Assert.That(difficulty.NotesPerSecond, Is.GreaterThan(0), $"{name}: no pace measured");
            Assert.That(difficulty.PitchSpanSemitones, Is.GreaterThan(0), $"{name}: no reach measured");

            // ...and the same number through the ruleset's calculator, which
            // is what fills BeatmapInfo.StarRating at import.
            var attributes = new KanDifficultyCalculator(new KanRuleset().RulesetInfo, new LevelWorkingBeatmap(beatmap, scoreJson)).Calculate();
            Assert.That(attributes.StarRating, Is.EqualTo(difficulty.Stars).Within(1e-6), $"{name}: the calculator disagrees");
            Assert.That(attributes.MaxCombo, Is.EqualTo(score.Notes.Count), $"{name}: one combo per note");
        }

        [Test]
        public void TheMarksSurviveTheEditorsRoundTrip()
        {
            // The editor saves a level by encoding what it decoded: the marks
            // must survive that unchanged, or a level's length would drift a
            // little with every save. The marks are plain circles (time only)
            // precisely because of this (game/core/Score/LevelMarks.cs).
            foreach (string levelDir in LevelDirs())
            {
                string name = Path.GetFileName(Path.GetDirectoryName(levelDir))!;
                Beatmap decoded = readLevel(levelDir, out _, out _);

                using var writer = new StringWriter();
                new LegacyBeatmapEncoder(decoded, null, null).Encode(writer);
                Beatmap again = decode(writer.ToString());

                Assert.That(again.HitObjects.Select(h => h.StartTime),
                    Is.EqualTo(decoded.HitObjects.Select(h => h.StartTime)).Within(0.001),
                    $"{name}: the marks moved in the editor's round trip");
            }
        }

        private static Beatmap readLevel(string levelDir, out KanScore score, out string scoreJson)
        {
            string osuPath = Directory.GetFiles(levelDir, "*.osu").Single();
            scoreJson = File.ReadAllText(Path.Combine(levelDir, KanScore.FILE_NAME));
            score = KanScore.FromJson(scoreJson)!;
            Assert.That(score.Notes, Is.Not.Empty, "the level has notes");
            return decode(File.ReadAllText(osuPath));
        }

        private static Beatmap decode(string osuText)
        {
            // A level's .osu says Mode: 0, which the decoder resolves to the
            // kan! ruleset (its legacy id): make sure its assembly is loaded
            // before the first decode asks for it.
            Assert.That(new KanRuleset().LegacyID, Is.EqualTo(0));

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(osuText));
            using var reader = new LineBufferedReader(stream);
            return osu.Game.Beatmaps.Formats.Decoder.GetDecoder<Beatmap>(reader).Decode(reader);
        }

        /// <summary>A working beatmap that carries its score, as a level's files do.</summary>
        private sealed class LevelWorkingBeatmap : FlatWorkingBeatmap
        {
            private readonly string scoreJson;

            public LevelWorkingBeatmap(IBeatmap beatmap, string scoreJson)
                : base(beatmap)
            {
                this.scoreJson = scoreJson;

                var set = new BeatmapSetInfo();
                set.Files.Add(new RealmNamedFileUsage(new RealmFile { Hash = @"score" }, KanScore.FILE_NAME));
                beatmap.BeatmapInfo.BeatmapSet = set;
            }

            public override Stream GetStream(string storagePath)
                => new MemoryStream(Encoding.UTF8.GetBytes(scoreJson));
        }
    }
}
