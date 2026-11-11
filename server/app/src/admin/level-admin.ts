/**
 * Level operations for the admin CLI (and the e2e suite, which drives exactly
 * this code): listing levels, moving one between statuses, and deleting a
 * level together with its stored package and cover.
 *
 * A level's status is `BeatmapOnlineStatus` (server/docs/lazer-protocol.md §7);
 * `ranked_date` follows it: it is set when the level becomes ranked or loved
 * and cleared when it goes back to pending/WIP/graveyard.
 */
import { Pool } from 'pg';
import { Storage } from '../storage/storage';
import { hasRankedDate } from '../beatmaps/status';
import { LEVEL_STATUSES, LevelStatusName } from './admin-tools';

/** What the `levels` listing shows. */
export interface LevelSummary {
  id: number;
  title: string;
  creator: string;
  status: number;
}

/** `levels`: id, title, creator and status of every level in the catalogue. */
export async function listLevels(pool: Pool): Promise<LevelSummary[]> {
  const result = await pool.query<LevelSummary>(
    `SELECT id, title, creator, status FROM beatmapsets ORDER BY id`,
  );
  return result.rows;
}

/**
 * `set-status`: moves a level to one of {@link LEVEL_STATUSES}. Returns the
 * level afterwards, or null when there is no such level.
 */
export async function setLevelStatus(
  pool: Pool,
  levelId: number,
  status: LevelStatusName | number,
): Promise<LevelSummary | null> {
  const wanted = typeof status === 'number' ? status : LEVEL_STATUSES[status];
  if (!Number.isInteger(wanted)) throw new Error(`unknown level status "${String(status)}"`);

  // Two statements rather than one: `ranked_date` either keeps its date or is
  // cleared, and a parameter used in both branches would need type casts.
  const statement = hasRankedDate(wanted)
    ? `UPDATE beatmapsets SET status = $2, ranked_date = COALESCE(ranked_date, now()), last_updated = now()
        WHERE id = $1 RETURNING id, title, creator, status`
    : `UPDATE beatmapsets SET status = $2, ranked_date = NULL, last_updated = now()
        WHERE id = $1 RETURNING id, title, creator, status`;
  const result = await pool.query<LevelSummary>(statement, [levelId, wanted]);
  return result.rows[0] ?? null;
}

/**
 * `delete-level`: removes the level with everything that belongs to it
 * (difficulties, scores and score tokens cascade; the package and the cover go
 * from storage). Returns the deleted level, or null when there is no such
 * level.
 */
export async function deleteLevel(
  pool: Pool,
  storage: Storage,
  levelId: number,
): Promise<LevelSummary | null> {
  const found = await pool.query<LevelSummary & { archive_key: string; cover_key: string | null }>(
    `SELECT id, title, creator, status, archive_key, cover_key FROM beatmapsets WHERE id = $1`,
    [levelId],
  );
  const level = found.rows[0];
  if (!level) return null;

  await pool.query(`DELETE FROM beatmapsets WHERE id = $1`, [levelId]);
  if (level.archive_key) await storage.remove(level.archive_key);
  if (level.cover_key) await storage.remove(level.cover_key);

  return { id: level.id, title: level.title, creator: level.creator, status: level.status };
}
