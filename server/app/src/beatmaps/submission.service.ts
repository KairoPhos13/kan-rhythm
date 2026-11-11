/**
 * The editor's level submission flow (server/docs/lazer-protocol.md §4.8):
 * reserving the ids of a set (`PUT /beatmap-submission/beatmapsets`) and
 * uploading the package (`PUT` / `PATCH /beatmap-submission/beatmapsets/{id}`).
 *
 * The three steps match `BeatmapSubmissionScreen.cs` exactly:
 *  1. `PutBeatmapSetRequest` reserves `beatmapset_id` and `beatmap_ids`, and
 *     lists the files already stored (`files`) so the client can diff;
 *  2. `ReplaceBeatmapPackageRequest` uploads the full package when there is
 *     nothing to diff against, `PatchBeatmapPackageRequest` uploads only the
 *     changed files and the names of the deleted ones;
 *  3. the client re-imports its own export and looks the beatmaps up by
 *     checksum, so the stored package must stay byte for byte as it was
 *     uploaded (the importer only rewrites ids that are not final).
 *
 * Only the creator of a set may update it, the package runs through the
 * importer's checks (src/beatmaps/importer.ts), and a submission can never
 * rank a level: the status comes from the client's WIP/pending setting while
 * the level is unranked and is left alone once the server owner ranked it.
 */
import { Inject, Injectable, Logger } from '@nestjs/common';
import AdmZip from 'adm-zip';
import { PoolClient } from 'pg';
import { APP_CONFIG, AppConfig } from '../config';
import { apiError } from '../common/http';
import { RateLimiter } from '../common/rate-limiter';
import { DbService } from '../db/db.service';
import { AuthenticatedUser } from '../auth/auth.service';
import { STORAGE, Storage } from '../storage/storage';
import { importPackage, PackageFile, PackageRejectedError, readPackage } from './importer';
import { hasRankedDate } from './status';
import {
  isSafeEntryName,
  MAX_DIFFICULTIES_PER_SET,
  PutBeatmapSetInput,
  sha2Hash,
  statusAfterSubmission,
  unrankedSetLimit,
} from './submission-rules';

/** One file of a stored package as the client diffs them (`BeatmapSetFile`). */
export interface PackageInventoryFile {
  filename: string;
  sha2_hash: string;
}

/** `PutBeatmapSetResponse` (the client reads all three fields). */
export interface PutBeatmapSetResult {
  beatmapset_id: number;
  beatmap_ids: number[];
  files: PackageInventoryFile[];
}

/**
 * How many ids a set may have reserved but not yet uploaded: bounds the
 * reservations of an abandoned submission.
 */
export const MAX_OUTSTANDING_RESERVATIONS = 4 * MAX_DIFFICULTIES_PER_SET;

@Injectable()
export class SubmissionService {
  private readonly logger = new Logger('submission');

  /** In-memory, per creator: the upload rate limit (config: uploads per hour). */
  private readonly uploadLimiter: RateLimiter;

  constructor(
    private readonly db: DbService,
    @Inject(STORAGE) private readonly storage: Storage,
    @Inject(APP_CONFIG) private readonly config: AppConfig,
  ) {
    this.uploadLimiter = new RateLimiter(60 * 60_000, config.submission.uploadsPerHour);
  }

  /** `PUT /beatmap-submission/beatmapsets`: reserve the ids of a submission. */
  async reserve(user: AuthenticatedUser, input: PutBeatmapSetInput): Promise<PutBeatmapSetResult> {
    const client = await this.db.pool.connect();
    try {
      await client.query('BEGIN');

      let setId: number;
      if (input.beatmapsetId == null) {
        await this.enforceUnrankedCap(client, user.id);
        const status = statusAfterSubmission(null, input.target);
        const created = await client.query<{ id: number }>(
          `INSERT INTO beatmapsets (user_id, creator, status, submitted_date, ranked_date,
                                    last_updated, archive_key)
           VALUES ($1, $2, $3, now(), $4, now(), '')
           RETURNING id`,
          [user.id, user.username, status, hasRankedDate(status) ? new Date() : null],
        );
        setId = created.rows[0].id;
      } else {
        const set = await client.query<{ id: number; status: number; user_id: number | null }>(
          `SELECT id, status, user_id FROM beatmapsets WHERE id = $1`,
          [input.beatmapsetId],
        );
        const row = set.rows[0];
        if (!row) throw apiError(404, 'Beatmapset not found');
        if (row.user_id !== user.id) throw apiError(403, 'Only the creator of a level may update it');
        await this.enforceKnownBeatmapIds(client, row.id, input.beatmapsToKeep);
        const status = statusAfterSubmission(row.status, input.target);
        if (status !== row.status) {
          await client.query(
            `UPDATE beatmapsets SET status = $2, ranked_date = $3, last_updated = now() WHERE id = $1`,
            [row.id, status, hasRankedDate(status) ? new Date() : null],
          );
        }
        setId = row.id;
      }

      const newIds = await this.reserveBeatmapIds(client, setId, input.beatmapsToCreate);

      await client.query('COMMIT');

      // New ids come first: `SubmissionBeatmapExporter` hands `beatmap_ids` out
      // in order to the difficulties that have none yet, and must not consume
      // an id that belongs to an existing difficulty.
      return {
        beatmapset_id: setId,
        beatmap_ids: [...newIds, ...input.beatmapsToKeep],
        files: await this.inventoryOf(setId),
      };
    } catch (error) {
      await client.query('ROLLBACK');
      throw error;
    } finally {
      client.release();
    }
  }

