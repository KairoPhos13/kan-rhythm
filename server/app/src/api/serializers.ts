/**
 * Response serialisation for the osu!lazer client models.
 *
 * Field names and shapes come from the client's Newtonsoft models listed in
 * server/docs/lazer-protocol.md (§3.3, §4.5, §4.6, §5.3). Two rules matter:
 *  - missing fields keep their C# defaults, so only what the client reads
 *    needs to be present;
 *  - in `SoloScoreInfo`, `beatmapset` must be emitted **after** `beatmap`
 *    (`SoloScoreInfo.cs:111-112`), which is why these builders construct
 *    objects in a fixed key order.
 */

export interface UserRow {
  id: number;
  username: string;
  country_code: string;
  join_date: Date | string;
  last_visit: Date | string | null;
  title: string;
  is_supporter: boolean;
  avatar_url: string;
  cover_url: string;
}

export interface BeatmapsetRow {
  id: number;
  user_id: number | null;
  title: string;
  title_unicode: string;
  artist: string;
  artist_unicode: string;
  creator: string;
  source: string;
  tags: string;
  status: number;
  bpm: number;
  genre_id: number;
  language_id: number;
  nsfw: boolean;
  video: boolean;
  storyboard: boolean;
  play_count: number;
  favourite_count: number;
  submitted_date: Date | string;
  ranked_date: Date | string | null;
  last_updated: Date | string;
  cover_key: string | null;
  archive_key: string;
}

export interface BeatmapRow {
  id: number;
  beatmapset_id: number;
  version: string;
  filename: string;
  checksum: string;
  mode_int: number;
  difficulty_rating: number;
  cs: number;
  ar: number;
  accuracy: number;
  drain: number;
  total_length: number;
  hit_length: number;
  max_combo: number;
  bpm: number;
  count_circles: number;
  count_sliders: number;
  count_spinners: number;
  last_updated: Date | string;
}

export interface ScoreRow {
  id: string | number;
  user_id: number;
  beatmap_id: number;
  ruleset_id: number;
  ruleset_short_name: string;
  total_score: string | number;
  total_score_without_mods: string | number;
  accuracy: number;
  max_combo: number;
  rank: string;
  passed: boolean;
  mods: unknown[];
  statistics: Record<string, number>;
  maximum_statistics: Record<string, number>;
  pauses: number[];
  build_id: number | null;
  started_at: Date | string | null;
  ended_at: Date | string;
}

export function isoDate(value: Date | string | null | undefined): string | null {
  if (value == null) return null;
  return value instanceof Date ? value.toISOString() : new Date(value).toISOString();
}

/**
 * Search genre / language ids (`lazer-protocol.md` §4.1). The UI only reads
 * the `name` of these objects.
 */
const GENRE_NAMES: Record<number, string> = {
  0: 'Any',
  1: 'Unspecified',
  2: 'Video Game',
  3: 'Anime',
  4: 'Rock',
  5: 'Pop',
  6: 'Other',
  7: 'Novelty',
  9: 'Hip Hop',
  10: 'Electronic',
  11: 'Metal',
  12: 'Classical',
  13: 'Folk',
  14: 'Jazz',
};

const LANGUAGE_NAMES: Record<number, string> = {
  0: 'Any',
  1: 'Unspecified',
  2: 'English',
  3: 'Japanese',
  4: 'Chinese',
  5: 'Instrumental',
  6: 'Korean',
  7: 'French',
  8: 'German',
  9: 'Swedish',
  10: 'Spanish',
  11: 'Italian',
  12: 'Russian',
  13: 'Polish',
  14: 'Other',
};

export function genreName(id: number): string {
  return GENRE_NAMES[id] ?? 'Unspecified';
}

export function languageName(id: number): string {
  return LANGUAGE_NAMES[id] ?? 'Unspecified';
}

export interface UserStatisticsSummary {
  playCount: number;
  playTime: number;
  totalScore: number;
  totalHits: number;
  hitAccuracy: number;
  maxCombo: number;
  rankedScore: number;
  grades: { ssh: number; ss: number; sh: number; s: number; a: number };
  bestCount: number;
}

export function emptyStatistics(): UserStatisticsSummary {
  return {
    playCount: 0,
    playTime: 0,
    totalScore: 0,
    totalHits: 0,
    hitAccuracy: 0,
    maxCombo: 0,
    rankedScore: 0,
    grades: { ssh: 0, ss: 0, sh: 0, s: 0, a: 0 },
    bestCount: 0,
  };
}

/** The profile's per-creator beatmapset counts (§3.3, `APIUser`). */
export interface BeatmapsetCounts {
  ranked: number;
  loved: number;
  pending: number;
  graveyard: number;
}

