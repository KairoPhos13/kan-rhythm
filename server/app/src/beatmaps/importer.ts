/**
 * Level import: `.osz` archive → rewritten archive in storage + catalogue rows.
 *
 * The importer rewrites `BeatmapID:` / `BeatmapSetID:` into every `.osu`,
 * re-zips, and stores each rewritten `.osu`'s MD5 as the beatmap checksum:
 * that exact checksum is what the client looks up after download and later
 * sends as `beatmap_hash` (server/docs/lazer-protocol.md §8.2).
 *
 * Two callers share this code (that is why the checks live in
 * {@link checkPackage}):
 *  - the import CLI (`src/import/cli.ts`), which takes the ids from the
 *    existing rows (`beatmapIds: 'stable'`, the default) and imports sets as
 *    ranked;
 *  - the editor's submission flow (`src/beatmaps/submission.service.ts`), which
 *    must keep the package byte for byte as the client exported it (`beatmapIds:
 *    'package'`: the ids the client was given at PUT /beatmap-submission/beatmapsets
 *    are already written into the files, so nothing is rewritten and the
 *    checksums stay identical to the creator's local copies).
 */
import { statSync } from 'node:fs';
import path from 'node:path';
import AdmZip from 'adm-zip';
import { Pool, PoolClient } from 'pg';
import { DEFAULT_MAX_PACKAGE_BYTES } from '../config';
import { Storage } from '../storage/storage';
import { hasRankedDate } from './status';
import { md5Of, OsuFileInfo, parseOsu, rewriteOsu } from './osu-file';

export interface ImportDeps {
  db: Pool;
  storage: Storage;
  log?: (message: string) => void;
}

