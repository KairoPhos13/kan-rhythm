/**
 * Level import: `.osz` archive → rewritten archive in storage + catalogue rows.
 *
 * The importer rewrites `BeatmapID:` / `BeatmapSetID:` into every `.osu`,
 * re-zips, and stores each rewritten `.osu`'s MD5 as the beatmap checksum:
 * that exact checksum is what the client looks up after download and later
 * sends as `beatmap_hash` (server/docs/lazer-protocol.md §8.2).
 */
import path from 'node:path';
import AdmZip from 'adm-zip';
import { Pool, PoolClient } from 'pg';
import { Storage } from '../storage/storage';
import { md5Of, OsuFileInfo, parseOsu, rewriteOsu } from './osu-file';

export interface ImportDeps {
  db: Pool;
  storage: Storage;
  log?: (message: string) => void;
}

export interface ImportOptions {
  /** Username owning the set; defaults to the first registered user. */
  ownerUsername?: string;
}

export interface ImportedBeatmap {
  id: number;
  version: string;
  checksum: string;
}

export interface ImportResult {
  setId: number;
  created: boolean;
  beatmaps: ImportedBeatmap[];
  coverKey: string | null;
  archiveKey: string;
}

interface ParsedDifficulty {
  entryName: string;
  text: string;
  info: OsuFileInfo;
}

const IMAGE_CONTENT_TYPES: Record<string, string> = {
  '.jpg': 'image/jpeg',
  '.jpeg': 'image/jpeg',
  '.png': 'image/png',
  '.webp': 'image/webp',
  '.avif': 'image/avif',
};

export function archiveKeyFor(setId: number): string {
  return `beatmapsets/${setId}.osz`;
}

function coverKeyFor(setId: number, backgroundFile: string): string {
  const ext = path.extname(backgroundFile).toLowerCase() || '.jpg';
  return `beatmapsets/${setId}/cover${ext}`;
}

function clampMode(mode: number): number {
  // The client throws for `mode_int` outside 0-3 (lazer-protocol.md §4.6).
  return Number.isFinite(mode) && mode >= 0 && mode <= 3 ? mode : 0;
}

