// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading.Tasks;
using osu.Framework.Audio;
using osu.Framework.Configuration;
using osu.Framework.Extensions;
using osu.Framework.IO.Stores;
using osu.Framework.Platform;
using osu.Framework.Testing;
using osu.Framework.Threading;
using osu.Game;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Extensions;
using osu.Game.Models;
using osu.Game.Rulesets;
using osu.Game.Tests;
using Kan.Ruleset;

namespace Kan.Forge.Tests
{
    /// <summary>
    /// A kan! level in a temporary realm: the realm, the file store and the
    /// BeatmapManager that change the level, on the test's own storage (the
    /// owner's realm and files are never touched). The work runs on the game
    /// thread of a headless host, like the fork's own database tests
    /// (RealmTest): that is where the realm wants its writes to happen.
    /// </summary>
    internal sealed class LevelTestBed : IDisposable
    {
        /// <summary>The temporary storage the levels' files live in.</summary>
        public Storage Storage { get; }

        public RealmAccess Realm { get; }

        public BeatmapManager Beatmaps { get; }

        /// <summary>The root of the levels' work folders (the game's kan-forge).</summary>
        public string ForgeRoot { get; }

        private readonly AudioManager audio;
        private readonly RulesetInfo ruleset;

        /// <summary>Run work against a fresh bed (one headless game each time).</summary>
        public static void Run(Func<LevelTestBed, Task> work, [CallerMemberName] string caller = "")
        {
            using var host = new CleanRunHeadlessGameHost(callingMethodName: caller);
            Exception? failure = null;

            host.Run(new BedGame(async () =>
            {
                try
                {
                    using var bed = new LevelTestBed(host);
                    await work(bed).ConfigureAwait(true);
                }
                catch (Exception e)
                {
                    // Assertion failures included: rethrown to NUnit after the run.
                    failure = e;
                }
            }));

            if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
        }

        private LevelTestBed(GameHost host)
        {
            Storage = new TemporaryNativeStorage("kan-level-tests-" + Guid.NewGuid().ToString("N")[..8]);
            Realm = new RealmAccess(Storage, OsuGameBase.CLIENT_DATABASE_FILENAME, host.UpdateThread);
            audio = new AudioManager(new AudioThread(), new ResourceStore<byte[]>(), new ResourceStore<byte[]>(),
                                     new FrameworkConfigManager(Storage, new Dictionary<FrameworkSetting, object>()));
            Beatmaps = new BeatmapManager(Storage, Realm, null, audio, new ResourceStore<byte[]>(), null,
                                          new FlatWorkingBeatmap(new Beatmap { BeatmapInfo = new BeatmapInfo() }));
            ForgeRoot = Path.Combine(Path.GetTempPath(), "kan-forge-tests", "forge-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(ForgeRoot);

            // The kan! ruleset: a level's .osu says "Mode: 0", which decodes
            // as kan! (its legacy id), and the exporters re-encode it through
            // the ruleset's own converter. Its assembly must be loaded before
            // the first decode asks for it.
            ruleset = new RulesetInfo("kan", "kan!", typeof(KanRuleset).AssemblyQualifiedName!, 0) { Available = true };
            Realm.Write(r => r.Add(ruleset, update: true));
        }

        /// <summary>
        /// A level of the local player: its osu! file (the score's marks) plus
        /// the given files. Returns the set detached, with its files in step
        /// with the realm (what the game's own code passes around).
        /// </summary>
        /// <param name="audioFile">The level's audio file name (default: the level audio's naming).</param>
        public BeatmapSetInfo CreateLevel(string title, IDictionary<string, byte[]> files, string? audioFile = null)
        {
            const string artist = "Test";
            const string author = "tester";
            string osuName = $"{artist} - {title} ({author}) [Standard].osu";

            var metadata = new BeatmapMetadata
            {
                Title = title,
                Artist = artist,
                Author = new RealmUser { Username = author },
                AudioFile = audioFile ?? title + " - level audio.ogg",
            };

            byte[] osuBytes = Encoding.UTF8.GetBytes(minimalOsu(metadata));

            var beatmapInfo = new BeatmapInfo(ruleset, new BeatmapDifficulty(), metadata)
            {
                DifficultyName = "Standard",
            };

            // The game finds a level's osu! file by its content hash
            // (BeatmapInfo.Path reads the name from the set's files).
            using (var stream = new MemoryStream(osuBytes))
                beatmapInfo.Hash = stream.ComputeSHA2Hash();

            var set = new BeatmapSetInfo();
            set.Beatmaps.Add(beatmapInfo);
            beatmapInfo.BeatmapSet = set;

            Realm.Write(r => r.Add(set));

            var detached = set.Detach();
            Beatmaps.AddFile(detached, new MemoryStream(osuBytes), osuName);

            foreach (var (name, bytes) in files)
                Beatmaps.AddFile(detached, new MemoryStream(bytes), name);

            return detached;
        }

        /// <summary>A working beatmap of the level (its files read from the file store).</summary>
        public WorkingBeatmap WorkingOf(BeatmapSetInfo set) => Beatmaps.GetWorkingBeatmap(set.Beatmaps.First());

        /// <summary>The content of one of the level's files, or null when it has none.</summary>
        public byte[]? ReadFile(WorkingBeatmap working, string fileName)
        {
            var file = working.BeatmapSetInfo?.GetFile(fileName);
            if (file == null) return null;

            using var stream = working.GetStream(file.File.GetStoragePath());
            if (stream == null) return null;

            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            return memory.ToArray();
        }

        public void Dispose()
        {
            Realm.Dispose();
            audio.Dispose();
        }

        /// <summary>A level's osu! file: two marks, the score's shape (see LevelMarks).</summary>
        private static string minimalOsu(BeatmapMetadata metadata)
        {
            var text = new StringBuilder();
            text.AppendLine("osu file format v14");
            text.AppendLine();
            text.AppendLine("[General]");
            text.AppendLine("AudioFilename: " + metadata.AudioFile);
            text.AppendLine("AudioLeadIn: 0");
            text.AppendLine("Mode: 0");
            text.AppendLine();
            text.AppendLine("[Metadata]");
            text.AppendLine("Title:" + metadata.Title);
            text.AppendLine("Artist:" + metadata.Artist);
            text.AppendLine("Creator:" + metadata.Author.Username);
            text.AppendLine("Version:Standard");
            text.AppendLine();
            text.AppendLine("[Difficulty]");
            text.AppendLine("HPDrainRate:5");
            text.AppendLine("CircleSize:4");
            text.AppendLine("OverallDifficulty:8");
            text.AppendLine("ApproachRate:5");
            text.AppendLine("SliderMultiplier:1.4");
            text.AppendLine("SliderTickRate:1");
            text.AppendLine();
            text.AppendLine("[TimingPoints]");
            text.AppendLine("0,500,4,2,0,50,1,0");
            text.AppendLine();
            text.AppendLine("[HitObjects]");
            text.AppendLine("256,192,1000,1,0,0:0:0:0:");
            text.AppendLine("256,192,2000,1,0,0:0:0:0:");
            return text.ToString();
        }

        /// <summary>The headless game the bed's work runs inside (one per test).</summary>
        private sealed class BedGame : osu.Framework.Game
        {
            public BedGame(Func<Task> work)
            {
                // ReSharper disable once AsyncVoidLambda
                Scheduler.Add(async () =>
                {
                    await work().ConfigureAwait(true);
                    Exit();
                });
            }
        }
    }
}
