// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kan.Forge.Design
{
    /// <summary>What a stretch of the level is (shown at its start; breaks and climaxes change the screen).</summary>
    public enum SectionKind
    {
        Intro,
        Verse,
        PreChorus,
        Chorus,
        Bridge,
        Solo,

        /// <summary>Nothing to play: the staff steps back (see <see cref="BreakStyle"/>).</summary>
        Break,

        Outro,
    }

    /// <summary>What the screen shows while there is nothing to play (owner 2026-10-15).</summary>
    public enum BreakStyle
    {
        /// <summary>The music as light (the companion's spectrum), then a countdown to the next note.</summary>
        Countdown,

        /// <summary>The music as light only.</summary>
        Visualizer,

        /// <summary>A calm, empty screen.</summary>
        Hide,
    }

    /// <summary>One stretch of the level's timeline.</summary>
    public class DesignSection
    {
        public double StartMs { get; set; }

        public double EndMs { get; set; }

        public SectionKind Kind { get; set; } = SectionKind.Verse;

        /// <summary>The level's high point: hotter colours, bigger rewards.</summary>
        public bool Climax { get; set; }

        /// <summary>For breaks and intros/outros without notes.</summary>
        public BreakStyle BreakStyle { get; set; } = BreakStyle.Countdown;

        /// <summary>A palette for this section only (null = the level's).</summary>
        public string? Palette { get; set; }

        /// <summary>A name shown at its start instead of the kind's ("Second verse"); null = the kind's.</summary>
        public string? Label { get; set; }

        [JsonIgnore]
        public double DurationMs => EndMs - StartMs;

        /// <summary>Whether the section is a rest (nothing to play): a break, or an intro/outro without notes.</summary>
        [JsonIgnore]
        public bool IsRest => Kind == SectionKind.Break;

        public string DisplayName => Label ?? Kind switch
        {
            SectionKind.PreChorus => @"Pre-chorus",
            _ => Kind.ToString(),
        };

        public bool Contains(double timeMs) => timeMs >= StartMs && timeMs < EndMs;

        public DesignSection Clone() => (DesignSection)MemberwiseClone();
    }

    /// <summary>
    /// The level's design (owner 2026-10-15): how the level looks and
    /// rewards, stretch by stretch — sections (breaks, climaxes), the
    /// palette, and how strong the effects are. Stored as a versioned file
    /// inside the level (<see cref="FILE_NAME"/>); a level without one gets
    /// <see cref="AutoDesign"/> from its notes, so it looks right untouched.
    /// </summary>
    public class KanDesign
    {
        public const int VERSION = 1;

        /// <summary>The design's file name inside the level.</summary>
        public const string FILE_NAME = @"kan.design.json";

        public int Version { get; set; } = VERSION;

        /// <summary>The level's palette (<see cref="KanPalette.Presets"/>).</summary>
        public string Palette { get; set; } = KanPalette.DEFAULT;

        /// <summary>Hit effects (sparks, bursts): 0 = off, 1 = normal, 2 = double.</summary>
        public float Sparkle { get; set; } = 1;

        /// <summary>How much playing well brightens the screen: 0 = off, 1 = normal, 2 = double.</summary>
        public float Glow { get; set; } = 1;

        /// <summary>
        /// How much of the level's picture or video shows through behind the
        /// staff: 0 = none (the palette's background), 1 = all of it (owner
        /// 2026-10-15: "I added a picture, it doesn't show"). Playing well and
        /// rests show a little more of it.
        /// </summary>
        public float Picture { get; set; } = 0.6f;

        /// <summary>Show each section's name as it starts.</summary>
        public bool ShowSectionNames { get; set; } = true;

        public List<DesignSection> Sections { get; set; } = new List<DesignSection>();

        /// <summary>Generated, never edited by hand (regenerated when the notes change).</summary>
        public bool Generated { get; set; }

        public DateTime SavedAtUtc { get; set; }

        public DesignSection? SectionAt(double timeMs)
        {
            foreach (var s in Sections)
            {
                if (s.Contains(timeMs)) return s;
            }

            return null;
        }

        /// <summary>The palette in force at <paramref name="timeMs"/> (the section's, else the level's).</summary>
        public KanPalette PaletteAt(double timeMs) => KanPalette.Get(SectionAt(timeMs)?.Palette ?? Palette);

        public KanDesign Clone() => new KanDesign
        {
            Version = Version,
            Palette = Palette,
            Sparkle = Sparkle,
            Glow = Glow,
            Picture = Picture,
            ShowSectionNames = ShowSectionNames,
            Sections = Sections.Select(s => s.Clone()).ToList(),
            Generated = Generated,
            SavedAtUtc = SavedAtUtc,
        };

        public void Sort() => Sections.Sort((a, b) => a.StartMs.CompareTo(b.StartMs));

        // ------------------------------------------------------------------
        // Editing (the design tab), kept here so it is testable without the UI
        // ------------------------------------------------------------------

        /// <summary>Split the section under <paramref name="timeMs"/> in two there. Returns false when there is none, or a part would be too short.</summary>
        public bool SplitAt(double timeMs)
        {
            var s = SectionAt(timeMs);
            if (s == null || timeMs - s.StartMs < MIN_SECTION_MS || s.EndMs - timeMs < MIN_SECTION_MS) return false;

            var second = s.Clone();
            second.StartMs = timeMs;
            second.Label = null;
            s.EndMs = timeMs;
            Sections.Insert(Sections.IndexOf(s) + 1, second);
            Generated = false;
            return true;
        }

        /// <summary>Join a section with the next one (the first keeps its settings).</summary>
        public bool MergeWithNext(DesignSection section)
        {
            int i = Sections.IndexOf(section);
            if (i < 0 || i + 1 >= Sections.Count) return false;

            section.EndMs = Sections[i + 1].EndMs;
            Sections.RemoveAt(i + 1);
            Generated = false;
            return true;
        }

        /// <summary>Move the boundary between a section and the next one, keeping both at least <see cref="MIN_SECTION_MS"/> long.</summary>
        public void MoveBoundary(DesignSection section, double timeMs)
        {
            int i = Sections.IndexOf(section);
            if (i < 0 || i + 1 >= Sections.Count) return;

            var next = Sections[i + 1];
            double t = Math.Clamp(timeMs, section.StartMs + MIN_SECTION_MS, next.EndMs - MIN_SECTION_MS);
            section.EndMs = t;
            next.StartMs = t;
            Generated = false;
        }

        public const double MIN_SECTION_MS = 1000;

        // ------------------------------------------------------------------
        // Files
        // ------------------------------------------------------------------

        private static readonly JsonSerializerOptions options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
        };

        public string ToJson() => JsonSerializer.Serialize(this, options);

        /// <summary>The design's content, without its save time (to compare two copies).</summary>
        public string ContentKey()
        {
            var copy = Clone();
            copy.SavedAtUtc = default;
            return copy.ToJson();
        }

        public string ToJsonStamped()
        {
            SavedAtUtc = DateTime.UtcNow;
            return ToJson();
        }

        public void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, ToJsonStamped());
            File.Move(tmp, path, true);
        }

        /// <summary>Read a design; null when it is missing, unreadable or from a newer version.</summary>
        public static KanDesign? FromJson(string json)
        {
            try
            {
                var design = JsonSerializer.Deserialize<KanDesign>(json, options);
                if (design == null || design.Version > VERSION) return null;

                design.Sections ??= new List<DesignSection>();
                design.Sort();
                return design;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static KanDesign? Load(string path) => File.Exists(path) ? FromJson(File.ReadAllText(path)) : null;
    }
}
