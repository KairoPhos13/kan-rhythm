// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using NUnit.Framework;
using Kan.Forge.Score;

namespace Kan.Forge.Tests
{
    [TestFixture]
    public class LevelDocumentTest
    {
        [Test]
        public void TheNewerCopyIsOpened()
        {
            var level = new KanScore { SavedAtUtc = new DateTime(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc) };
            var autosave = new KanScore { SavedAtUtc = level.SavedAtUtc.AddMinutes(5) };

            Assert.That(LevelDocument.Newer(level, autosave), Is.SameAs(autosave), "edits since the level was saved");
            Assert.That(LevelDocument.Newer(autosave, level), Is.SameAs(autosave), "the level saved after its autosave");
            Assert.That(LevelDocument.Newer(level, null), Is.SameAs(level));
            Assert.That(LevelDocument.Newer(null, autosave), Is.SameAs(autosave));
            Assert.That(LevelDocument.Newer<KanScore>(null, null), Is.Null);
        }
    }
}