  /** `PUT /beatmap-submission/beatmapsets/{id}`: replace the full package. */
  async replacePackage(user: AuthenticatedUser, setId: number, archive: Buffer): Promise<void> {
    this.checkUploadRate(user);

    let files: PackageFile[];
    try {
      files = readPackage(new AdmZip(archive));
    } catch {
      throw apiError(400, 'The upload is not a valid .osz level package');
    }
    await this.applyPackage(user, setId, files);
  }

  /** `PATCH /beatmap-submission/beatmapsets/{id}`: changed files and deletions. */
  async patchPackage(
    user: AuthenticatedUser,
    setId: number,
    changed: PackageFile[],
    deleted: string[],
  ): Promise<void> {
    this.checkUploadRate(user);

    const set = await this.loadOwnedSet(user, setId);
    const stored = await this.readPackageOf(set.archive_key);
    if (!stored) {
      throw apiError(400, 'This level has no package to patch; upload the full package');
    }

    const merged = new Map<string, Buffer>();
    for (const file of stored) merged.set(file.entryName, file.data);
    for (const name of deleted) {
      if (!isSafeEntryName(name)) throw apiError(400, `Invalid file name "${name}"`);
      merged.delete(name);
    }
    for (const file of changed) merged.set(file.entryName, file.data);

    await this.applyPackage(user, setId, [...merged.entries()].map(([entryName, data]) => ({ entryName, data })));
  }

  /** Runs the importer's checks on the package and stores it. */
  private async applyPackage(
    user: AuthenticatedUser,
    setId: number,
    files: PackageFile[],
  ): Promise<void> {
    const set = await this.loadOwnedSet(user, setId);

    const totalBytes = files.reduce((sum, file) => sum + file.data.length, 0);
    if (totalBytes > this.config.submission.maxPackageBytes) {
      throw apiError(413, `Level package is larger than the ${this.config.submission.maxPackageBytes} byte limit`);
    }
    for (const file of files) {
      if (!isSafeEntryName(file.entryName)) throw apiError(400, `Invalid file name "${file.entryName}"`);
    }

    const allowed = await this.allowedBeatmapIds(set.id);
    try {
      await importPackage(
        { db: this.db.pool, storage: this.storage, log: (message) => this.logger.log(message) },
        files,
        `beatmapset ${set.id}`,
        {
          ownerUserId: user.id,
          setId: set.id,
          // The submission never changes the ranking state on its own.
          status: set.status,
          // The level is credited to the submitting account, whatever the
          // package's metadata says ("mapped by" the creator).
          creator: user.username,
          beatmapIds: 'package',
          allowedBeatmapIds: allowed,
        },
      );
    } catch (error) {
      // The importer's refusals carry their own message for the creator.
      if (error instanceof PackageRejectedError) throw apiError(400, error.message);
      throw error;
    }

    // Whatever was reserved and not used is obsolete once the package landed.
    await this.db.query(`DELETE FROM beatmap_id_reservations WHERE beatmapset_id = $1`, [set.id]);
  }

  /** Rate limit on package uploads, per creator. */
  private checkUploadRate(user: AuthenticatedUser): void {
    if (!this.uploadLimiter.check(String(user.id))) {
      throw apiError(429, 'Too many level uploads, please try again later');
    }
  }

