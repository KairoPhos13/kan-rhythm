// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Extensions;
using Kan.Core.Tracks;

namespace Kan.Forge.Editor
{
    /// <summary>
    /// Brings the local levels up to the current format (issue 42 step 2):
    /// a WAV level audio and a WAV voice (the renders of kan!forge) become
    /// Ogg Vorbis, and a demuxed level carries its stems. There is no UI
    /// here: the maintenance action calls <see cref="UpgradeAllAsync"/> and
    /// shows the report.
    ///
    /// It never changes the sound (the same samples in, Ogg out) and leaves
    /// a level alone whose audio is not a kan render (an imported mp3 or ogg
    /// stays as it is). A second run changes nothing.
    /// </summary>
    public static class LevelUpgrade
    {
        /// <summary>One level's upgrade: converted, stems attached, skipped (why), failed (why).</summary>
        public class LevelReport
        {
            /// <summary>The level ("Artist - Title").</summary>
            public string Level = string.Empty;

            /// <summary>Some of the played files are Ogg now.</summary>
            public bool Converted;

            /// <summary>The level carries its stems now.</summary>
            public bool StemsAttached;

            /// <summary>What was left alone, and why ("the level audio is already Ogg").</summary>
            public List<string> Skipped = new List<string>();

            /// <summary>What failed, and why.</summary>
            public List<string> Failed = new List<string>();

            /// <summary>The level's outcome, one line per fact.</summary>
            public IEnumerable<string> Lines()
            {
                if (Converted) yield return "converted to Ogg";
                if (StemsAttached) yield return "stems attached";

                foreach (string s in Skipped)
                    yield return "skipped: " + s;

                foreach (string f in Failed)
                    yield return "failed: " + f;
            }
        }

        /// <summary>The whole run, per level, with plain words.</summary>
        public class UpgradeReport
        {
            public List<LevelReport> Levels = new List<LevelReport>();

            public int Converted => Levels.Count(l => l.Converted);
            public int StemsAttached => Levels.Count(l => l.StemsAttached);

            /// <summary>Levels where nothing had to be done.</summary>
            public int Skipped => Levels.Count(l => !l.Converted && !l.StemsAttached && l.Failed.Count == 0);

            public int Failed => Levels.Count(l => l.Failed.Count > 0);

            /// <summary>The run in plain words.</summary>
            public string Summarize()
            {
                var text = new StringBuilder();
                text.Append(FormattableString.Invariant(
                    $"{Levels.Count} levels: {Converted} converted to Ogg, {StemsAttached} with stems attached, {Skipped} already done, {Failed} failed."));

                foreach (var level in Levels)
                {
                    foreach (string line in level.Lines())
                        text.Append('\n').Append(level.Level).Append(": ").Append(line);
                }

                return text.ToString();
            }
        }

        /// <summary>
        /// Upgrade every kan! level of the local player. The work folder of
        /// each level (the game's kan-forge) is where the conversion happens
        /// and where the stems are found.
        /// </summary>
        public static async Task<UpgradeReport> UpgradeAllAsync(BeatmapManager beatmaps, RealmAccess realm,
                                                               string forgeRoot, CancellationToken cancellation = default)
        {
            var report = new UpgradeReport();

            var sets = realm.Run(r => r.All<BeatmapSetInfo>()
                                   .Where(s => !s.DeletePending && !s.Protected)
                                   .AsEnumerable()
                                   .Where(s => s.Beatmaps.Any(b => b.Ruleset.ShortName == @"kan"))
                                   .ToList()
                                   .Detach());

            foreach (var set in sets)
            {
                cancellation.ThrowIfCancellationRequested();
                report.Levels.Add(await upgradeLevelAsync(beatmaps, set, forgeRoot));
            }

            return report;
        }

        // ------------------------------------------------------------------

