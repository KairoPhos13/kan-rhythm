// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Overlays.Settings;
using osu.Game.Rulesets.Mods;
using osu.Game.Screens.Play;
using Kan.Forge.Design;
using Kan.Ruleset.Beatmaps;
using Kan.Ruleset.Objects;

namespace Kan.Ruleset.Mods
{
    /// <summary>
    /// Practice (owner 2026-10-15): play one part of the level — a range of
    /// its design's sections — over and over, without score, then play the
    /// whole song for real. Scored play keeps the genre's rule (earn the hard
    /// part by getting there); practice is apart from it, as in other music
    /// games. Combines with Half Time and Wait for me.
    ///
    /// The level is cut to the notes of the chosen sections; a moment after
    /// the last of them the play restarts by itself — before it would
    /// complete, so practice never fills the local scores.
    /// </summary>
    public partial class KanModPractice : Mod, IApplicableToBeatmap, IApplicableToHUD, IApplicableFailOverride
    {
        public override string Name => @"Practice";
        public override string Acronym => @"PR";
        public override LocalisableString Description => @"Loop a part of the level, without score: pick its sections below.";
        public override ModType Type => ModType.DifficultyReduction;
        public override IconUsage? Icon => FontAwesome.Solid.Redo;
        public override bool ValidForMultiplayer => false;
        public override bool ValidForMultiplayerAsFreeMod => false;
        public override Type[] IncompatibleMods => new[] { typeof(ModFailCondition), typeof(ModNoFail) };

        [SettingSource("From section", "The first section to practise (from the level's design).", SettingControlType = typeof(SectionChoice))]
        public BindableInt FromSection { get; } = new BindableInt(1) { MinValue = 1, MaxValue = 999 };

        [SettingSource("To section", "The last section to practise.", SettingControlType = typeof(SectionChoice))]
        public BindableInt ToSection { get; } = new BindableInt(1) { MinValue = 1, MaxValue = 999 };

        /// <summary>
        /// How many times the part has been played (shown while practising).
        /// A restart plays with fresh copies of the mods, so the count lives
        /// apart, for the same part practised without a long pause.
        /// </summary>
        public int Loops => loops.count;

        private static (string key, int count, double at) loops;

        private string loopKey => $"{rangeStart:0}-{rangeEnd:0}";

        private void countLoop()
        {
            double now = Environment.TickCount64;
            loops = loops.key == loopKey && now - loops.at < 30_000 ? (loopKey, loops.count + 1, now) : (loopKey, 1, now);
        }

        /// <summary>The loop number being played now.</summary>
        private int currentLoop => loops.key == loopKey && Environment.TickCount64 - loops.at < 30_000 ? loops.count + 1 : 1;

        /// <summary>A moment after the part's last note, the next loop starts.</summary>
        private const double loop_after_ms = 1500;

        private double rangeStart, rangeEnd, lastNoteEnd;
        private string rangeName = string.Empty;

        public bool PerformFail() => false;

        public bool RestartOnFail => false;

        public void ApplyToBeatmap(IBeatmap beatmap)
        {
            var notes = beatmap.HitObjects.OfType<KanHitObject>().Where(h => h.HasScore).ToList();
            var design = DesignOf(beatmap.BeatmapInfo.BeatmapSet as BeatmapSetInfo, notes);
            if (design.Sections.Count == 0) return;

            int from = Math.Clamp(FromSection.Value, 1, design.Sections.Count);
            int to = Math.Clamp(Math.Max(ToSection.Value, from), 1, design.Sections.Count);

            rangeStart = design.Sections[from - 1].StartMs;
            rangeEnd = design.Sections[to - 1].EndMs;
            rangeName = from == to ? design.Sections[from - 1].DisplayName : $"{design.Sections[from - 1].DisplayName} – {design.Sections[to - 1].DisplayName}";

            // Only the part's notes, numbered from 0 again (the play state indexes by them).
            // (a few ms of slack: a note on a section's boundary belongs to it)
            var part = notes.Where(n => n.StartTime >= rangeStart - 5 && n.StartTime < rangeEnd - 5).ToList();
            for (int i = 0; i < part.Count; i++)
                part[i].Index = i;

            lastNoteEnd = part.Count > 0 ? part.Max(n => n.EndMs) : rangeEnd;

            // A placeholder after the loop point keeps the play from completing
            // (and saving a score) before it restarts.
            var hitObjects = part.Cast<osu.Game.Rulesets.Objects.HitObject>().ToList();
            hitObjects.Add(new KanHitObject { StartTime = lastNoteEnd + loop_after_ms + 3000 });

            ((Beatmap<KanHitObject>)beatmap).HitObjects = hitObjects.Cast<KanHitObject>().ToList();
        }