export function noBeatmapsetCounts(): BeatmapsetCounts {
  return { ranked: 0, loved: 0, pending: 0, graveyard: 0 };
}

/** `APIUser` / `APIMe` (lazer-protocol.md §3.3). */
export function toApiUser(
  user: UserRow,
  statistics: UserStatisticsSummary = emptyStatistics(),
  options: { isOnline?: boolean; beatmapCounts?: BeatmapsetCounts } = {},
): Record<string, unknown> {
  const counts = options.beatmapCounts ?? noBeatmapsetCounts();
  return {
    id: user.id,
    username: user.username,
    join_date: isoDate(user.join_date),
    country_code: user.country_code,
    avatar_url: user.avatar_url,
    cover_url: user.cover_url,
    cover: { custom_url: null, url: '', id: null },
    is_admin: false,
    is_supporter: user.is_supporter,
    support_level: user.is_supporter ? 1 : 0,
    is_gmt: false,
    is_qat: false,
    is_bng: false,
    is_bot: false,
    is_active: true,
    is_online: options.isOnline ?? true,
    pm_friends_only: false,
    title: user.title,
    profile_colour: null,
    last_visit: isoDate(user.last_visit) ?? isoDate(user.join_date),
    playmode: 'kan',
    kudosu: { total: 0, available: 0 },
    scores_best_count: statistics.bestCount,
    scores_first_count: 0,
    scores_recent_count: statistics.playCount,
    scores_pinned_count: 0,
    // The beatmapset counts of the profile's beatmap sections (§3.5). kan!
    // tracks what it stores; favourites, guest difficulties and nominations
    // have no data yet and count 0.
    favourite_beatmapset_count: 0,
    ranked_beatmapset_count: counts.ranked,
    loved_beatmapset_count: counts.loved,
    pending_beatmapset_count: counts.pending,
    graveyard_beatmapset_count: counts.graveyard,
    guest_beatmapset_count: 0,
    nominated_beatmapset_count: 0,
    statistics: toApiStatistics(statistics),
    statistics_rulesets: { kan: toApiStatistics(statistics) },
    global_rank: null,
    rank_history: { data: [] },
    badges: [],
    user_achievements: [],
    groups: [],
    monthly_playcounts: [],
    replays_watched_counts: [],
    active_tournament_banners: [],
    team: null,
  };
}

/** `UserStatistics` (profile stats block). */
export function toApiStatistics(statistics: UserStatisticsSummary): Record<string, unknown> {
  return {
    level: { current: 1, progress: 0 },
    pp: null,
    ranked_score: statistics.rankedScore,
    hit_accuracy: statistics.hitAccuracy,
    play_count: statistics.playCount,
    play_time: statistics.playTime,
    total_score: statistics.totalScore,
    total_hits: statistics.totalHits,
    maximum_combo: statistics.maxCombo,
    replays_watched_by_others: 0,
    is_ranked: true,
    grade_counts: {
      ssh: statistics.grades.ssh,
      ss: statistics.grades.ss,
      sh: statistics.grades.sh,
      s: statistics.grades.s,
      a: statistics.grades.a,
    },
    variants: [],
  };
}

/** `APIBeatmap` (lazer-protocol.md §4.6). */
export function toApiBeatmap(
  beatmap: BeatmapRow,
  options: { setId?: number; userId?: number | null; setStatus?: number; setRankedDate?: Date | string | null; setSubmittedDate?: Date | string | null; lastUpdated?: Date | string | null; set?: Record<string, unknown> | null } = {},
): Record<string, unknown> {
  const body: Record<string, unknown> = {
    id: beatmap.id,
    beatmapset_id: beatmap.beatmapset_id,
    status: options.setStatus ?? 1,
    checksum: beatmap.checksum,
    user_id: options.userId ?? 0,
  };
  if (options.set) body['beatmapset'] = options.set;
  body['mode_int'] = Math.min(3, Math.max(0, beatmap.mode_int));
  body['difficulty_rating'] = beatmap.difficulty_rating;
  body['version'] = beatmap.version;
  body['drain'] = beatmap.drain;
  body['cs'] = beatmap.cs;
  body['ar'] = beatmap.ar;
  body['accuracy'] = beatmap.accuracy;
  body['total_length'] = beatmap.total_length;
  body['hit_length'] = beatmap.hit_length;
  body['max_combo'] = beatmap.max_combo;
  body['bpm'] = beatmap.bpm;
  body['count_circles'] = beatmap.count_circles;
  body['count_sliders'] = beatmap.count_sliders;
  body['count_spinners'] = beatmap.count_spinners;
  body['last_updated'] = isoDate(options.lastUpdated ?? beatmap.last_updated);
  body['playcount'] = 0;
  body['current_user_playcount'] = 0;
  body['passcount'] = 0;
  body['convert'] = false;
  body['failtimes'] = { fail: [], exit: [] };
  body['top_tag_ids'] = [];
  body['current_user_tag_ids'] = [];
  body['owners'] = [];
  return body;
}

