// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Models;
using Kan.Core.Design;
using Kan.Core.Score;
using Kan.Forge.Editor;
using Kan.Forge.Editor.Design;
using Kan.Forge.Tracks;

namespace Kan.Forge.Tests
{
    /// <summary>
    /// The key a level's work folder and autosaves go by (2026-11-12 audit
    /// B6): a server update or a re-import of the same .osz gives the level
    /// a new set id, and the work folder (tracks, autosaves) must still be
    /// found. The stable id travels inside the level's kan! files and is
    /// stamped on first open.
    /// </summary>
    [TestFixture]
    public class LevelKeyTest
    {
        [Test]
        public void TheWorkFolderGoesByTheStableIdInsideTheLevel()
        {
            string stable = Guid.NewGuid().ToString("N");
            var withId = level(new Dictionary<string, string>
            {
                [KanScore.FILE_NAME] = $@"{{ ""Version"": 1, ""LevelId"": ""{stable}"" }}",
            });

            Assert.That(LevelSession.KeyOf(withId), Is.EqualTo(stable),
                        "the id travels inside the level: a re-import gives it a new set id, and the work folder must still be found");

            var plain = level(new Dictionary<string, string>());
            Assert.That(LevelSession.KeyOf(plain), Is.EqualTo(plain.BeatmapSetInfo.ID.ToString("N")),
                        "a level with no id yet keeps the key it always had");
        }

        [Test]
        public void TheDesignCarriesTheIdWhenThereIsNoScore()
        {
            string stable = Guid.NewGuid().ToString("N");
            var withDesign = level(new Dictionary<string, string>
            {
                [KanDesign.FILE_NAME] = $@"{{ ""Version"": 2, ""LevelId"": ""{stable}"" }}",
            });

            Assert.That(LevelSession.KeyOf(withDesign), Is.EqualTo(stable));
        }

        [Test]
        public void TheIdIsStampedFromFirstOpen()
        {
            string key = Guid.NewGuid().ToString("N");
            var working = level(new Dictionary<string, string>
            {
                [KanScore.FILE_NAME] = @"{ ""Version"": 1, ""Notes"": [] }",
                [KanDesign.FILE_NAME] = @"{ ""Version"": 2 }",
            });

            openSession(working, key);

            Assert.That(ScoreSession.Score.ToJson(), Does.Contain($@"""LevelId"": ""{key}"""),
                        "stamped on open, the id reaches the level with the next save");
            Assert.That(DesignSession.Design.ToJson(), Does.Contain($@"""LevelId"": ""{key}"""));
        }

        /// <summary>A working beatmap whose set carries the given named files.</summary>
        private static FlatWorkingBeatmap level(Dictionary<string, string> files)
        {
            var set = new BeatmapSetInfo();

            foreach (string name in files.Keys)
                set.Files.Add(new RealmNamedFileUsage(new RealmFile { Hash = name }, name));

            var beatmap = new Beatmap { BeatmapInfo = new BeatmapInfo { BeatmapSet = set } };
            return new LevelWorkingBeatmap(beatmap, files);
        }

        private static void openSession(FlatWorkingBeatmap working, string key)
        {
            string root = Path.Combine(Path.GetTempPath(), "kan-forge-tests", Guid.NewGuid().ToString("N"));
            ForgeTrackSession.Open(root, key);
            ScoreSession.Open(working, key);
            DesignSession.Open(working, key, ScoreSession.Score.Notes, 1000);
        }

        private sealed class LevelWorkingBeatmap : FlatWorkingBeatmap
        {
            private readonly Dictionary<string, string> files;

            public LevelWorkingBeatmap(IBeatmap beatmap, Dictionary<string, string> files)
                : base(beatmap)
            {
                this.files = files;
            }

            public override Stream GetStream(string storagePath)
                => new MemoryStream(System.Text.Encoding.UTF8.GetBytes(files[Path.GetFileName(storagePath)]));
        }
    }
}