        /// <summary>The level's design (the same one the play uses).</summary>
        internal static KanDesign DesignOf(BeatmapSetInfo? set, IReadOnlyCollection<KanHitObject> notes)
        {
            var scoreNotes = KanLevelFiles.LoadScore(set)?.Notes ?? new List<Kan.Forge.Score.ScoreNote>();
            return KanLevelFiles.DesignFor(set, scoreNotes, 0);
        }

        public void ApplyToHUD(HUDOverlay overlay) => overlay.Add(new PracticeLoop(this));

        /// <summary>Restarts the play a moment after the part's last note, and says which loop it is.</summary>
        private partial class PracticeLoop : CompositeDrawable
        {
            private readonly KanModPractice mod;
            private bool restarted;

            [Resolved]
            private osu.Game.Screens.Play.IGameplayClock clock { get; set; } = null!;

            public PracticeLoop(KanModPractice mod)
            {
                this.mod = mod;
                RelativeSizeAxes = Axes.Both;
                AlwaysPresent = true;

                InternalChild = new OsuSpriteText
                {
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                    Y = 60,
                    Font = OsuFont.GetFont(size: 18, weight: FontWeight.SemiBold),
                    Text = $"Practice · {mod.rangeName} · loop {mod.currentLoop}",
                    Alpha = 0.8f,
                    Shadow = true,
                };
            }

            protected override void Update()
            {
                base.Update();

                if (restarted || clock.CurrentTime < mod.lastNoteEnd + loop_after_ms) return;

                var player = this.FindClosestParent<Player>();
                if (player == null) return;

                restarted = true;
                mod.countLoop();
                osu.Framework.Logging.Logger.Log($"kan!: practice loop {mod.Loops} done ({mod.rangeName}), again");
                player.Restart(quickRestart: true);
            }
        }

        /// <summary>A dropdown of the level's sections ("2. Chorus — 0:25").</summary>
        public partial class SectionChoice : SettingsDropdown<int>
        {
            protected override OsuDropdown<int> CreateDropdown() => new SectionDropdown();

            private partial class SectionDropdown : DropdownControl
            {
                [Resolved]
                private IBindable<WorkingBeatmap> beatmap { get; set; } = null!;

                private IReadOnlyList<DesignSection> sections = Array.Empty<DesignSection>();

                protected override void LoadComplete()
                {
                    base.LoadComplete();
                    beatmap.BindValueChanged(_ => refresh(), true);
                }

                private void refresh()
                {
                    var set = beatmap.Value.BeatmapSetInfo;
                    sections = DesignOf(set, Array.Empty<KanHitObject>()).Sections;

                    if (sections.Count == 0)
                    {
                        Items = new[] { 1 };
                        return;
                    }

                    Items = Enumerable.Range(1, sections.Count).ToArray();
                    if (Current.Value > sections.Count) Current.Value = sections.Count;
                }

                protected override LocalisableString GenerateItemText(int item)
                {
                    if (item < 1 || item > sections.Count) return item.ToString();

                    var s = sections[item - 1];
                    var at = TimeSpan.FromMilliseconds(s.StartMs);
                    return $"{item}. {s.DisplayName}{(s.Climax ? " ★" : "")} — {(int)at.TotalMinutes}:{at.Seconds:00}";
                }
            }
        }
    }
}
