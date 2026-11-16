// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.IO.Archives;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Screens.Edit.Submission;
using Kan.Forge.Editor;

namespace Kan.Forge.Tests
{
    /// <summary>
    /// The upload carries the level's stems (issue 42), within the server's
    /// limit: a submission package of a level with stems has them under
    /// stems/, and one over the limit leaves them out and tells the creator
    /// why. The creator's own level keeps its stems either way.
    /// </summary>
    [TestFixture]
    public class LevelStemsSubmissionTest
    {
        [Test]
        public void TestASubmissionPackageOfALevelWithStemsHasThem() => LevelTestBed.Run(bed =>
        {
            var set = levelWithStems(bed);

            string[] names = export(bed, set, fileSelector: null);

            Assert.That(names, Does.Contain("stems/bass.ogg"));
            Assert.That(names, Does.Contain("stems/vocals.ogg"));
            Assert.That(names, Does.Contain(LevelStems.MANIFEST));

            return Task.CompletedTask;
        });

        [Test]
        public void TestAnUploadOverTheLimitLeavesTheStemsOutAndSaysSo() => LevelTestBed.Run(bed =>
        {
            var set = levelWithStems(bed);

            // Under the limit the stems travel (no message for the creator).
            Assert.That(LevelStems.UploadNotice(LevelStems.MAX_UPLOAD_BYTES), Is.Null);
            Assert.That(export(bed, set, fileSelector: null), Does.Contain("stems/bass.ogg"));

            // Over the limit: the stems stay on the computer, in plain words.
            string? notice = LevelStems.UploadNotice(LevelStems.MAX_UPLOAD_BYTES + 1);
            Assert.That(notice, Is.Not.Null);
            Assert.That(notice, Does.Contain("stems").IgnoreCase);
            Assert.That(notice, Does.Contain("stay on your computer"));

            string[] names = export(bed, set, LevelStems.SubmissionFiles);
            Assert.That(names, Has.None.StartsWith("stems/"), "the over limit package still carries the stems");
            Assert.That(names, Does.Contain("My Song - level audio.ogg"), "the rest of the level still travels");
            Assert.That(names, Does.Contain("Test - My Song (tester) [Standard].osu"));

            return Task.CompletedTask;
        });

        // ------------------------------------------------------------------

        private static BeatmapSetInfo levelWithStems(LevelTestBed bed)
            => bed.CreateLevel("My Song", new Dictionary<string, byte[]>
            {
                ["My Song - level audio.ogg"] = "the level audio"u8.ToArray(),
                ["stems/bass.ogg"] = "the bass stem"u8.ToArray(),
                ["stems/vocals.ogg"] = "the vocals stem"u8.ToArray(),
                [LevelStems.MANIFEST] = "{}"u8.ToArray(),
            });

        /// <summary>The file names a submission package built this way carries.</summary>
        private static string[] export(LevelTestBed bed, BeatmapSetInfo set, Func<BeatmapSetInfo, IEnumerable<INamedFileUsage>>? fileSelector)
        {
            var exporter = new SubmissionBeatmapExporter(bed.Storage, new PutBeatmapSetResponse
            {
                BeatmapSetId = 1,
                BeatmapIds = new uint[] { 1 },
            })
            {
                FileSelector = fileSelector,
            };

            using var output = new MemoryStream();
            exporter.ExportToStream(set, output, null);
            output.Position = 0;

            using var reader = new ZipArchiveReader(output);
            return reader.Filenames.ToArray();
        }
    }
}
