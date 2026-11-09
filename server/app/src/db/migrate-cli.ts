/** CLI: `npm run migrate` — applies pending SQL migrations. */
import 'dotenv/config';
import { loadConfig } from '../config';
import { createPool } from './db.service';
import { defaultMigrationsDir, runMigrations } from './migrate';

async function main(): Promise<void> {
  const config = loadConfig();
  const pool = createPool(config.databaseUrl);
  try {
    const applied = await runMigrations(pool, defaultMigrationsDir());
    if (applied.length === 0) {
      console.log('migrate: database is up to date');
    } else {
      console.log(`migrate: applied ${applied.length} migration(s): ${applied.join(', ')}`);
    }
  } finally {
    await pool.end();
  }
}

main().catch((error) => {
  console.error('migrate: failed:', error instanceof Error ? error.message : error);
  process.exitCode = 1;
});
