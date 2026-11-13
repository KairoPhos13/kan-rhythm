// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using Kan.Core.Design;
using Kan.Core.Score;
using NUnit.Framework;

namespace Kan.Core.Tests
{
    /// <summary>
    /// The starter pack's levels, loaded the way the ruleset loads them: the
    /// archive read as an osu! .osz, then the kan! files decoded with
    /// <see cref="KanScore.FromJson" />, <see cref="KanDesign.FromJson" /> and
    /// <see cref="KanCredits.FromJson" /> - exactly what
    /// Kan.Ruleset.Beatmaps.KanLevelFiles reads out of a level's files.
    /// Every level a player gets must pass: notes present and playable, in
    /// the instrument's range, inside the level's own timeline, and with the
    /// audio the .osu names.
    /// </summary>
    [TestFixture]
    public class StarterPackTest
    {
        /// <summary>The 12-hole alto C ocarina: A4 (69) to F6 (89), concert pitch.</summary>
        private const int range_low = 69;
        private const int range_high = 89;

        public static IEnumerable<TestCaseData> Levels
        {
            get
            {
                foreach (string level in LevelFiles())
                    yield return new TestCaseData(level).SetName(Path.GetFileNameWithoutExtension(Path.GetFileName(level)));
            }
        }

        /// <summary>
        /// The case source above yields NO cases when no levels are found,
        /// so a release could ship without its starter pack and the suite
        /// would still be green (audit C7). Finding zero levels is a
        /// failure, said once and loudly.
        /// </summary>
        [Test]
        public void StarterPackLevelsArePresent()
        {
            string? official = FindOfficial();
            Assert.That(official, Is.Not.Null,
                "charts/official was not found above the test binary; the per-level cases would silently be zero");

            int levels = Directory.GetDirectories(official!)
                .Count(song => Directory.Exists(Path.Combine(song, "level")));
            Assert.That(levels, Is.GreaterThan(0),
                "charts/official has no song folder with a level/; the per-level cases would silently be zero");
        }

