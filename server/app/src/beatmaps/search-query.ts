/**
 * Beatmapset search query builder (`lazer-protocol.md` §4.1).
 *
 * Paging uses the `cursor[key]` mechanism the client echoes back; the cursor
 * is opaque to it, so this MVP encodes a plain row offset in `_id`. Kept pure
 * so it can be unit-tested without a database.
 */

export interface SearchFilters {
  /** Free text over title/artist/creator/source/tags. */
  query?: string;
  /** `s` — category (see CATEGORY_SQL). */
  category: string;
  /** `m` — ruleset/mode int; filters difficulties' `mode_int`. */
  mode?: number;
  /** `g` / `l` — genre and language ids. */
  genre?: number;
  language?: number;
  /** `sort` — `{criteria}_{asc|desc}`. */
  sort: string;
  offset: number;
  limit: number;
  /** Set owner for the `mine` category. */
  ownerUserId?: number | null;
}

export const SET_COLUMNS = `id, user_id, title, title_unicode, artist, artist_unicode, creator,
       source, tags, status, bpm, genre_id, language_id, nsfw, video, storyboard,
       play_count, favourite_count, submitted_date, ranked_date, last_updated,
       cover_key, archive_key`;

export const BEATMAP_COLUMNS = `id, beatmapset_id, version, filename, checksum, mode_int,
       difficulty_rating, cs, ar, accuracy, drain, total_length, hit_length,
       max_combo, bpm, count_circles, count_sliders, count_spinners, last_updated`;

export const SEARCH_PAGE_SIZE = 20;

/** `s` parameter values → SQL predicates (lazer-protocol.md §4.1). */
const CATEGORY_SQL: Record<string, string> = {
  any: 'TRUE',
  leaderboard: 'status IN (1, 2, 4)',
  ranked: 'status = 1',
  qualified: 'status = 3',
  loved: 'status = 4',
  pending: 'status = 0',
  wip: 'status = -1',
  graveyard: 'status = -2',
  // No favourites in the MVP; "mine" shows the requesting user's own sets.
  favourites: 'FALSE',
  mine: 'user_id = {owner}',
};

const SORT_EXPRESSIONS: Record<string, string> = {
  title: 'lower(title)',
  artist: 'lower(artist)',
  updated: 'last_updated',
  ranked: 'ranked_date',
  difficulty:
    '(SELECT max(b.difficulty_rating) FROM beatmaps b WHERE b.beatmapset_id = beatmapsets.id)',
  plays: 'play_count',
  favourites: 'favourite_count',
  // Relevance and nominations are not tracked in the MVP: fall back to recency.
  relevance: 'last_updated',
  nominations: 'last_updated',
  rating: 'favourite_count',
};

export function buildSearchQuery(filters: SearchFilters): { text: string; params: unknown[] } {
  const params: unknown[] = [];
  const conditions: string[] = [];

  const category = CATEGORY_SQL[filters.category] ?? CATEGORY_SQL['any'];
  if (category === 'FALSE') {
    conditions.push('FALSE');
  } else if (category.includes('{owner}')) {
    conditions.push(`user_id = $${params.push(filters.ownerUserId ?? -1)}`);
  } else {
    conditions.push(category);
  }

  if (filters.query) {
    const needle = `%${filters.query.replace(/^"(.*)"$/, '$1').replace(/[%_]/g, ' ')}%`;
    const p = `$${params.push(needle)}`;
    conditions.push(
      `(title ILIKE ${p} OR title_unicode ILIKE ${p} OR artist ILIKE ${p}
        OR artist_unicode ILIKE ${p} OR creator ILIKE ${p} OR source ILIKE ${p}
        OR tags ILIKE ${p})`,
    );
  }

  if (filters.mode != null && Number.isFinite(filters.mode)) {
    const p = `$${params.push(filters.mode)}`;
    conditions.push(`EXISTS (SELECT 1 FROM beatmaps b WHERE b.beatmapset_id = beatmapsets.id AND b.mode_int = ${p})`);
  }
  if (filters.genre != null && filters.genre > 0) {
    conditions.push(`genre_id = $${params.push(filters.genre)}`);
  }
  if (filters.language != null && filters.language > 0) {
    conditions.push(`language_id = $${params.push(filters.language)}`);
  }

  const [criteria, direction] = splitSort(filters.sort);
  const sortExpression = SORT_EXPRESSIONS[criteria] ?? SORT_EXPRESSIONS['ranked'];
  const dir = direction === 'asc' ? 'ASC' : 'DESC';
  const orderBy = `${sortExpression} ${dir} NULLS LAST, id DESC`;

  const limitParam = `$${params.push(filters.limit)}`;
  const offsetParam = `$${params.push(filters.offset)}`;

  return {
    text: `SELECT ${SET_COLUMNS}
             FROM beatmapsets
            WHERE ${conditions.join(' AND ')}
            ORDER BY ${orderBy}
            LIMIT ${limitParam} OFFSET ${offsetParam}`,
    params,
  };
}

export function splitSort(sort: string): [string, 'asc' | 'desc'] {
  const match = /^(.*)_(asc|desc)$/.exec(sort ?? '');
  if (!match) return ['ranked', 'desc'];
  return [match[1], match[2] === 'asc' ? 'asc' : 'desc'];
}
