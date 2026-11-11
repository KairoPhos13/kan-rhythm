/**
 * Beatmapset/beatmap catalogue queries (server/docs/lazer-protocol.md §4).
 */
import { Injectable } from '@nestjs/common';
import { DbService } from '../db/db.service';
import {
  BeatmapRow,
  BeatmapsetRow,
  UserRow,
  toApiBeatmap,
  toApiBeatmapSet,
} from '../api/serializers';
import {
  BEATMAP_COLUMNS,
  buildSearchQuery,
  SEARCH_PAGE_SIZE,
  SET_COLUMNS,
} from './search-query';

export interface SearchInput {
  query?: string;
  category: string;
  mode?: number;
  genre?: number;
  language?: number;
  sort: string;
  offset: number;
  ownerUserId?: number | null;
}

export interface SearchPage {
  beatmapsets: Record<string, unknown>[];
  cursor: Record<string, unknown> | null;
}

@Injectable()
export class BeatmapsService {
  constructor(private readonly db: DbService) {}

  /** `GET /api/v2/beatmapsets/search` with `cursor[key]` paging. */
  async search(input: SearchInput): Promise<SearchPage> {
    const built = buildSearchQuery({ ...input, limit: SEARCH_PAGE_SIZE });
    const result = await this.db.query<BeatmapsetRow>(built.text, built.params);
    const beatmapsets = await this.serialiseSets(result.rows);
    // Opaque cursor: `_id` holds the offset of the next page (§4.1).
    const cursor =
      result.rows.length === SEARCH_PAGE_SIZE
        ? { _id: input.offset + SEARCH_PAGE_SIZE }
        : null;
    return { beatmapsets, cursor };
  }

  /** `GET /api/v2/beatmapsets/{id}`. */
  async getSet(setId: number): Promise<Record<string, unknown> | null> {
    const result = await this.db.query<BeatmapsetRow>(
      `SELECT ${SET_COLUMNS} FROM beatmapsets WHERE id = $1`,
      [setId],
    );
    const sets = await this.serialiseSets(result.rows);
    return sets[0] ?? null;
  }

  /** `GET /api/v2/beatmapsets/lookup?beatmap_id=`. */
  async getSetByBeatmapId(beatmapId: number): Promise<Record<string, unknown> | null> {
    const result = await this.db.query<BeatmapsetRow>(
      `SELECT ${SET_COLUMNS}
         FROM beatmapsets
        WHERE id = (SELECT beatmapset_id FROM beatmaps WHERE id = $1)`,
      [beatmapId],
    );
    const sets = await this.serialiseSets(result.rows);
    return sets[0] ?? null;
  }

  /**
   * `GET /api/v2/beatmaps/lookup?id|checksum|filename`: checksum first (it is
   * the `.osu` MD5), then id, then filename (§4.3).
   */
  async lookupBeatmap(
    lookup: { id?: number; checksum?: string; filename?: string },
  ): Promise<Record<string, unknown> | null> {
    const byChecksum = lookup.checksum
      ? await this.db.query<BeatmapRow>(
          `SELECT ${BEATMAP_COLUMNS} FROM beatmaps WHERE checksum = $1`,
          [lookup.checksum],
        )
      : null;
    const byId =
      !byChecksum?.rows.length && lookup.id != null
        ? await this.db.query<BeatmapRow>(`SELECT ${BEATMAP_COLUMNS} FROM beatmaps WHERE id = $1`, [
            lookup.id,
          ])
        : null;
    const byFilename =
      !byChecksum?.rows.length && !byId?.rows.length && lookup.filename
        ? await this.db.query<BeatmapRow>(
            `SELECT ${BEATMAP_COLUMNS} FROM beatmaps WHERE lower(filename) = lower($1)`,
            [lookup.filename],
          )
        : null;

    const row =
      byChecksum?.rows[0] ?? byId?.rows[0] ?? byFilename?.rows[0] ?? null;
    if (!row) return null;

    const set = await this.getSetRow(row.beatmapset_id);
    if (!set) return null;
    return this.serialiseBeatmap(row, set);
  }