export async function importOsz(
  deps: ImportDeps,
  filePath: string,
  options: ImportOptions = {},
): Promise<ImportResult> {
  const log = deps.log ?? ((): void => undefined);
  const zip = new AdmZip(filePath);
  const entries = zip.getEntries().filter((entry) => !entry.isDirectory);
  const osuEntries = entries.filter((entry) => /\.osu$/i.test(entry.entryName));
  if (osuEntries.length === 0) throw new Error(`${filePath}: no .osu files in archive`);

  const parsed: ParsedDifficulty[] = osuEntries.map((entry) => {
    const text = entry.getData().toString('utf8');
    return { entryName: entry.entryName, text, info: parseOsu(text) };
  });

  const client = await deps.db.connect();
  try {
    await client.query('BEGIN');

    const ownerId = await resolveOwnerId(client, options.ownerUsername);
    const requestedSetId = parsed[0].info.metadata.beatmapSetId;

    let setId: number;
    let created = false;
    if (requestedSetId > 0 && (await setExists(client, requestedSetId))) {
      setId = requestedSetId;
      log(`updating beatmapset ${setId}`);
    } else {
      const allocated = await client.query<{ id: number }>(
        `INSERT INTO beatmapsets (user_id, archive_key, submitted_date, ranked_date, last_updated)
         VALUES ($1, '', now(), now(), now())
         RETURNING id`,
        [ownerId],
      );
      setId = allocated.rows[0].id;
      created = true;
      log(`created beatmapset ${setId}`);
    }

    const setMeta = parsed[0].info;
    await client.query(
      `UPDATE beatmapsets
          SET user_id = COALESCE($2, user_id),
              title = $3, title_unicode = $4, artist = $5, artist_unicode = $6,
              creator = $7, source = $8, tags = $9,
              bpm = $10, status = 1, last_updated = now(),
              ranked_date = COALESCE(ranked_date, now()),
              archive_key = $11
        WHERE id = $1`,
      [
        setId,
        ownerId,
        setMeta.metadata.title,
        setMeta.metadata.titleUnicode,
        setMeta.metadata.artist,
        setMeta.metadata.artistUnicode,
        setMeta.metadata.creator,
        setMeta.metadata.source,
        setMeta.metadata.tags,
        setMeta.bpm,
        archiveKeyFor(setId),
      ],
    );

    // Rewrite ids into every .osu and keep each rewritten checksum.
    const imported: ImportedBeatmap[] = [];
    const rewrittenByEntry = new Map<string, string>();
    for (const difficulty of parsed) {
      const beatmapId = await resolveBeatmapId(client, setId, difficulty.info.metadata.version);
      const rewritten = rewriteOsu(difficulty.text, { beatmapId, beatmapSetId: setId });
      const checksum = md5Of(rewritten);
      rewrittenByEntry.set(difficulty.entryName, rewritten);

      await client.query(
        `INSERT INTO beatmaps
           (id, beatmapset_id, version, filename, checksum, mode_int, difficulty_rating,
            cs, ar, accuracy, drain, total_length, hit_length, max_combo, bpm,
            count_circles, count_sliders, count_spinners, last_updated)
         VALUES ($1, $2, $3, $4, $5, $6, 0, $7, $8, $9, $10, $11, $12, $13, $14, $15, $16, $17, now())
         ON CONFLICT (id) DO UPDATE
            SET version = EXCLUDED.version,
                filename = EXCLUDED.filename,
                checksum = EXCLUDED.checksum,
                mode_int = EXCLUDED.mode_int,
                cs = EXCLUDED.cs, ar = EXCLUDED.ar, accuracy = EXCLUDED.accuracy,
                drain = EXCLUDED.drain,
                total_length = EXCLUDED.total_length, hit_length = EXCLUDED.hit_length,
                max_combo = EXCLUDED.max_combo, bpm = EXCLUDED.bpm,
                count_circles = EXCLUDED.count_circles,
                count_sliders = EXCLUDED.count_sliders,
                count_spinners = EXCLUDED.count_spinners,
                last_updated = now()`,
        [
          beatmapId,
          setId,
          difficulty.info.metadata.version,
          difficulty.entryName,
          checksum,
          clampMode(difficulty.info.general.mode),
          difficulty.info.difficulty.circleSize,
          difficulty.info.difficulty.approachRate,
          difficulty.info.difficulty.overallDifficulty,
          difficulty.info.difficulty.hpDrainRate,
          difficulty.info.totalLengthSeconds,
          difficulty.info.hitLengthSeconds,
          difficulty.info.maxCombo,
          difficulty.info.bpm,
          difficulty.info.countCircles,
          difficulty.info.countSliders,
          difficulty.info.countSpinners,
        ],
      );
      imported.push({ id: beatmapId, version: difficulty.info.metadata.version, checksum });
    }

    // Difficulties that vanished from the archive go away with their rows.
    await client.query(
      `DELETE FROM beatmaps
        WHERE beatmapset_id = $1
          AND NOT (id = ANY($2::bigint[]))`,
      [setId, imported.map((b) => b.id)],
    );

    // Rebuild the archive with the rewritten .osu files, in the original order.
    const outZip = new AdmZip();
    for (const entry of entries) {
      const rewritten = rewrittenByEntry.get(entry.entryName);
      outZip.addFile(
        entry.entryName,
        rewritten != null ? Buffer.from(rewritten, 'utf8') : entry.getData(),
      );
    }
    const archiveKey = archiveKeyFor(setId);
    await deps.storage.put(archiveKey, outZip.toBuffer(), 'application/zip');

    // Cover image: first background referenced by any difficulty.
    let coverKey: string | null = null;
    const background = parsed.find((d) => d.info.backgroundFile)?.info.backgroundFile ?? null;
    if (background) {
      const backgroundEntry = entries.find(
        (entry) => basename(entry.entryName).toLowerCase() === basename(background).toLowerCase(),
      );
      if (backgroundEntry) {
        coverKey = coverKeyFor(setId, backgroundEntry.entryName);
        const ext = path.extname(backgroundEntry.entryName).toLowerCase();
        await deps.storage.put(
          coverKey,
          backgroundEntry.getData(),
          IMAGE_CONTENT_TYPES[ext] ?? 'application/octet-stream',
        );
      }
    }
    await client.query('UPDATE beatmapsets SET cover_key = $2 WHERE id = $1', [setId, coverKey]);

    await client.query('COMMIT');
    return { setId, created, beatmaps: imported, coverKey, archiveKey };
  } catch (error) {
    await client.query('ROLLBACK');
    throw error;
  } finally {
    client.release();
  }
}

function basename(entryName: string): string {
  return entryName.split(/[\\/]/).pop() ?? entryName;
}

async function setExists(client: PoolClient, setId: number): Promise<boolean> {
  const result = await client.query('SELECT 1 FROM beatmapsets WHERE id = $1', [setId]);
  return (result.rowCount ?? 0) > 0;
}

/**
 * Keeps beatmap ids stable across re-imports: an existing difficulty with the
 * same name keeps its id (and therefore its checksum identity), new
 * difficulties get a fresh one.
 */
async function resolveBeatmapId(
  client: PoolClient,
  setId: number,
  version: string,
): Promise<number> {
  const existing = await client.query<{ id: number }>(
    `SELECT id FROM beatmaps WHERE beatmapset_id = $1 AND version = $2`,
    [setId, version],
  );
  if (existing.rows.length > 0) return existing.rows[0].id;
  const allocated = await client.query<{ id: number }>(
    `SELECT nextval(pg_get_serial_sequence('beatmaps', 'id'))::int AS id`,
  );
  return allocated.rows[0].id;
}

async function resolveOwnerId(
  client: PoolClient,
  ownerUsername: string | undefined,
): Promise<number> {
  if (ownerUsername) {
    const result = await client.query<{ id: number }>(
      `SELECT id FROM users WHERE username_lower = lower($1)`,
      [ownerUsername],
    );
    if (result.rows.length === 0) {
      throw new Error(`owner user "${ownerUsername}" does not exist`);
    }
    return result.rows[0].id;
  }
  const fallback = await client.query<{ id: number }>(
    `SELECT id FROM users ORDER BY id LIMIT 1`,
  );
  if (fallback.rows.length === 0) {
    throw new Error('no users exist yet: register one first or pass --owner <username>');
  }
  return fallback.rows[0].id;
}