  /**
   * osu!'s cap on unranked (pending/WIP) sets: a base allowance plus one slot
   * per ranked set, up to the configured maximum.
   */
  private async enforceUnrankedCap(client: PoolClient, userId: number): Promise<void> {
    const counts = await client.query<{ unranked: number; ranked: number }>(
      `SELECT count(*) FILTER (WHERE status IN (0, -1))::int AS unranked,
              count(*) FILTER (WHERE status IN (1, 2))::int AS ranked
         FROM beatmapsets
        WHERE user_id = $1`,
      [userId],
    );
    const unranked = counts.rows[0]?.unranked ?? 0;
    const limit = unrankedSetLimit(
      this.config.submission.unrankedSetsBase,
      this.config.submission.unrankedSetsMax,
      counts.rows[0]?.ranked ?? 0,
    );
    if (unranked >= limit) {
      throw apiError(
        422,
        `You already have ${unranked} pending or WIP level(s); the limit is ${limit} ` +
          '(it grows with every ranked level you have)',
      );
    }
  }

  /** `beatmaps_to_keep` must name difficulties of this very set. */
  private async enforceKnownBeatmapIds(
    client: PoolClient,
    setId: number,
    beatmapIds: number[],
  ): Promise<void> {
    if (beatmapIds.length === 0) return;
    const known = await client.query<{ id: number }>(
      `SELECT id FROM beatmaps WHERE beatmapset_id = $1 AND id = ANY($2::int[])
       UNION
       SELECT id FROM beatmap_id_reservations WHERE beatmapset_id = $1 AND id = ANY($2::int[])`,
      [setId, beatmapIds],
    );
    const knownIds = new Set(known.rows.map((row) => row.id));
    const unknown = beatmapIds.filter((id) => !knownIds.has(id));
    if (unknown.length > 0) {
      throw apiError(400, `Beatmap id(s) ${unknown.join(', ')} do not belong to beatmapset ${setId}`);
    }
  }

  /** Fresh beatmap ids reserved for the package the client is about to export. */
  private async reserveBeatmapIds(
    client: PoolClient,
    setId: number,
    count: number,
  ): Promise<number[]> {
    if (count === 0) return [];
    const outstanding = await client.query<{ n: string }>(
      `SELECT count(*) AS n FROM beatmap_id_reservations WHERE beatmapset_id = $1`,
      [setId],
    );
    if (Number(outstanding.rows[0]?.n ?? 0) + count > MAX_OUTSTANDING_RESERVATIONS) {
      throw apiError(400, 'This level has too many unuploaded beatmap ids reserved');
    }

    const ids: number[] = [];
    for (let i = 0; i < count; i += 1) {
      const allocated = await client.query<{ id: number }>(
        `SELECT nextval(pg_get_serial_sequence('beatmaps', 'id'))::int AS id`,
      );
      const id = allocated.rows[0].id;
      await client.query(`INSERT INTO beatmap_id_reservations (id, beatmapset_id) VALUES ($1, $2)`, [
        id,
        setId,
      ]);
      ids.push(id);
    }
    return ids;
  }

  /** Ids a package may write into its difficulties. */
  private async allowedBeatmapIds(setId: number): Promise<Set<number>> {
    const rows = await this.db.query<{ id: number }>(
      `SELECT id FROM beatmaps WHERE beatmapset_id = $1
       UNION
       SELECT id FROM beatmap_id_reservations WHERE beatmapset_id = $1`,
      [setId],
    );
    return new Set(rows.rows.map((row) => row.id));
  }

  private async loadOwnedSet(
    user: AuthenticatedUser,
    setId: number,
  ): Promise<{ id: number; status: number; user_id: number; archive_key: string }> {
    const result = await this.db.query<{ id: number; status: number; user_id: number; archive_key: string }>(
      `SELECT id, status, user_id, archive_key FROM beatmapsets WHERE id = $1`,
      [setId],
    );
    const set = result.rows[0];
    if (!set) throw apiError(404, 'Beatmapset not found');
    if (set.user_id !== user.id) throw apiError(403, 'Only the creator of a level may update it');
    return set;
  }

  /** The stored package, or `null` when the set has none yet. */
  private async readPackageOf(archiveKey: string): Promise<PackageFile[] | null> {
    if (!archiveKey) return null;
    const archive = await this.storage.read(archiveKey);
    if (!archive) return null;
    try {
      return new AdmZip(archive)
        .getEntries()
        .filter((entry) => !entry.isDirectory)
        .map((entry) => ({ entryName: entry.entryName, data: entry.getData() }));
    } catch {
      return null;
    }
  }

  /** The stored files and their `sha2_hash`es, for the client to diff against. */
  private async inventoryOf(setId: number): Promise<PackageInventoryFile[]> {
    const set = await this.db.query<{ archive_key: string }>(
      `SELECT archive_key FROM beatmapsets WHERE id = $1`,
      [setId],
    );
    const files = await this.readPackageOf(set.rows[0]?.archive_key ?? '');
    if (!files) return [];
    return files.map((file) => ({ filename: file.entryName, sha2_hash: sha2Hash(file.data) }));
  }
}
