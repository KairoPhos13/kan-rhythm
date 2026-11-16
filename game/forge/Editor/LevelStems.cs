// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using osu.Game.Beatmaps;
using Kan.Core.Native;
using Kan.Core.Tracks;

namespace Kan.Forge.Editor
{
    /// <summary>
    /// A level keeps the stems of its song (issue 42): the raw separation of
    /// its mix, so the bass level, the guitar level and the rest can later
    /// start from the same song without a new demux (each demux loses
    /// quality). The stems live in the level's files under <c>stems/</c> as
    /// Ogg Vorbis, one file per separated track plus <c>stems/stems.json</c>
    /// saying where they came from. Play never opens them.
    ///
    /// Stems are the raw separation: full song length, no gain, edits,
    /// trims or window. All of a level's stems come from ONE separation and
    /// have the same frame count; anything else is refused with a clear
    /// error, so they line up to the sample.
    /// </summary>
    public static class LevelStems
    {
        /// <summary>The folder inside the level's files that holds the stems.</summary>
        public const string FOLDER = @"stems";

        /// <summary>The level's file naming its stems and where they came from.</summary>
        public const string MANIFEST = FOLDER + @"/stems.json";

        /// <summary>
        /// The stems are source material for other levels, so they travel at
        /// a higher quality than the played files
        /// (<see cref="BeatmapAudio.PLAY_FILE_QUALITY"/>).
        /// </summary>
        public const float STEM_QUALITY = 0.8f;

        /// <summary>Whether a file of a level is one of its stems: anything under <c>stems/</c>.</summary>
        public static bool IsStemFile(string? fileName)
            => !string.IsNullOrEmpty(fileName)
               && fileName.Replace('\\', '/').StartsWith(FOLDER + "/", StringComparison.OrdinalIgnoreCase);

        /// <summary>An attach is running (one at a time: a save can follow a render).</summary>
        public static bool Attaching { get; private set; }