/** `APIBeatmapSet` (lazer-protocol.md §4.5). */
export function toApiBeatmapSet(
  set: BeatmapsetRow,
  beatmaps: BeatmapRow[],
  owner: UserRow | null,
): Record<string, unknown> {
  return {
    id: set.id,
    status: set.status,
    covers: {
      cover: '',
      'cover@2x': '',
      card: '',
      'card@2x': '',
      list: '',
      'list@2x': '',
    },
    // Preview audio is hardcoded to b.ppy.sh in the client; the URL is ignored
    // (lazer-protocol.md §4.5). Covers/previews are empty in the MVP.
    preview_url: '',
    title: set.title,
    title_unicode: set.title_unicode,
    artist: set.artist,
    artist_unicode: set.artist_unicode,
    creator: set.creator,
    user_id: set.user_id ?? 1,
    user: owner
      ? toApiUser(owner)
      : { id: set.user_id ?? 1, username: set.creator || 'unknown', country_code: 'XX', avatar_url: '', cover_url: '' },
    last_updated: isoDate(set.last_updated),
    ranked_date: isoDate(set.ranked_date),
    submitted_date: isoDate(set.submitted_date),
    bpm: set.bpm,
    play_count: set.play_count,
    favourite_count: set.favourite_count,
    ratings: [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
    nsfw: set.nsfw,
    spotlight: false,
    video: set.video,
    storyboard: set.storyboard,
    availability: { download_disabled: false, more_information: null },
    genre: { id: set.genre_id, name: genreName(set.genre_id) },
    language: { id: set.language_id, name: languageName(set.language_id) },
    source: set.source,
    tags: set.tags,
    has_favourited: false,
    track_id: null,
    beatmaps: beatmaps.map((beatmap) =>
      toApiBeatmap(beatmap, {
        userId: set.user_id,
        setStatus: set.status,
        setRankedDate: set.ranked_date,
        setSubmittedDate: set.submitted_date,
        lastUpdated: set.last_updated,
      }),
    ),
  };
}

/**
 * `SoloScoreInfo` for score rows (lazer-protocol.md §5.3). Key order matters:
 * `beatmap` must precede `beatmapset`.
 */
export function toApiScore(
  score: ScoreRow,
  user: UserRow,
  beatmap: BeatmapRow,
  set: BeatmapsetRow,
  setOwner: UserRow | null,
): Record<string, unknown> {
  return {
    id: Number(score.id),
    user_id: score.user_id,
    beatmap_id: score.beatmap_id,
    ruleset_id: score.ruleset_id,
    build_id: score.build_id,
    passed: score.passed,
    total_score: Number(score.total_score),
    total_score_without_mods: Number(score.total_score_without_mods),
    accuracy: score.accuracy,
    max_combo: score.max_combo,
    rank: String(score.rank).toUpperCase(),
    started_at: isoDate(score.started_at),
    ended_at: isoDate(score.ended_at),
    mods: score.mods ?? [],
    statistics: score.statistics ?? {},
    maximum_statistics: score.maximum_statistics ?? {},
    pauses: score.pauses ?? [],
    user: toApiUser(user),
    beatmap: toApiBeatmap(beatmap, {
      userId: set.user_id,
      setStatus: set.status,
      setRankedDate: set.ranked_date,
      setSubmittedDate: set.submitted_date,
      lastUpdated: set.last_updated,
    }),
    beatmapset: toApiBeatmapSet(set, [beatmap], setOwner),
    pp: null,
    has_replay: false,
    ranked: true,
    preserve: true,
    processed: true,
    legacy_total_score: null,
    legacy_score_id: null,
  };
}

/**
 * `MultiplayerScore` returned from score submission (lazer-protocol.md §5.2).
 * The client reads `id` and `position`; the rest keeps the model deserialisable.
 */
export function toMultiplayerScore(
  score: ScoreRow,
  user: UserRow,
  position: number | null,
): Record<string, unknown> {
  return {
    id: Number(score.id),
    user: toApiUser(user),
    rank: String(score.rank).toUpperCase(),
    total_score: Number(score.total_score),
    accuracy: score.accuracy,
    max_combo: score.max_combo,
    mods: score.mods ?? [],
    statistics: score.statistics ?? {},
    maximum_statistics: score.maximum_statistics ?? {},
    passed: score.passed,
    ended_at: isoDate(score.ended_at),
    position,
    pp: null,
    has_replay: false,
    ranked: true,
    scores_around: null,
    ruleset_id: score.ruleset_id,
    beatmap_id: score.beatmap_id,
  };
}
