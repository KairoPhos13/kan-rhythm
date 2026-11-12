/**
 * Plain SQL migration runner. Files in `migrations/` are applied in
 * alphabetical order exactly once, each in its own transaction, and recorded
 * in `schema_migrations`.
 *
 * One migrator at a time across processes (audit S12): the run holds a
 * session-level advisory lock, so two instances starting together (a container
 * restart while another is still migrating) queue up instead of both applying
 * the same file.
 */
import { readFileSync, readdirSync } from 'node:fs';
import path from 'node:path';
import { Pool } from 'pg';

/** Advisory lock key for migration runs ('kan' as a number). */
export const MIGRATIONS_LOCK_KEY = 0x6b616e;

export async function runMigrations(pool: Pool, migrationsDir: string): Promise<string[]> {
  const client = await pool.connect();
  const applied: string[] = [];
  try {
    await client.query('SELECT pg_advisory_lock($1)', [MIGRATIONS_LOCK_KEY]);
    await client.query(
      `CREATE TABLE IF NOT EXISTS schema_migrations (
         name        text PRIMARY KEY,
         applied_at  timestamptz NOT NULL DEFAULT now()
       )`,
    );

    const files = readdirSync(migrationsDir)
      .filter((f) => f.endsWith('.sql'))
      .sort();

    for (const file of files) {
      const existing = await client.query('SELECT 1 FROM schema_migrations WHERE name = $1', [file]);
      if (existing.rowCount) continue;

      const sql = readFileSync(path.join(migrationsDir, file), 'utf8');
      await client.query('BEGIN');
      try {
        await client.query(sql);
        await client.query('INSERT INTO schema_migrations (name) VALUES ($1)', [file]);
        await client.query('COMMIT');
      } catch (error) {
        await client.query('ROLLBACK');
        throw error;
      }
      applied.push(file);
    }
    return applied;
  } finally {
    // The lock is session-scoped: release it before the connection goes back
    // to the pool, or the next run on this connection would inherit it. A lost
    // connection releases the lock on its own.
    await client.query('SELECT pg_advisory_unlock($1)', [MIGRATIONS_LOCK_KEY]).catch(() => undefined);
    client.release();
  }
}

/** Absolute path of the bundled migrations directory. */
export function defaultMigrationsDir(): string {
  return path.join(__dirname, '..', '..', 'migrations');
}