        /// <summary>
        /// Put the set's separated tracks (the demux output) into the level
        /// as <c>stems/&lt;kind&gt;.ogg</c> and <c>stems/stems.json</c>,
        /// replacing any earlier stems. The encoding runs off the update
        /// thread. Returns true when the level carries these stems (attached
        /// now, or already there); a refusal or failure is reported to
        /// <paramref name="problem"/> and never throws.
        /// </summary>
        /// <param name="separated">The set's separated tracks (<see cref="ForgeTrack.IsSeparated"/>).</param>
        /// <param name="workDir">The level's work folder, where the encoded stems are written.</param>
        public static async Task<bool> AttachAsync(IReadOnlyList<ForgeTrack> separated, string workDir,
                                                  WorkingBeatmap working, BeatmapManager beatmaps,
                                                  osu.Game.Screens.Edit.Editor? editor, Action<string>? problem = null)
        {
            if (Attaching) return true; // one is already carrying them

            Attaching = true;

            try
            {
                var sources = separated.Where(t => t.IsSeparated).ToList();
                if (sources.Count == 0) return true; // a level without stems carries none

                if (!sources.All(t => File.Exists(t.Path)))
                {
                    string gone = sources.First(t => !File.Exists(t.Path)).Path;
                    problem?.Invoke($"The stems cannot be attached: \"{Path.GetFileName(gone)}\" is gone from the work folder.");
                    return false;
                }

                // All of a level's stems come from ONE separation.
                string folder = Path.GetDirectoryName(sources[0].Path) ?? string.Empty;
                if (sources.Any(t => !string.Equals(Path.GetDirectoryName(t.Path) ?? string.Empty, folder, StringComparison.OrdinalIgnoreCase))
                    || sources.Select(t => Path.GetFileNameWithoutExtension(t.Path)).Distinct(StringComparer.OrdinalIgnoreCase).Count() != sources.Count)
                {
                    problem?.Invoke("The stems cannot be attached: the set holds stems from more than one separation. Keep one separation in the set.");
                    return false;
                }

                // The same frame count, rate and channels: they line up to the sample.
                var infos = new List<WavInfo>();

                foreach (var t in sources)
                {
                    var info = await Task.Run(() => wavInfo(t.Path, workDir));
                    if (info == null)
                    {
                        problem?.Invoke($"The stems cannot be attached: \"{Path.GetFileName(t.Path)}\" is not an audio file that can be measured.");
                        return false;
                    }

                    infos.Add(info.Value);
                }

                if (infos.Any(i => i != infos[0]))
                {
                    problem?.Invoke("The stems cannot be attached: they do not all have the same length. They must come from one separation of one song.");
                    return false;
                }

                // The same stems again: nothing to do (the work is only done
                // when the sources changed or the level lost its stems).
                var kinds = sources.Select(t => Path.GetFileNameWithoutExtension(t.Path)).OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
                var wanted = kinds.Select(k => FOLDER + "/" + k + BeatmapAudio.PLAY_FILE_EXTENSION).ToList();
                string signature = sourceSignature(sources);

                if (ReadManifest(working) is { } current && current.SourceSignature == signature
                    && wanted.All(name => working.BeatmapSetInfo?.GetFile(name) != null))
                    return true;

                // Encode each stem (off the update thread) into the work folder.
                string outDir = Path.Combine(workDir, @"level-stems");
                Directory.CreateDirectory(outDir);

                var encoded = await Task.Run(() =>
                {
                    var files = new List<(string Kind, string Path)>();

                    foreach (var t in sources)
                    {
                        string kind = Path.GetFileNameWithoutExtension(t.Path);
                        string outPath = Path.Combine(outDir, kind + BeatmapAudio.PLAY_FILE_EXTENSION);
                        var err = new byte[512];

                        try
                        {
                            if (KanForgeNative.EncodeOgg(t.Path, outPath, STEM_QUALITY, err, err.Length) != KanForgeStatus.Ok)
                                return (Files: files, Error: $"\"{kind}\" could not be encoded: {KanForgeNative.ErrMessage(err)}");
                        }
                        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
                        {
                            return (Files: files, Error: "the built-in audio encoder is not available: update kan!");
                        }

                        files.Add((kind, outPath));
                    }

                    return (Files: files, Error: (string?)null);
                });

                if (encoded.Error != null)
                {
                    problem?.Invoke("The stems cannot be attached: " + encoded.Error);
                    return false;
                }

                // The manifest: what the level holds and where it came from.
                var manifest = new Manifest
                {
                    Model = ModelId(),
                    SampleRate = infos[0].SampleRate,
                    Channels = infos[0].Channels,
                    Frames = infos[0].Frames,
                    SourceSignature = signature,
                    Stems = encoded.Files.Select(f => new Stem
                    {
                        File = f.Kind + BeatmapAudio.PLAY_FILE_EXTENSION,
                        Kind = f.Kind,
                        Sha256 = sha256Of(f.Path),
                    }).ToList(),
                };

                // Replace the level's earlier stems, then add these.
                var set = working.BeatmapSetInfo;
                if (set == null)
                {
                    problem?.Invoke("The stems cannot be attached: the level has no files.");
                    return false;
                }

                foreach (var old in set.Files.Where(f => IsStemFile(f.Filename)).ToList())
                    beatmaps.DeleteFile(set, old);

                foreach (var f in encoded.Files)
                {
                    using var stream = File.OpenRead(f.Path);
                    beatmaps.AddFile(set, stream, FOLDER + "/" + f.Kind + BeatmapAudio.PLAY_FILE_EXTENSION);
                }

                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(ToJson(manifest))))
                    beatmaps.AddFile(set, stream, MANIFEST);

