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
    /// games. Combines with Wait for me, the speed mods and Autoplay: what
    /// they set is where practice starts (owner 2026-10-17: "it says wait
    /// for me is off but it is actually waiting for me"), and W / S then
    /// switch it while practising.
    ///
    /// The level is cut to the notes of the chosen sections; a moment after
    /// the last of them the play restarts by itself — before it would
    /// complete, so practice never fills the local scores.
    /// </summary>
    public partial class KanModPractice : Mod, IApplicableToBeatmap, IApplicableToHUD, IApplicableFailOverride,
                                          IApplicableToDrawableRuleset<KanHitObject>, IApplicableToTrack, IApplicableToRate
    {
        /// <summary>
        /// Switched on and off while practising (owner 2026-10-16), apart from
        /// the selected mods (which cannot change during play): wait for the
        /// player, a slower speed, or listening to the part played for them.
        /// Kept until the player leaves practice.
        /// </summary>
        public record PracticeOptions(bool Wait, double Speed, bool Listen);

        // (declared first: static fields initialise in order)
        private static readonly PracticeOptions defaults = new PracticeOptions(false, 1, false);

        private static PracticeOptions? options;

        public static PracticeOptions Options => options ?? defaults;

        /// <summary>The selected mods' speed (Half Time 0.75, Double Time 1.5…); practice's own speed makes up the rest.</summary>
        private double modSpeed = 1;

        /// <summary>Wait for me is selected: its listening overlay is already there.</summary>
        private bool waitModSelected;

        /// <summary>Autoplay (or Cinema) is selected: the part is played for the player, and L cannot change that.</summary>
        public bool AutoplaySelected { get; private set; }

        /// <summary>
        /// This play's options, taken once (<see cref="Begin"/>): the
        /// ruleset, the HUD and the track all go by the same ones, whatever
        /// happens to <see cref="Options"/> meanwhile (owner 2026-10-17: after
        /// osu!'s quick retry the play waited but its HUD did not listen).
        /// </summary>
        public PracticeOptions Play { get; private set; } = defaults;

        /// <summary>
        /// A practice play is being made with these mods (the drawable
        /// ruleset's constructor, before the HUD and the track are set up).
        /// The first play of a practice starts from what the mods set.
        /// </summary>
        public PracticeOptions Begin(IReadOnlyList<Mod> mods)
        {
            waitModSelected = mods.OfType<KanModWait>().Any();
            AutoplaySelected = mods.OfType<ModAutoplay>().Any();
            modSpeed = mods.Where(m => m is not KanModPractice).OfType<IApplicableToRate>().Aggregate(1.0, (r, m) => m.ApplyToRate(0, r));

            options ??= new PracticeOptions(waitModSelected, modSpeed, AutoplaySelected);
            Play = options;
            speed.Value = ownSpeed;
            return Play;
        }

        /// <summary>Practice's own tempo: what turns the mods' speed into the practice speed.</summary>
        private double ownSpeed => modSpeed > 0 ? Play.Speed / modSpeed : 1;

        /// <summary>The speeds S steps through.</summary>
        private static readonly double[] speeds = { 1, 0.9, 0.75, 0.6, 0.5 };

        private readonly BindableDouble speed = new BindableDouble(1);
        private Kan.Ruleset.UI.KanDrawableRuleset? ruleset;

        public void ApplyToDrawableRuleset(osu.Game.Rulesets.UI.DrawableRuleset<KanHitObject> drawableRuleset)
        {
            ruleset = drawableRuleset as Kan.Ruleset.UI.KanDrawableRuleset;
            speed.Value = ownSpeed;
        }

        // The practice speed keeps the pitch (tempo), like Half Time.
        public void ApplyToTrack(osu.Framework.Audio.IAdjustableAudioComponent track)
        {
            speed.Value = ownSpeed;
            track.AddAdjustment(osu.Framework.Audio.AdjustableProperty.Tempo, speed);
        }

        public double ApplyToRate(double time, double rate = 1) => rate * ownSpeed;

        // Hit sounds keep their pitch too.
        public void ApplyToSample(osu.Framework.Audio.IAdjustableAudioComponent sample) => sample.AddAdjustment(osu.Framework.Audio.AdjustableProperty.Tempo, speed);

        public override string Name => @"Practice";
        public override string Acronym => @"PR";
        public override LocalisableString Description => @"Loop a part of the level, without score. While practising: arrows move, W waits for you, S slows down, L listens.";
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

        private double rangeStart, rangeEnd, lastNoteEnd, firstNote;

        /// <summary>A part starts this long before its first note.</summary>
        private const double lead_in_ms = 2000;
        private string rangeName = string.Empty;

        /// <summary>The level's sections and the practised range (1-based), for the on-screen strip.</summary>
        private IReadOnlyList<DesignSection> sections = Array.Empty<DesignSection>();

        /// <summary>The sections with notes (1-based): the only ones to practise (owner 2026-10-16: leave the rest out).</summary>
        private List<int> playable = new List<int>();

        /// <summary>What each playable section is called, numbered without gaps (<see cref="DesignParts"/>).</summary>
        private Dictionary<int, string> partNames = new Dictionary<int, string>();

        private int chosenFrom, chosenTo;
        private Guid setId;

        /// <summary>
        /// A range picked while practising (keys or the strip). It outlives the
        /// restart — a restart plays with fresh copies of the mods, and the
        /// selected mods cannot change during play — and is dropped when the
        /// player leaves practice.
        /// </summary>
        private static (Guid set, int from, int to)? picked;

        public bool PerformFail() => false;

        public bool RestartOnFail => false;

        /// <summary>Whether a part was cut out of the level (otherwise the whole level plays, and never loops).</summary>
        private bool prepared;

        public void ApplyToBeatmap(IBeatmap beatmap)
        {
            // Whatever goes wrong here, the level still loads — whole, without looping
            // (2026-10-16: a failure here restarted the play at once, and the restart
            // failed too, back to song select).
            try
            {
                cut(beatmap);
            }
            catch (Exception e)
            {
                prepared = false;
                osu.Framework.Logging.Logger.Error(e, @"kan!: practice could not cut the level; playing it whole");
            }
        }

        private void cut(IBeatmap beatmap)
        {
            prepared = false;

            var notes = beatmap.HitObjects.OfType<KanHitObject>().Where(h => h.HasScore).ToList();
            var design = DesignOf(beatmap.BeatmapInfo.BeatmapSet as BeatmapSetInfo, notes);

            if (design.Sections.Count == 0)
            {
                osu.Framework.Logging.Logger.Log($"kan!: practice found no sections (level known: {beatmap.BeatmapInfo.BeatmapSet != null}); playing it whole");
                return;
            }

            setId = (beatmap.BeatmapInfo.BeatmapSet as BeatmapSetInfo)?.ID ?? Guid.Empty;
            sections = design.Sections;

            int from = FromSection.Value, to = ToSection.Value;
            if (picked is { } p && p.set == setId) (from, to) = (p.from, p.to);

            // Only sections with notes can be practised: a range starting or ending on
            // one without (an intro, a break, the outro) snaps to the nearest that has
            // some (2026-10-16: section 1, the intro, crashed; the outro looped forever).
            (playable, partNames) = DesignParts.Of(design.Sections, notes.Select(n => n.StartTime));
            if (playable.Count == 0)
            {
                osu.Framework.Logging.Logger.Log(@"kan!: practice found no section with notes; playing it whole");
                return;
            }

            from = nearestPlayable(from);
            to = Math.Max(from, nearestPlayable(to));

            (chosenFrom, chosenTo) = (from, to);
            rangeStart = design.Sections[from - 1].StartMs;
            rangeEnd = design.Sections[to - 1].EndMs;
            rangeName = from == to ? partNames[from] : $"{partNames[from]} – {partNames[to]}";

            // Only the part's notes, numbered from 0 again (the play state indexes by them).
            // (a few ms of slack: a note on a section's boundary belongs to it)
            var part = notes.Where(n => n.StartTime >= rangeStart - 5 && n.StartTime < rangeEnd - 5).ToList();
            for (int i = 0; i < part.Count; i++)
                part[i].Index = i;

            lastNoteEnd = part.Count > 0 ? part.Max(n => n.EndMs) : rangeEnd;
            firstNote = part.Count > 0 ? part.Min(n => n.StartTime) : rangeStart;

            // A placeholder after the loop point keeps the play from completing
            // (and saving a score) before it restarts.
            // Mods reshape the beatmap after its objects got their defaults (timing
            // windows): the placeholder needs them too, or the HUD's hit error meter fails.
            var placeholder = new KanHitObject { StartTime = lastNoteEnd + loop_after_ms + 3000 };
            placeholder.ApplyDefaults(beatmap.ControlPointInfo, beatmap.Difficulty);

            part.Add(placeholder);
            ((Beatmap<KanHitObject>)beatmap).HitObjects = part;

            prepared = true;
            osu.Framework.Logging.Logger.Log($"kan!: practice sections {from}–{to} ({rangeName}), {part.Count - 1} notes, {firstNote / 1000:0.0}–{lastNoteEnd / 1000:0.0} s");
        }

        /// <summary>The playable section nearest to <paramref name="number"/> (the next one first).</summary>
        private int nearestPlayable(int number)
        {
            // (2026-10-16: computing the fallback eagerly threw for section 1 — the default)
            foreach (int p in playable)
            {
                if (p >= number) return p;
            }

            return playable[^1];
        }

        /// <summary>The level's design (the same one the play uses).</summary>
        internal static KanDesign DesignOf(BeatmapSetInfo? set, IReadOnlyCollection<KanHitObject> notes)
        {
            var scoreNotes = KanLevelFiles.LoadScore(set)?.Notes ?? new List<Kan.Forge.Score.ScoreNote>();
            return KanLevelFiles.DesignFor(set, scoreNotes, 0);
        }

        public void ApplyToHUD(HUDOverlay overlay)
        {
            overlay.Add(new PracticeLoop(this));

            // Waiting while practising works as the Wait for me mod does (listening
            // from the HUD). With that mod selected its overlay is already there
            // (and stays quiet while practice does not wait).
            if (Play.Wait && !waitModSelected)
            {
                var live = new Kan.Ruleset.UI.KanWaitOverlay(() => ruleset);
                overlay.Add(live);
                overlay.Add(new Kan.Forge.Play.WaitHold(() => ruleset?.State is { Waiting: true } st ? st.WaitResumeTime : null, () => live.UserPaused));
            }
        }

        /// <summary>Developer tooling: what ← → (with Shift) do while practising.</summary>
        internal static void DevStep(bool forward, bool resize) => PracticeLoop.Current?.Step(forward, resize);

        /// <summary>Developer tooling: what W / S / L do while practising.</summary>
        internal static void DevToggle(char option) => PracticeLoop.Current?.Toggle(option);

        /// <summary>
        /// While practising: restarts the play a moment after the part's last
        /// note, and lets the player move around (owner 2026-10-16: "how do I
        /// go to the next loop, back, choose?") — ← → (or [ ]) the previous or
        /// next section, Shift with them to shorten or lengthen the part, or a
        /// click on the strip of sections at the top (Shift+click: up to there).
        /// </summary>
        private partial class PracticeLoop : CompositeDrawable
        {
            private readonly KanModPractice mod;
            private bool restarting;

            [Resolved]
            private osu.Game.Screens.Play.IGameplayClock clock { get; set; } = null!;

            /// <summary>The practice being played (developer tooling drives it like the keys do).</summary>
            internal static PracticeLoop? Current;

            public PracticeLoop(KanModPractice mod)
            {
                this.mod = mod;
                Current = this;
                RelativeSizeAxes = Axes.Both;
                AlwaysPresent = true;

                var strip = new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    Height = 26,
                    Direction = FillDirection.Horizontal,
                };

                // Only the sections with notes, each its share of them.
                double total = mod.playable.Sum(n => mod.sections[n - 1].DurationMs);

                foreach (int number in mod.playable)
                {
                    var section = mod.sections[number - 1];
                    bool inside = number >= mod.chosenFrom && number <= mod.chosenTo;

                    strip.Add(new SectionButton(section, mod.partNames[number], inside, (float)(section.DurationMs / total), shift => pick(number, shift)));
                }

                InternalChild = new FillFlowContainer
                {
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                    RelativeSizeAxes = Axes.X,
                    Width = 0.6f,
                    AutoSizeAxes = Axes.Y,
                    Y = 48,
                    Direction = FillDirection.Vertical,
                    Spacing = new osuTK.Vector2(0, 4),
                    Children = new Drawable[]
                    {
                        new OsuSpriteText
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            Font = OsuFont.GetFont(size: 18, weight: FontWeight.SemiBold),
                            Text = $"Practice · {mod.rangeName} · loop {mod.currentLoop}",
                            Shadow = true,
                        },
                        strip,
                        new OsuSpriteText
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            Font = OsuFont.GetFont(size: 13),
                            Text = @"← → section · Shift+← → shorter / longer · click a section (Shift+click: up to it)",
                            Alpha = 0.7f,
                            Shadow = true,
                        },
                        new FillFlowContainer
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            AutoSizeAxes = Axes.Both,
                            Direction = FillDirection.Horizontal,
                            Spacing = new osuTK.Vector2(8, 0),
                            Children = new Drawable[]
                            {
                                new OptionButton($"Wait for me: {(mod.Play.Wait ? "on" : "off")} (W)", mod.Play.Wait, () => Toggle('w')),
                                new OptionButton($"Speed {mod.Play.Speed:0%} (S)", Math.Abs(mod.Play.Speed - 1) > 0.001, () => Toggle('s')),
                                new OptionButton(mod.AutoplaySelected ? @"Listen: on (Autoplay)" : $"Listen: {(mod.Play.Listen ? "on" : "off")} (L)", mod.Play.Listen || mod.AutoplaySelected, () => Toggle('l')),
                            },
                        },
                    },
                };
            }

            private bool skipped;

            protected override void LoadComplete()
            {
                base.LoadComplete();
                player = this.FindClosestParent<Player>();
            }

            protected override void Update()
            {
                base.Update();

                // osu! starts a level from its beginning (with a Skip button): a part
                // later in the song starts at once, a moment before its first note.
                if (!skipped && mod.prepared && clock.IsRunning)
                {
                    skipped = true;
                    double target = mod.firstNote - lead_in_ms;

                    var gcc = this.FindClosestParent<osu.Game.Screens.Play.GameplayClockContainer>();
                    if (target - clock.CurrentTime > lead_in_ms)
                        gcc?.Seek(target);
                }

                if (!mod.prepared || restarting || clock.CurrentTime < mod.lastNoteEnd + loop_after_ms) return;

                mod.countLoop();
                osu.Framework.Logging.Logger.Log($"kan!: practice loop {mod.Loops} done ({mod.rangeName}), again");
                restart();
            }

            protected override bool OnKeyDown(osu.Framework.Input.Events.KeyDownEvent e)
            {
                if (e.Repeat || mod.sections.Count == 0) return false;

                switch (e.Key)
                {
                    case osuTK.Input.Key.W:
                        Toggle('w');
                        return true;

                    case osuTK.Input.Key.S:
                        Toggle('s');
                        return true;

                    case osuTK.Input.Key.L:
                        Toggle('l');
                        return true;
                }

                bool back = e.Key is osuTK.Input.Key.Left or osuTK.Input.Key.BracketLeft;
                bool forward = e.Key is osuTK.Input.Key.Right or osuTK.Input.Key.BracketRight;
                if (!back && !forward) return false;

                Step(forward, e.ShiftPressed);
                return true;
            }

            /// <summary>The next / previous section, or (with <paramref name="resize"/>) a longer / shorter part.</summary>
            public void Step(bool forward, bool resize)
            {
                // Steps go through the sections with notes only.
                var playable = mod.playable;
                if (playable.Count == 0)
            {
                osu.Framework.Logging.Logger.Log(@"kan!: practice found no section with notes; playing it whole");
                return;
            }

                int a = Math.Max(0, playable.IndexOf(mod.chosenFrom)), b = Math.Max(a, playable.IndexOf(mod.chosenTo));

                if (resize)
                    b = Math.Clamp(b + (forward ? 1 : -1), a, playable.Count - 1); // longer / shorter
                else
                {
                    int length = b - a;
                    a = Math.Clamp(a + (forward ? 1 : -1), 0, playable.Count - 1 - length);
                    b = a + length;
                }

                choose(playable[a], playable[b]);
            }

            /// <summary>The next slower speed, back to the fastest after the slowest; the mods' speed is one of the steps.</summary>
            private double nextSpeed(double current)
            {
                var steps = speeds.Append(mod.modSpeed).Distinct().OrderByDescending(v => v).ToArray();
                return steps.FirstOrDefault(v => v < current - 0.001, steps[0]);
            }

            /// <summary>W / S / L: wait for me, the next speed, listen.</summary>
            public void Toggle(char option)
            {
                var o = mod.Play;

                options = option switch
                {
                    'w' => o with { Wait = !o.Wait },
                    's' => o with { Speed = nextSpeed(o.Speed) },
                    'l' when !mod.AutoplaySelected => o with { Listen = !o.Listen },
                    _ => o,
                };

                if (options == o) return;

                osu.Framework.Logging.Logger.Log($"kan!: practice options {Options}");
                restart();
            }

            /// <summary>A section of the strip clicked: practise it, or (with Shift) extend the part up to it.</summary>
            private void pick(int number, bool extend)
            {
                if (extend)
                    choose(Math.Min(mod.chosenFrom, number), Math.Max(mod.chosenTo, number));
                else
                    choose(number, number);
            }

            private void choose(int from, int to)
            {
                if (from == mod.chosenFrom && to == mod.chosenTo) return;

                picked = (mod.setId, from, to);
                osu.Framework.Logging.Logger.Log($"kan!: practice now sections {from}-{to}");
                restart();
            }

            private void restart()
            {
                if (player == null || restarting) return;

                restarting = true;
                player.Restart(quickRestart: true);
            }

            /// <summary>osu!'s own restart (quick retry, the pause menu's retry) is a restart too: it keeps the part and the options.</summary>
            private static readonly System.Reflection.FieldInfo? player_restarting =
                typeof(Player).GetField(@"isRestarting", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

            private Player? player;

            private bool playerRestarting => player != null && player_restarting?.GetValue(player) is true;

            protected override void Dispose(bool isDisposing)
            {
                // Leaving practice (not restarting it): the next practice starts from the mod's settings.
                if (!restarting && !playerRestarting)
                {
                    picked = null;
                    options = null;
                }

                base.Dispose(isDisposing);
            }

            /// <summary>A practice option, lit when on.</summary>
            private partial class OptionButton : osu.Game.Graphics.Containers.OsuClickableContainer
            {
                public OptionButton(string text, bool on, Action toggle)
                {
                    AutoSizeAxes = Axes.Both;
                    Action = toggle;
                    Masking = true;
                    CornerRadius = 4;

                    Children = new Drawable[]
                    {
                        new osu.Framework.Graphics.Shapes.Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Colour = on ? osuTK.Graphics.Color4.White : new osuTK.Graphics.Color4(0.18f, 0.18f, 0.24f, 1),
                            Alpha = 0.85f,
                        },
                        new OsuSpriteText
                        {
                            Margin = new MarginPadding { Horizontal = 8, Vertical = 3 },
                            Font = OsuFont.GetFont(size: 13, weight: FontWeight.SemiBold),
                            Text = text,
                            Colour = on ? osuTK.Graphics.Color4.Black : osuTK.Graphics.Color4.White,
                        },
                    };
                }
            }

            /// <summary>One section on the strip: its share of the level, its name, highlighted when practised.</summary>
            private partial class SectionButton : osu.Game.Graphics.Containers.OsuClickableContainer
            {
                private readonly Action<bool> picked;

                public SectionButton(DesignSection section, string name, bool inside, float share, Action<bool> picked)
                {
                    this.picked = picked;
                    RelativeSizeAxes = Axes.Both;
                    Width = share;
                    Padding = new MarginPadding { Horizontal = 1 };
                    TooltipText = name;

                    Child = new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Masking = true,
                        CornerRadius = 4,
                        Children = new Drawable[]
                        {
                            new osu.Framework.Graphics.Shapes.Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Colour = inside ? osuTK.Graphics.Color4.White : section.IsRest ? new osuTK.Graphics.Color4(0.3f, 0.3f, 0.35f, 1) : new osuTK.Graphics.Color4(0.18f, 0.18f, 0.24f, 1),
                                Alpha = inside ? 0.85f : 0.75f,
                            },
                            new osu.Game.Graphics.Sprites.TruncatingSpriteText
                            {
                                RelativeSizeAxes = Axes.X,
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Padding = new MarginPadding { Horizontal = 5 },
                                Font = OsuFont.GetFont(size: 12, weight: FontWeight.Bold),
                                Text = name,
                                Colour = inside ? osuTK.Graphics.Color4.Black : osuTK.Graphics.Color4.White,
                            },
                        },
                    };
                }

                protected override bool OnClick(osu.Framework.Input.Events.ClickEvent e)
                {
                    picked(e.ShiftPressed);
                    return true;
                }
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
                private Dictionary<int, string> names = new Dictionary<int, string>();

                protected override void LoadComplete()
                {
                    base.LoadComplete();
                    beatmap.BindValueChanged(_ => refresh(), true);
                }

                private void refresh()
                {
                    var set = beatmap.Value.BeatmapSetInfo;
                    sections = DesignOf(set, Array.Empty<KanHitObject>()).Sections;

                    // Only the sections with notes, by the names practice shows (no gaps in the numbers).
                    var starts = KanLevelFiles.LoadScore(set)?.Notes.Select(n => n.StartMs) ?? Enumerable.Empty<double>();
                    (var playable, names) = DesignParts.Of(sections, starts);

                    if (playable.Count == 0)
                    {
                        Items = new[] { 1 };
                        return;
                    }

                    Items = playable.ToArray();
                    if (!playable.Contains(Current.Value)) Current.Value = playable.FirstOrDefault(p => p >= Current.Value, playable[^1]);
                }

                protected override LocalisableString GenerateItemText(int item)
                {
                    if (item < 1 || item > sections.Count) return item.ToString();

                    var s = sections[item - 1];
                    var at = TimeSpan.FromMilliseconds(s.StartMs);
                    return $"{names.GetValueOrDefault(item, s.DisplayName)}{(s.Climax ? " ★" : "")} — {(int)at.TotalMinutes}:{at.Seconds:00}";
                }
            }
        }
    }
}