  /** `GET /api/v2/beatmaps/?ids[]=`: batch lookup. */
  async getBeatmaps(ids: number[]): Promise<Record<string, unknown>[]> {
    if (ids.length === 0) return [];
    const result = await this.db.query<BeatmapRow>(
      `SELECT ${BEATMAP_COLUMNS} FROM beatmaps WHERE id = ANY($1::int[])`,
      [ids.slice(0, 50)],
    );
    const out: Record<string, unknown>[] = [];
    for (const row of result.rows) {
      const set = await this.getSetRow(row.beatmapset_id);
      if (set) out.push(await this.serialiseBeatmap(row, set));
    }
    return out;
  }

  /** Archive key and status of a set, for the download endpoint. */
  async getSetRow(setId: number): Promise<BeatmapsetRow | null> {
    const result = await this.db.query<BeatmapsetRow>(
      `SELECT ${SET_COLUMNS} FROM beatmapsets WHERE id = $1`,
      [setId],
    );
    return result.rows[0] ?? null;
  }

  async beatmapsOf(setId: number): Promise<BeatmapRow[]> {
    const result = await this.db.query<BeatmapRow>(
      `SELECT ${BEATMAP_COLUMNS} FROM beatmaps WHERE beatmapset_id = $1 ORDER BY difficulty_rating, id`,
      [setId],
    );
    return result.rows;
  }

  /**
   * The sets one creator owns with one of the given statuses, for the profile's
   * beatmap sections (`GET /api/v2/users/{id}/beatmapsets/{type}`, §3.5).
   */
  async setsOwnedBy(
    userId: number,
    statuses: number[],
    offset: number,
    limit: number,
  ): Promise<Record<string, unknown>[]> {
    const result = await this.db.query<BeatmapsetRow>(
      `SELECT ${SET_COLUMNS}
         FROM beatmapsets
        WHERE user_id = $1 AND status = ANY($2::int[])
        ORDER BY ranked_date DESC NULLS LAST, last_updated DESC, id DESC
        LIMIT $3 OFFSET $4`,
      [userId, statuses, limit, offset],
    );
    return this.serialiseSets(result.rows);
  }

  private async serialiseSets(sets: BeatmapsetRow[]): Promise<Record<string, unknown>[]> {
    if (sets.length === 0) return [];
    const beatmapsBySet = new Map<number, BeatmapRow[]>();
    const beatmapResult = await this.db.query<BeatmapRow>(
      `SELECT ${BEATMAP_COLUMNS} FROM beatmaps WHERE beatmapset_id = ANY($1::int[])`,
      [sets.map((s) => s.id)],
    );
    for (const beatmap of beatmapResult.rows) {
      const list = beatmapsBySet.get(beatmap.beatmapset_id) ?? [];
      list.push(beatmap);
      beatmapsBySet.set(beatmap.beatmapset_id, list);
    }
    const owners = await this.loadUsers(sets.map((s) => s.user_id));

    return sets.map((set) =>
      toApiBeatmapSet(set, beatmapsBySet.get(set.id) ?? [], set.user_id ? owners.get(set.user_id) ?? null : null),
    );
  }

  private async serialiseBeatmap(
    beatmap: BeatmapRow,
    set: BeatmapsetRow,
  ): Promise<Record<string, unknown>> {
    const owners = await this.loadUsers([set.user_id]);
    const owner = set.user_id ? owners.get(set.user_id) ?? null : null;
    const apiSet = toApiBeatmapSet(set, [beatmap], owner);
    return toApiBeatmap(beatmap, {
      userId: set.user_id,
      setStatus: set.status,
      setRankedDate: set.ranked_date,
      setSubmittedDate: set.submitted_date,
      lastUpdated: set.last_updated,
      set: apiSet,
    });
  }

  private async loadUsers(ids: (number | null)[]): Promise<Map<number, UserRow>> {
    const map = new Map<number, UserRow>();
    const wanted = [...new Set(ids.filter((id): id is number => id != null))];
    if (wanted.length === 0) return map;
    const result = await this.db.query<UserRow>(
      `SELECT id, username, country_code, join_date, last_visit, title, is_supporter,
              avatar_url, cover_url
         FROM users
        WHERE id = ANY($1::int[])`,
      [wanted],
    );
    for (const row of result.rows) map.set(row.id, row);
    return map;
  }
}