                BeatmapAudio.SaveEditor(editor);
                return true;
            }
            catch (Exception e)
            {
                problem?.Invoke($"The stems could not be attached: {e.Message}");
                return false;
            }
            finally
            {
                Attaching = false;
            }
        }

        /// <summary>The separation the demux runs (libkan's RoFormer grades).</summary>
        public static string ModelId()
        {
            string grade = Environment.GetEnvironmentVariable("KAN_ROFORMER_GRADE") ?? @"f16";
            string model = grade == @"q8" ? @"FractalSurfer/BS-RoFormer-SW-6stem" : @"studio409/BSRoformer-SW-6Stem-GGUF";

            if (Environment.GetEnvironmentVariable("KAN_ROFORMER_GGUF") is { Length: > 0 } gguf)
                model = Path.GetFileName(gguf);

            return FormattableString.Invariant($"{model} (grade {grade})");
        }

        /// <summary>Read a level's stems.json, or null when it has none (or it cannot be read).</summary>
        public static Manifest? ReadManifest(IWorkingBeatmap working)
        {
            try
            {
                return LevelFile.Read(working, MANIFEST) is { } json ? JsonSerializer.Deserialize<Manifest>(json) : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>One file of a level's stems and what it is.</summary>
        public class Stem
        {
            [JsonPropertyName("file")]
            public string File { get; set; } = string.Empty;

            [JsonPropertyName("kind")]
            public string Kind { get; set; } = string.Empty;

            [JsonPropertyName("sha256")]
            public string Sha256 { get; set; } = string.Empty;
        }

        /// <summary>What a level's <c>stems/stems.json</c> holds.</summary>
        public class Manifest
        {
            [JsonPropertyName("version")]
            public int Version { get; set; } = 1;

            /// <summary>Where the stems come from: the editor's demux.</summary>
            [JsonPropertyName("source")]
            public string Source { get; set; } = @"demux";

            /// <summary>The separation model and its grade.</summary>
            [JsonPropertyName("model")]
            public string Model { get; set; } = string.Empty;

            [JsonPropertyName("sample_rate")]
            public int SampleRate { get; set; }

            [JsonPropertyName("channels")]
            public int Channels { get; set; }

            /// <summary>The frames of every stem (they all have the same length).</summary>
            [JsonPropertyName("frames")]
            public long Frames { get; set; }

            [JsonPropertyName("stems")]
            public List<Stem> Stems { get; set; } = new List<Stem>();

            /// <summary>The source files (path, length, mtime): unchanged sources are attached once.</summary>
            [JsonPropertyName("source_signature")]
            public string? SourceSignature { get; set; }
        }

        /// <summary>The manifest as the level stores it.</summary>
        public static string ToJson(Manifest manifest)
            => JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });

        // ------------------------------------------------------------------

        /// <summary>What a WAV file's header says about its samples.</summary>
        public readonly struct WavInfo
        {
            public WavInfo(int sampleRate, int channels, long frames)
            {
                SampleRate = sampleRate;
                Channels = channels;
                Frames = frames;
            }

            public int SampleRate { get; }
            public int Channels { get; }
            public long Frames { get; }

            public static bool operator ==(WavInfo a, WavInfo b)
                => a.SampleRate == b.SampleRate && a.Channels == b.Channels && a.Frames == b.Frames;

            public static bool operator !=(WavInfo a, WavInfo b) => !(a == b);

            public override bool Equals(object? obj) => obj is WavInfo other && this == other;

            public override int GetHashCode() => HashCode.Combine(SampleRate, Channels, Frames);
        }

        /// <summary>
        /// The samples a file holds: its WAV header, or (any other format the
        /// bridge reads) a decode of it first. Null when it cannot be measured.
        /// </summary>
        private static WavInfo? wavInfo(string path, string workDir)
        {
            if (readWavInfo(path) is { } info) return info;

            // Not a WAV (the demux writes WAV, but a track can point anywhere):
            // decode it once to measure it.
            string probe = Path.Combine(workDir, @"level-stems", Path.GetFileNameWithoutExtension(path) + "-probe.wav");
            Directory.CreateDirectory(Path.GetDirectoryName(probe)!);
            var err = new byte[512];

            try
            {
                if (KanForgeNative.DecodeToWav(path, probe, err, err.Length) != KanForgeStatus.Ok) return null;
                return readWavInfo(probe);
            }
            catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
            {
                return null;
            }
        }

        /// <summary>Read a WAV file's header: rate, channels and frame count. Null when it is not a WAV.</summary>
        private static WavInfo? readWavInfo(string path)
        {
            try
            {
                using var stream = File.OpenRead(path);
                using var reader = new BinaryReader(stream);

                if (stream.Length < 44) return null;
                if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "RIFF") return null;
                reader.ReadUInt32();
                if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "WAVE") return null;

                int rate = 0, channels = 0, blockAlign = 0;
                long dataBytes = 0;

                while (stream.Position + 8 <= stream.Length)
                {
                    string chunk = Encoding.ASCII.GetString(reader.ReadBytes(4));
                    uint size = reader.ReadUInt32();
                    long next = stream.Position + size + (size & 1);

                    if (chunk == "fmt " && size >= 16)
                    {
                        reader.ReadUInt16(); // the sample format
                        channels = reader.ReadUInt16();
                        rate = (int)reader.ReadUInt32();
                        reader.ReadUInt32(); // the byte rate
                        blockAlign = reader.ReadUInt16();
                    }
                    else if (chunk == "data")
                    {
                        dataBytes = Math.Min(size, stream.Length - stream.Position);
                    }

                    if (next <= stream.Position) break;
                    stream.Position = next;
                }

                if (rate <= 0 || channels <= 0 || blockAlign <= 0) return null;
                return new WavInfo(rate, channels, dataBytes / blockAlign);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or EndOfStreamException)
            {
                return null;
            }
        }

        /// <summary>The source files (path, length, mtime), as one comparable string.</summary>
        private static string sourceSignature(IEnumerable<ForgeTrack> sources)
        {
            string canonical = string.Join(";", sources
                                          .Select(s => (Path: s.Path, Info: new FileInfo(s.Path)))
                                          .OrderBy(s => s.Path, StringComparer.OrdinalIgnoreCase)
                                          .Select(s => FormattableString.Invariant($"{s.Path}|{s.Info.Length}|{s.Info.LastWriteTimeUtc.Ticks}")));

            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        }

        /// <summary>The SHA-256 of a file's bytes (the manifest records what the level holds).</summary>
        private static string sha256Of(string path)
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }
    }
}
