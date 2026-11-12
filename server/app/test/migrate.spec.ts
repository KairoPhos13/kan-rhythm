/**
 * Migration runner tests (audit S12): two instances starting together must
 * queue on the advisory lock and apply every migration exactly once between
 * them, and a second run must find nothing left to do.
 */
import path from 'node:path';
import { Pool } from 'pg';
import { createPool } from '../src/db/db.service';
import { runMigrations } from '../src/db/migrate';

const TEST_DATABASE_URL =
  process.env.TEST_DATABASE_URL ?? 'postgres://kan:kan@localhost:55432/kan_test';
/** Its own database, on the server TEST_DATABASE_URL points at. */
const MIGRATE_DATABASE_URL = (() => {
  const url = new URL(TEST_DATABASE_URL);
  url.pathname = '/kan_migrate_test';
  return url.toString();
})();

jest.setTimeout(120_000);

describe('migrations', () => {
  const pools: Pool[] = [];

  const poolFor = (): Pool => {
    const pool = createPool(MIGRATE_DATABASE_URL);
    pools.push(pool);
    return pool;
  };

  beforeAll(async () => {
    const adminUrl = new URL(MIGRATE_DATABASE_URL);
    const dbName = adminUrl.pathname.replace(/^\//, '');
    adminUrl.pathname = '/postgres';
    const admin = createPool(adminUrl.toString());
    await admin.query(`DROP DATABASE IF EXISTS ${dbName} WITH (FORCE)`);
    await admin.query(`CREATE DATABASE ${dbName}`);
    await admin.end();
  });

  afterAll(async () => {
    await Promise.all(pools.map((pool) => pool.end()));
  });

  it('applies every migration exactly once across two concurrent runs', async () => {
    const migrationsDir = path.join(__dirname, '..', 'migrations');

    // Two instances booting at the same time (a container restart while the
    // other is still migrating): the advisory lock makes them take turns.
    const [first, second] = await Promise.all([
      runMigrations(poolFor(), migrationsDir),
      runMigrations(poolFor(), migrationsDir),
    ]);

    const applied = [...first, ...second];
    expect(applied.length).toBeGreaterThan(0);
    expect(new Set(applied).size).toBe(applied.length);

    const recorded = await poolFor().query<{ name: string }>(
      'SELECT name FROM schema_migrations ORDER BY name',
    );
    expect(recorded.rows.map((row) => row.name)).toEqual([...applied].sort());
  });

  it('has nothing left to do on a second run', async () => {
    const applied = await runMigrations(poolFor(), path.join(__dirname, '..', 'migrations'));
    expect(applied).toEqual([]);
  });
});