export interface ImportOptions {
  /** Username owning the set; defaults to the first registered user. */
  ownerUsername?: string;
  /** Owner of the set (submission): the authenticated creator. */
  ownerUserId?: number;
  /** Update this set (submission); otherwise the archive's set id is reused. */
  setId?: number;
  /** Status the set has after the import (default 1 = ranked: CLI imports). */
  status?: number;
  /** Creator shown on the set (default: the `.osu` metadata). */
  creator?: string;
  /**
   * How a difficulty gets its beatmap id:
   *  - `stable` (default): an existing (beatmapset, version, mode) row keeps its
   *    id, anything else gets a fresh one;
   *  - `package`: the id written in the `.osu`, which must be one of
   *    `allowedBeatmapIds` (the ids handed out at PUT /beatmap-submission/beatmapsets
   *    or already stored on this set).
   */
  beatmapIds?: 'stable' | 'package';
  allowedBeatmapIds?: ReadonlySet<number>;
  /** Bounds for reading the archive (audit S2); defaults to the package limit. */
  zipLimits?: ZipLimits;
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

/** One file of the package to import. */
export interface PackageFile {
  /** Archive entry name (`artist - title (creator) [diff].osu`, `audio.wav`, …). */
  entryName: string;
  data: Buffer;
}

/**
 * A package the importer refuses (no playable difficulty, an id that does not
 * belong to the set): a user-facing message, mapped to HTTP 400 by the
 * submission routes and to exit code 1 by the import CLI.
 */
export class PackageRejectedError extends Error {}

/** A package refused for size: mapped to HTTP 413 by the submission routes. */
export class PackageTooLargeError extends PackageRejectedError {}

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

function basename(entryName: string): string {
  return entryName.split(/[\\/]/).pop() ?? entryName;
}

/**
 * The importer's checks on a package: every `.osu` must parse, and at least one
 * difficulty must be playable in kan! (mode 0; kan! claims legacy ruleset id 0,
 * so mode 1-3 difficulties cannot be played). They are skipped here and
 * stripped from the stored archive. Throws when nothing is playable.
 */
export function checkPackage(
  files: PackageFile[],
  log: (message: string) => void,
  label = 'archive',
): ParsedDifficulty[] {
  const osuFiles = files.filter((file) => /\.osu$/i.test(file.entryName));
  if (osuFiles.length === 0) throw new PackageRejectedError(`${label}: no .osu files in archive`);

  const parsed: ParsedDifficulty[] = [];
  for (const file of osuFiles) {
    const text = file.data.toString('utf8');
    const info = parseOsu(text);
    const mode = info.general.mode;
    if (mode !== 0) {
      log(`skipping ${info.metadata.version}: mode ${mode} is not playable in kan!`);
      continue;
    }
    parsed.push({ entryName: file.entryName, text, info });
  }
  // Nothing is written (no rows, no files) when there is nothing playable.
  if (parsed.length === 0) {
    throw new PackageRejectedError(`${label}: no mode-0 (kan!-playable) difficulties in archive`);
  }
  return parsed;
}

/**
 * Bounds on reading a package archive, all derived from the configured package
 * limit (audit S2: zip bomb defence).
 */
export interface ZipLimits {
  /** Most entries (files) one package may carry. */
  maxEntries: number;
  /** Largest size one entry may declare; the inflater is capped at it too. */
  maxEntryBytes: number;
  /** Largest total of declared uncompressed sizes. */
  maxTotalBytes: number;
  /** Largest ratio of declared uncompressed bytes to the archive's bytes. */
  maxCompressionRatio: number;
}

/**
 * Package bounds for a package limit in bytes. A level package is mostly
 * already-compressed media (audio, video, images) plus `.osu` text, which
 * compresses up to about 20x; the bounds are generous for that and useless for
 * a bomb (a small archive of zeros declares far more than 50x its size).
 */
export function zipLimitsFor(maxPackageBytes: number): ZipLimits {
  return {
    maxEntries: 1024,
    maxEntryBytes: maxPackageBytes,
    maxTotalBytes: 2 * maxPackageBytes,
    maxCompressionRatio: 50,
  };
}

/** Reads an `.osz` file from disk under the zip bounds (the import CLI). */
export function readPackageFile(filePath: string, limits: ZipLimits): PackageFile[] {
  return readPackageEntries(new AdmZip(filePath), statSync(filePath).size, limits);
}

/** Reads an `.osz` buffer under the zip bounds (uploads, stored packages). */
export function readPackageBuffer(archive: Buffer, limits: ZipLimits): PackageFile[] {
  return readPackageEntries(new AdmZip(archive), archive.length, limits);
}

/**
 * Reads a zip into plain files (entry order preserved, directories dropped).
 *
 * The central directory is checked *before* anything is inflated: entry count,
 * each declared uncompressed size and their total against the archive's own
 * size (audit S2). Inflation is hard-capped per entry: adm-zip's inflater
 * refuses to produce more than the entry's declared size, so a header that
 * lies about its size fails instead of exhausting memory, and the result is
 * checked against `maxEntryBytes` once more.
 */
function readPackageEntries(zip: AdmZip, archiveBytes: number, limits: ZipLimits): PackageFile[] {
  const entries = zip.getEntries().filter((entry) => !entry.isDirectory);
  if (entries.length > limits.maxEntries) {
    throw new PackageRejectedError(
      `archive has ${entries.length} files (the limit is ${limits.maxEntries})`,
    );
  }

  let declaredTotal = 0;
  for (const entry of entries) {
    const declared = entry.header.size;
    if (declared > limits.maxEntryBytes) {
      throw new PackageTooLargeError(
        `archive entry "${entry.entryName}" declares ${declared} bytes (the limit is ${limits.maxEntryBytes})`,
      );
    }
    declaredTotal += declared;
  }
  if (declaredTotal > limits.maxTotalBytes) {
    throw new PackageTooLargeError(
      `archive inflates to ${declaredTotal} bytes (the limit is ${limits.maxTotalBytes})`,
    );
  }
  if (archiveBytes > 0 && declaredTotal > limits.maxCompressionRatio * archiveBytes) {
    throw new PackageTooLargeError(
      `archive inflates ${Math.round(declaredTotal / archiveBytes)}x its own size ` +
        `(the limit is ${limits.maxCompressionRatio}x)`,
    );
  }

  return entries.map((entry) => ({
    entryName: entry.entryName,
    data: entryData(entry, limits.maxEntryBytes),
  }));
}

/** One entry's data, hard-capped at `maxEntryBytes` whatever its header says. */
function entryData(entry: AdmZip.IZipEntry, maxEntryBytes: number): Buffer {
  let data: Buffer;
  try {
    data = entry.getData();
  } catch {
    throw new PackageRejectedError(
      `archive entry "${entry.entryName}" is corrupt or lies about its size`,
    );
  }
  if (data.length > maxEntryBytes) {
    throw new PackageTooLargeError(
      `archive entry "${entry.entryName}" inflates past the ${maxEntryBytes} byte limit`,
    );
  }
  return data;
}

/** Imports an `.osz` file from disk (the import CLI). */
export async function importOsz(
  deps: ImportDeps,
  filePath: string,
  options: ImportOptions = {},
): Promise<ImportResult> {
  const limits = options.zipLimits ?? zipLimitsFor(DEFAULT_MAX_PACKAGE_BYTES);
  return importPackage(deps, readPackageFile(filePath, limits), filePath, options);
}

/**
 * Imports a package: runs the checks, keeps/creates the catalogue rows and
 * stores the rewritten archive plus the cover image.
 */
export async function importPackage(
  deps: ImportDeps,
  files: PackageFile[],
  label = 'archive',
  options: ImportOptions = {},
): Promise<ImportResult> {
  const log = deps.log ?? ((): void => undefined);
  const parsed = checkPackage(files, log, label);
  const packageIds = options.beatmapIds === 'package';

  const client = await deps.db.connect();
  try {
    await client.query('BEGIN');

    const ownerId =
      options.ownerUserId != null
        ? options.ownerUserId
        : await resolveOwnerId(client, options.ownerUsername);
    const status = Number.isFinite(options.status) ? Math.trunc(options.status as number) : 1;

    let setId: number;
    let created = false;
    if (options.setId != null) {
      const existing = await setExists(client, options.setId);
      if (!existing) throw new PackageRejectedError(`beatmapset ${options.setId} does not exist`);
      setId = options.setId;
    } else {
      const requestedSetId = parsed[0].info.metadata.beatmapSetId;
      if (requestedSetId > 0 && (await setExists(client, requestedSetId))) {
        setId = requestedSetId;
        log(`updating beatmapset ${setId}`);
      } else {
        const allocated = await client.query<{ id: number }>(
          `INSERT INTO beatmapsets (user_id, archive_key, submitted_date, ranked_date, last_updated)
           VALUES ($1, '', now(), ${newRankedDateSql(status)}, now())
           RETURNING id`,
          [ownerId],
        );
        setId = allocated.rows[0].id;
        created = true;
        log(`created beatmapset ${setId}`);
      }
    }

    // Ids first: what is not in the package any more goes away with its row
    // before the upserts run, so a difficulty that changed its name can take
    // over the (beatmapset, version, mode) slot of a removed one.
    const resolved: { difficulty: ParsedDifficulty; beatmapId: number }[] = [];
    for (const difficulty of parsed) {
      const modeInt = clampMode(difficulty.info.general.mode);
      const beatmapId = packageIds
        ? await packageBeatmapId(client, setId, difficulty, options.allowedBeatmapIds)
        : await resolveBeatmapId(client, setId, difficulty.info.metadata.version, modeInt);
      resolved.push({ difficulty, beatmapId });
    }
    await client.query(
      `DELETE FROM beatmaps
        WHERE beatmapset_id = $1
          AND NOT (id = ANY($2::bigint[]))`,
      [setId, resolved.map((entry) => entry.beatmapId)],
    );

    const setMeta = parsed[0].info;
    await client.query(
      `UPDATE beatmapsets
          SET user_id = COALESCE($2, user_id),
              title = $3, title_unicode = $4, artist = $5, artist_unicode = $6,
              creator = COALESCE($12, $7), source = $8, tags = $9,
              bpm = $10, status = ${status}, last_updated = now(),
              ranked_date = ${rankedDateSql(status)},
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
        options.creator ?? null,
      ],
    );

    // Rewrite ids into every playable .osu (only where they are not already the
    // final ones) and keep each resulting checksum.
    const imported: ImportedBeatmap[] = [];
    const storedByEntry = new Map<string, string>();
    for (const { difficulty, beatmapId } of resolved) {
      const rewritten = packageIds
        ? difficulty.text
        : rewriteOsu(difficulty.text, { beatmapId, beatmapSetId: setId });
      const checksum = md5Of(rewritten);
      storedByEntry.set(difficulty.entryName, rewritten);

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

    // Rebuild the archive with the stored .osu files, in the original order.
    // Skipped (non-mode-0) difficulties are stripped from the stored archive.
    const outZip = new AdmZip();
    for (const file of files) {
      const stored = storedByEntry.get(file.entryName);
      if (stored != null) {
        outZip.addFile(file.entryName, Buffer.from(stored, 'utf8'));
      } else if (!/\.osu$/i.test(file.entryName)) {
        outZip.addFile(file.entryName, file.data);
      }
    }
    const archiveKey = archiveKeyFor(setId);
    await deps.storage.put(archiveKey, outZip.toBuffer(), 'application/zip');

    // Cover image: first background referenced by any difficulty.
    let coverKey: string | null = null;
    const background = parsed.find((d) => d.info.backgroundFile)?.info.backgroundFile ?? null;
    if (background) {
      const backgroundFile = files.find(
        (file) => basename(file.entryName).toLowerCase() === basename(background).toLowerCase(),
      );
      if (backgroundFile) {
        coverKey = coverKeyFor(setId, backgroundFile.entryName);
        const ext = path.extname(backgroundFile.entryName).toLowerCase();
        await deps.storage.put(
          coverKey,
          backgroundFile.data,
          IMAGE_CONTENT_TYPES[ext] ?? 'application/octet-stream',
        );
      }
    }
    await client.query('UPDATE beatmapsets SET cover_key = $2 WHERE id = $1', [setId, coverKey]);

    await client.query('COMMIT');
    return { setId, created, beatmaps: imported, coverKey, archiveKey };
  } catch (error) {
    await client.query('ROLLBACK');
    // Belt and braces for crafted values (audit S8): a PostgreSQL data
    // exception (out of range, truncated) refuses the package instead of
    // surfacing as a 500 after a full upload.
    if (isDataException(error)) {
      throw new PackageRejectedError('the package carries values out of range');
    }
    throw error;
  } finally {
    client.release();
  }
}

/** PostgreSQL data exceptions (SQLSTATE 22xxx): values that do not fit. */
function isDataException(error: unknown): boolean {
  const code = (error as { code?: string }).code;
  return typeof code === 'string' && code.startsWith('22');
}

/** `ranked_date` follows the status: set for ranked/approved/loved, cleared otherwise. */
function rankedDateSql(status: number): string {
  return hasRankedDate(status) ? 'COALESCE(ranked_date, now())' : 'NULL';
}

/** `ranked_date` of a freshly created set. */
function newRankedDateSql(status: number): string {
  return hasRankedDate(status) ? 'now()' : 'NULL';
}

/**
 * The id the package itself carries (submission flow). It must be one the set
 * was given at PUT /beatmap-submission/beatmapsets (or already owns): a foreign
 * id would let a package overwrite another set's difficulty.
 */
async function packageBeatmapId(
  client: PoolClient,
  setId: number,
  difficulty: ParsedDifficulty,
  allowed: ReadonlySet<number> | undefined,
): Promise<number> {
  const beatmapId = difficulty.info.metadata.beatmapId;
  if (!allowed || !allowed.has(beatmapId)) {
    throw new PackageRejectedError(
      `difficulty "${difficulty.info.metadata.version}" carries BeatmapID ${beatmapId}, ` +
        `which does not belong to beatmapset ${setId}`,
    );
  }
  // Belt and braces: the id may not be stored on another set either.
  const taken = await client.query<{ beatmapset_id: number }>(
    `SELECT beatmapset_id FROM beatmaps WHERE id = $1`,
    [beatmapId],
  );
  if (taken.rows.length > 0 && taken.rows[0].beatmapset_id !== setId) {
    throw new PackageRejectedError(`BeatmapID ${beatmapId} already belongs to another beatmapset`);
  }
  return beatmapId;
}

async function setExists(client: PoolClient, setId: number): Promise<boolean> {
  const result = await client.query('SELECT 1 FROM beatmapsets WHERE id = $1', [setId]);
  return (result.rowCount ?? 0) > 0;
}

/**
 * Keeps beatmap ids stable across re-imports: an existing difficulty with the
 * same name and mode keeps its id (and therefore its checksum identity), new
 * difficulties get a fresh one. The identity is (beatmapset_id, version, mode):
 * archives can carry two difficulties with the same `Version` in different
 * modes (e.g. "241526 Soleily - Renatus.osz" has "Normal" in mode 0 and 3) and
 * those must not collide.
 */
export async function resolveBeatmapId(
  client: PoolClient,
  setId: number,
  version: string,
  modeInt: number,
): Promise<number> {
  const existing = await client.query<{ id: number }>(
    `SELECT id FROM beatmaps WHERE beatmapset_id = $1 AND version = $2 AND mode_int = $3`,
    [setId, version, modeInt],
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