        /// <summary>
        /// Every level under charts/official/&lt;song&gt;/level/, packed into a
        /// temporary .osz the way tools/level_pack packs it. The generator's
        /// own output in charts/official/dist is preferred when it is there.
        /// </summary>
        private static IEnumerable<string> LevelFiles()
        {
            string? official = FindOfficial();
            if (official == null)
                yield break;

            foreach (string song in Directory.GetDirectories(official).OrderBy(d => d))
            {
                string level = Path.Combine(song, "level");
                if (!Directory.Exists(level))
                    continue;

                string shipped = Path.Combine(official, "dist", Path.GetFileName(song) + ".osz");
                if (File.Exists(shipped))
                {
                    yield return shipped;
                    continue;
                }

                string built = Path.Combine(Path.GetTempPath(), "kan-starter-pack-" + Path.GetFileName(song) + ".osz");
                if (File.Exists(built)) File.Delete(built);
                ZipFile.CreateFromDirectory(level, built);
                yield return built;
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

        private static Dictionary<string, byte[]> ReadArchive(string path)
        {
            var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            using var archive = ZipFile.OpenRead(path);
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                using var stream = entry.Open();
                using var memory = new MemoryStream();
                stream.CopyTo(memory);
                files[entry.FullName] = memory.ToArray();
            }

            return files;
        }

        [TestCaseSource(nameof(Levels))]
        public void LevelLoadsAndIsPlayable(string oszPath)
        {
            Assert.That(File.Exists(oszPath), Is.True, "the level file exists");
            var files = ReadArchive(oszPath);
            string name = Path.GetFileNameWithoutExtension(oszPath);

            // The score, decoded exactly as KanLevelFiles decodes it.
            Assert.That(files.ContainsKey(KanScore.FILE_NAME), Is.True, $"{name}: no {KanScore.FILE_NAME}");
            KanScore? score = KanScore.FromJson(Text(files, KanScore.FILE_NAME));
            Assert.That(score, Is.Not.Null, $"{name}: the score does not decode");
            Assert.That(score!.Notes, Is.Not.Empty, $"{name}: the level has no notes");
            Assert.That(score.Instrument, Is.EqualTo("ocarina"), $"{name}: written for the starter instrument");

            foreach (ScoreNote note in score.Notes)
            {
                Assert.That(note.Midi, Is.InRange(range_low, range_high),
                    $"{name}: {KanScore.NoteName(note.Midi)} is outside the 12-hole alto C ocarina (A4..F6)");
                Assert.That(note.StartMs, Is.GreaterThanOrEqualTo(0), $"{name}: a note starts before the level");
                Assert.That(note.DurationMs, Is.GreaterThanOrEqualTo(KanScore.MIN_DURATION_MS), $"{name}: a note has no length");
                Assert.That(note.PitchUnknown, Is.False, $"{name}: a note whose pitch is only a guess");
            }

            for (int i = 1; i < score.Notes.Count; i++)
                Assert.That(score.Notes[i].StartMs, Is.GreaterThanOrEqualTo(score.Notes[i - 1].StartMs),
                    $"{name}: the notes are out of order");

            // The design: sections that cover the level, and stay calm.
            Assert.That(files.ContainsKey(KanDesign.FILE_NAME), Is.True, $"{name}: no {KanDesign.FILE_NAME}");
            KanDesign? design = KanDesign.FromJson(Text(files, KanDesign.FILE_NAME));
            Assert.That(design, Is.Not.Null, $"{name}: the design does not decode");
            Assert.That(design!.Sections, Is.Not.Empty, $"{name}: no sections");
            Assert.That(design.Generated, Is.False, $"{name}: the shipped design is the one the level keeps");
            Assert.That(design.Sections.All(s => !s.Climax), Is.True, $"{name}: the starter levels stay calm");

            double levelMs = design.Sections.Max(s => s.EndMs);
            Assert.That(design.Sections[0].StartMs, Is.EqualTo(0).Within(1), $"{name}: the sections start at the start");
            for (int i = 1; i < design.Sections.Count; i++)
                Assert.That(design.Sections[i].StartMs, Is.EqualTo(design.Sections[i - 1].EndMs).Within(1),
                    $"{name}: the sections leave a gap in the timeline");

            foreach (ScoreNote note in score.Notes)
                Assert.That(note.EndMs, Is.LessThanOrEqualTo(levelMs + 1),
                    $"{name}: a note runs past the level's audio");

            // Credits travel with the level.
            Assert.That(files.ContainsKey(KanCredits.FILE_NAME), Is.True, $"{name}: no {KanCredits.FILE_NAME}");
            KanCredits? credits = KanCredits.FromJson(Text(files, KanCredits.FILE_NAME));
            Assert.That(credits, Is.Not.Null, $"{name}: the credits do not decode");
            Assert.That(credits!.IsEmpty, Is.False, $"{name}: the level carries no credits");

            // The .osu side: its audio and its tempo grid.
            string? osuName = files.Keys.FirstOrDefault(k => k.EndsWith(".osu", StringComparison.OrdinalIgnoreCase));
            Assert.That(osuName, Is.Not.Null, $"{name}: no .osu file");
            string osu = Text(files, osuName!);
            string? audio = Value(osu, "AudioFilename");
            Assert.That(audio, Is.Not.Null, $"{name}: the .osu names no audio");
            Assert.That(files.ContainsKey(audio!), Is.True, $"{name}: the audio the .osu names is not in the level");
            Assert.That(files.ContainsKey("reference.ogg"), Is.True, $"{name}: no reference render");
            Assert.That(files[audio!].Length, Is.GreaterThan(1000), $"{name}: the audio is empty");

            string? timing = Section(osu, "[TimingPoints]")?
                                .Split("\n").Select(l => l.Trim())
                                .FirstOrDefault(l => l.Length > 0 && char.IsDigit(l[0]) && l.Contains(','));
            Assert.That(timing, Is.Not.Null, $"{name}: no timing point");
            double beatLength = double.Parse(timing!.Split(',')[1], System.Globalization.CultureInfo.InvariantCulture);
            Assert.That(score.Grid, Is.Not.Null.And.Not.Empty, $"{name}: the score has no tempo grid");
            Assert.That(score.Grid![0].BeatMs, Is.EqualTo(beatLength).Within(0.01),
                $"{name}: the score's grid and the .osu's tempo disagree");
        }

        [Test]
        public void EverySongHasCreditsAndSources()
        {
            string? official = FindOfficial();
            Assert.That(official, Is.Not.Null, "charts/official is next to the repository root");
            int songs = 0;

            foreach (string song in Directory.GetDirectories(official!).OrderBy(d => d))
            {
                if (!File.Exists(Path.Combine(song, "song.json")))
                    continue;
                songs++;

                string slug = Path.GetFileName(song);
                Assert.That(File.Exists(Path.Combine(song, "CREDITS.md")), Is.True, $"{slug}: no CREDITS.md");
                using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(song, "song.json")));
                Assert.That(document.RootElement.TryGetProperty("sources", out var sources), Is.True, $"{slug}: no sources");
                Assert.That(sources.GetArrayLength(), Is.GreaterThan(0), $"{slug}: no sources recorded");
                foreach (var source in sources.EnumerateArray())
                    Assert.That(source.TryGetProperty("licence", out var licence) && licence.GetString()!.Length > 0,
                        Is.True, $"{slug}: a source without a licence");
            }

            Assert.That(songs, Is.GreaterThanOrEqualTo(5), "the starter pack ships five levels");
        }

        private static string Text(Dictionary<string, byte[]> files, string name)
            => System.Text.Encoding.UTF8.GetString(files[name]);

        /// <summary>The lines of one .osu section (from its header to the next one).</summary>
        private static string? Section(string osuText, string header)
        {
            int start = osuText.IndexOf(header, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
                return null;

            int end = osuText.IndexOf("\n[", start + header.Length, StringComparison.Ordinal);
            return end < 0 ? osuText[start..] : osuText[start..end];
        }

        private static string? Value(string osuText, string key)
        {
            foreach (string line in osuText.Split('\n'))
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith(key + ":", StringComparison.OrdinalIgnoreCase))
                    return trimmed[(key.Length + 1)..].Trim();
            }

            return null;
        }
    }
}
