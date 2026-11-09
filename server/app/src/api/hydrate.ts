/**
 * Hydrates score rows with their user / beatmap / beatmapset data and
 * serialises them in the `SoloScoreInfo` shape (beatmap before beatmapset).
 */
import { BeatmapRow, BeatmapsetRow, ScoreRow, UserRow, toApiScore } from './serializers';
import { DbService } from '../db/db.service';

async function loadUsers(db: DbService, ids: number[]): Promise<Map<number, UserRow>> {
  const map = new Map<number, UserRow>();
  if (ids.length === 0) return map;
  const result = await db.query<UserRow>(
    `SELECT id, username, country_code, join_date, last_visit, title, is_supporter,
            avatar_url, cover_url
       FROM users
      WHERE id = ANY($1::int[])`,
    [ids],
  );
  for (const row of result.rows) map.set(row.id, row);
  return map;
}

async function loadBeatmaps(db: DbService, ids: number[]): Promise<Map<number, BeatmapRow>> {
  const map = new Map<number, BeatmapRow>();
  if (ids.length === 0) return map;
  const result = await db.query<BeatmapRow>(
    `SELECT id, beatmapset_id, version, filename, checksum, mode_int, difficulty_rating,
            cs, ar, accuracy, drain, total_length, hit_length, max_combo, bpm,
            count_circles, count_sliders, count_spinners, last_updated
       FROM beatmaps
      WHERE id = ANY($1::int[])`,
    [ids],
  );
  for (const row of result.rows) map.set(row.id, row);
  return map;
}

async function loadSets(db: DbService, ids: number[]): Promise<Map<number, BeatmapsetRow>> {
  const map = new Map<number, BeatmapsetRow>();
  if (ids.length === 0) return map;
  const result = await db.query<BeatmapsetRow>(
    `SELECT id, user_id, title, title_unicode, artist, artist_unicode, creator, source, tags,
            status, bpm, genre_id, language_id, nsfw, video, storyboard, play_count,
            favourite_count, submitted_date, ranked_date, last_updated, cover_key, archive_key
       FROM beatmapsets
      WHERE id = ANY($1::int[])`,
    [ids],
  );
  for (const row of result.rows) map.set(row.id, row);
  return map;
}

/** Serialises score rows with everything the client dereferences. */
export async function hydrateScores(
  db: DbService,
  scores: ScoreRow[],
): Promise<Record<string, unknown>[]> {
  if (scores.length === 0) return [];

  const users = await loadUsers(db, [...new Set(scores.map((s) => s.user_id))]);
  const beatmaps = await loadBeatmaps(db, [...new Set(scores.map((s) => s.beatmap_id))]);
  const sets = await loadSets(db, [...new Set([...beatmaps.values()].map((b) => b.beatmapset_id))]);
  const ownerIds = [
    ...new Set([...sets.values()].map((s) => s.user_id).filter((id): id is number => id != null)),
  ];
  const owners = await loadUsers(db, ownerIds);

  const out: Record<string, unknown>[] = [];
  for (const score of scores) {
    const user = users.get(score.user_id);
    const beatmap = beatmaps.get(score.beatmap_id);
    const set = beatmap ? sets.get(beatmap.beatmapset_id) : undefined;
    if (!user || !beatmap || !set) continue;
    const owner = set.user_id != null ? owners.get(set.user_id) ?? null : null;
    out.push(toApiScore(score, user, beatmap, set, owner));
  }
  return out;
}