        private static async Task<LevelReport> upgradeLevelAsync(BeatmapManager beatmaps, BeatmapSetInfo set, string forgeRoot)
        {
            var report = new LevelReport();

            try
            {
                var first = set.Beatmaps.FirstOrDefault(b => b.Ruleset.ShortName == @"kan");
                if (first == null) return report;

                var working = beatmaps.GetWorkingBeatmap(first);
                var level = working.BeatmapSetInfo;

                if (level == null)
                {
                    report.Failed.Add("the level's files could not be read");
                    return report;
                }

                var metadata = level.Beatmaps.First(b => b.Ruleset.ShortName == @"kan").Metadata;
                report.Level = FormattableString.Invariant($"{metadata.Artist} - {metadata.Title}");

                string workDir = Path.Combine(forgeRoot, LevelSession.KeyOf(working));
                Directory.CreateDirectory(workDir);

                // 1. The played files: the WAV renders of kan!forge become
                //    Ogg (issue 42 step 1). The same samples in, Ogg out.
                bool converted = false;

                foreach (var difficulty in level.Beatmaps.Where(b => b.Ruleset.ShortName == @"kan").ToList())
                {
                    string? name = difficulty.Metadata.AudioFile;

                    if (string.IsNullOrEmpty(name))
                    {
                        report.Skipped.Add("the level has no audio file");
                        continue;
                    }

                    if (!BeatmapAudio.IsLevelAudio(name))
                    {
                        report.Skipped.Add($"the level audio \"{name}\" is not a kan render (left alone)");
                        continue;
                    }

                    if (!hasExtension(name, @".wav"))
                    {
                        report.Skipped.Add("the level audio is already Ogg");
                        continue;
                    }

                    var audio = await convertAsync(working, level, name, workDir);

                    if (audio.Error != null)
                    {
                        report.Failed.Add($"the level audio could not be converted: {audio.Error}");
                        continue;
                    }

                    converted |= swapIntoLevel(beatmaps, level, difficulty, name, audio.New!, audio.Ogg!, report);
                }

                foreach (var voice in level.Files.Where(f => BeatmapAudio.IsLevelVoice(f.Filename)).ToList())
                {
                    if (!hasExtension(voice.Filename, @".wav"))
                    {
                        report.Skipped.Add("the voice is already Ogg");
                        continue;
                    }

                    var spoken = await convertAsync(working, level, voice.Filename, workDir);

                    if (spoken.Error != null)
                    {
                        report.Failed.Add($"the voice could not be converted: {spoken.Error}");
                        continue;
                    }

                    using (var stream = File.OpenRead(spoken.Ogg!))
                        beatmaps.AddFile(level, stream, spoken.New!);

                    beatmaps.DeleteFile(level, voice);
                    converted = true;
                }

                report.Converted = converted;

                // The editor must not render the level audio again: it is up
                // to date with the set as the upgrade leaves it.
                if (converted)
                    storeSignature(workDir);

                // 2. The stems of the song, from the level's work folder.
                var doc = ForgeSetFile.Load(Path.Combine(workDir, ForgeSetFile.FILE_NAME));
                var separated = doc == null
                    ? new List<ForgeTrack>()
                    : ForgeSetFile.ToTracks(doc).Select(t => t.Track).ToList();

                if (!separated.Any(t => t.IsSeparated))
                    report.Skipped.Add("no stems in the work folder");
                else if (LevelStems.IsUpToDate(working, separated))
                    report.Skipped.Add("the level already holds its stems");
                else if (await LevelStems.AttachAsync(separated, workDir, working, beatmaps, null, report.Failed.Add))
                    report.StemsAttached = true;
            }
            catch (Exception e)
            {
                report.Failed.Add(e.Message);
            }

            return report;
        }

        /// <summary>
        /// Put the converted file into the level, point the metadata at it and
        /// save the level's osu! file. The old file goes only after the save
        /// (a level that cannot be saved keeps playing).
        /// </summary>
        private static bool swapIntoLevel(BeatmapManager beatmaps, BeatmapSetInfo level,
                                      BeatmapInfo difficulty, string oldName, string newName, string oggPath,
                                      LevelReport report)
        {
            using (var stream = File.OpenRead(oggPath))
                beatmaps.AddFile(level, stream, newName);

            string? previous = difficulty.Metadata.AudioFile;
            difficulty.Metadata.AudioFile = newName;

            try
            {
                var difficultyWorking = beatmaps.GetWorkingBeatmap(difficulty);

                if (difficultyWorking.Beatmap == null)
                    throw new InvalidOperationException("the level's osu! file could not be read");

                beatmaps.Save(difficulty, difficultyWorking.Beatmap, difficultyWorking.Skin, difficultyWorking.Storyboard);
            }
            catch (Exception e)
            {
                difficulty.Metadata.AudioFile = previous;
                report.Failed.Add($"the level could not be saved: {e.Message}");
                return false;
            }

            if (level.GetFile(oldName) is { } old)
                beatmaps.DeleteFile(level, old);

            return true;
        }

        /// <summary>The level's WAV into the work folder, and the same samples as Ogg next to it.</summary>
        private static async Task<(string? New, string? Ogg, string? Error)> convertAsync(WorkingBeatmap working, BeatmapSetInfo level,
                                                                                        string name, string workDir)
        {
            var file = level.GetFile(name);

            if (file == null)
                return (null, null, $"\"{name}\" is missing from the level");

            string wav = Path.Combine(workDir, Path.GetFileNameWithoutExtension(name) + ".wav");

            using (var input = working.GetStream(file.File.GetStoragePath()))
            {
                if (input == null)
                    return (null, null, $"\"{name}\" cannot be read");

                using var output = File.Create(wav);
                input.CopyTo(output);
            }

            try
            {
                string ogg = await BeatmapAudio.ToPlayFileAsync(wav);
                return (Path.ChangeExtension(name, @".ogg"), ogg, null);
            }
            catch (Exception e)
            {
                return (null, null, e.Message);
            }
        }

        /// <summary>Keep the level audio's signature in the set, so the editor does not render it again.</summary>
        private static void storeSignature(string workDir)
        {
            string setFile = Path.Combine(workDir, ForgeSetFile.FILE_NAME);
            var doc = ForgeSetFile.Load(setFile);
            if (doc == null) return;

            var tracks = new ForgeTrackSet();

            foreach (var (track, _) in ForgeSetFile.ToTracks(doc))
                tracks.Add(track);

            tracks.AudioStartMs = doc.AudioStartMs;
            tracks.AudioEndMs = doc.AudioEndMs;
            tracks.LoudnessOffsetDb = doc.LoudnessOffsetDb;
            doc.LevelAudioSignature = BeatmapAudio.Signature(tracks);
            ForgeSetFile.Save(doc, setFile);
        }

        private static bool hasExtension(string fileName, string extension)
            => string.Equals(Path.GetExtension(fileName), extension, StringComparison.OrdinalIgnoreCase);
    }
}
