// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Formats;
using osu.Game.Database;
using osu.Game.IO;
using osu.Game.IO.Archives;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Screens.Edit.Submission;
using Kan.Core.Score;

namespace Kan.Forge.Tests
{
    /// <summary>
    /// A kan! level with notes exports and submits (issue 42 step 2 found it
    /// broken): the legacy .osu encoder writes a position for every hit
    /// object, and kan!'s notes carry one (KanHitObject, the centre of the
    /// playfield) because kan! claims legacy ruleset id 0. The exported
    /// level keeps its note times.
    /// </summary>
    [TestFixture]
    public class LevelExportTest
    {
        [Test]
        public void TestALevelWithNotesExportsAndKeepsItsNoteTimes() => LevelTestBed.Run(bed =>
        {
            var score = new KanScore
            {
                LevelId = Guid.NewGuid().ToString("N"),
                Notes =
                {
                    new ScoreNote { StartMs = 1000, DurationMs = 250, Midi = 60 },
                    new ScoreNote { StartMs = 2000, DurationMs = 250, Midi = 64 },
                },
            };

            var set = bed.CreateLevel("My Song", new Dictionary<string, byte[]>
            {
                ["My Song - level audio.ogg"] = "the level audio"u8.ToArray(),
                [KanScore.FILE_NAME] = Encoding.UTF8.GetBytes(score.ToJson()),
            });

            double[] expected = { 1000, 2000 };

            // The submission package (the fork's exporter) and the plain
            // .osz export both walk the legacy encoder over the level's
            // notes: neither may throw.
            Assert.That(noteTimes(exportSubmission(bed, set)), Is.EqualTo(expected).Within(0.001),
                "the submission package lost the note times");
            Assert.That(noteTimes(exportPlain(bed, set)), Is.EqualTo(expected).Within(0.001),
                "the plain export lost the note times");

            return Task.CompletedTask;
        });

        /// <summary>
        /// Saving a score writes its marks into the level's .osu through the
        /// same legacy encoder: the marks must be the ruleset's objects, or
        /// the save reports "could not keep its marks".
        /// </summary>
        [Test]
        public void TestTheScoreMarksAreWrittenIntoTheLevel() => LevelTestBed.Run(bed =>
        {
            var previous = Kan.Forge.Editor.LevelOsuMarks.EditorObject;
            Kan.Forge.Editor.LevelOsuMarks.EditorObject = time => new Kan.Ruleset.Objects.KanHitObject { StartTime = time };

            try
            {
                var score = new KanScore
                {
                    LevelId = Guid.NewGuid().ToString("N"),
                    Notes =
                    {
                        new ScoreNote { StartMs = 1000, DurationMs = 250, Midi = 60 },
                        new ScoreNote { StartMs = 2000, DurationMs = 250, Midi = 64 },
                    },
                };

                var set = bed.CreateLevel("Marks", new Dictionary<string, byte[]>
                {
                    ["Marks - level audio.ogg"] = "the level audio"u8.ToArray(),
                });

                string? error = Kan.Forge.Editor.LevelOsuMarks.Sync(bed.WorkingOf(set), bed.Beatmaps, score);
                Assert.That(error, Is.Null, "the marks could not be written");

                // The level's .osu, fresh from the file store, carries the marks.
                var osuFiles = bed.FileNames(set.ID).Where(f => f.EndsWith(".osu", StringComparison.OrdinalIgnoreCase)).ToList();
                Assert.That(osuFiles, Has.Count.EqualTo(1), $"files: {string.Join(", ", bed.FileNames(set.ID))}");

                using var text = new LineBufferedReader(new MemoryStream(bed.ReadFile(set.ID, osuFiles[0])!));
                Beatmap saved = osu.Game.Beatmaps.Formats.Decoder.GetDecoder<Beatmap>(text).Decode(text);
                Assert.That(saved.HitObjects.Select(h => h.StartTime).Take(2), Is.EqualTo(new double[] { 1000, 2000 }).Within(0.001));
            }
            finally
            {
                Kan.Forge.Editor.LevelOsuMarks.EditorObject = previous;
            }

            return Task.CompletedTask;
        });

        // ------------------------------------------------------------------

        /// <summary>The note times of the level's osu! file in the exported package.</summary>
        private static double[] noteTimes(byte[] package)
        {
            using var reader = new ZipArchiveReader(new MemoryStream(package));
            string osuFile = reader.Filenames.Single(f => f.EndsWith(".osu", StringComparison.OrdinalIgnoreCase));

            using var stream = reader.GetStream(osuFile);
            using var text = new LineBufferedReader(stream);
            Beatmap beatmap = osu.Game.Beatmaps.Formats.Decoder.GetDecoder<Beatmap>(text).Decode(text);

            return beatmap.HitObjects.Select(h => h.StartTime).OrderBy(t => t).ToArray();
        }

        private static byte[] exportSubmission(LevelTestBed bed, BeatmapSetInfo set)
        {
            var exporter = new SubmissionBeatmapExporter(bed.Storage, new PutBeatmapSetResponse
            {
                BeatmapSetId = 1,
                BeatmapIds = new uint[] { 1 },
            });

            return export(exporter, set);
        }

        private static byte[] exportPlain(LevelTestBed bed, BeatmapSetInfo set)
            => export(new LegacyBeatmapExporter(bed.Storage), set);

        private static byte[] export(LegacyBeatmapExporter exporter, BeatmapSetInfo set)
        {
            using var output = new MemoryStream();
            exporter.ExportToStream(set, output, null);
            return output.ToArray();
        }
    }
}
