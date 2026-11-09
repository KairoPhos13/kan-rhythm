/** CLI: `npm run import -- <file.osz> [--owner <username>]`. */
import 'dotenv/config';
import { loadConfig } from '../config';
import { importOsz } from '../beatmaps/importer';
import { createPool } from '../db/db.service';
import { createStorage } from '../storage/create-storage';

function usage(): void {
  console.error('usage: npm run import -- <file.osz> [--owner <username>]');
}

async function main(): Promise<void> {
  const args = process.argv.slice(2);
  const file = args.find((arg) => !arg.startsWith('--'));
  const ownerIndex = args.indexOf('--owner');
  const owner = ownerIndex >= 0 ? args[ownerIndex + 1] : undefined;

  if (!file || (ownerIndex >= 0 && !owner)) {
    usage();
    process.exitCode = 2;
    return;
  }

  const config = loadConfig();
  const pool = createPool(config.databaseUrl);
  const storage = createStorage(config);
  try {
    const result = await importOsz({ db: pool, storage, log: console.log }, file, {
      ownerUsername: owner,
    });
    const verb = result.created ? 'imported' : 'updated';
    console.log(
      `${verb} beatmapset ${result.setId} (${result.beatmaps.length} difficulties) as ${result.archiveKey}`,
    );
    for (const beatmap of result.beatmaps) {
      console.log(`  beatmap ${beatmap.id} "${beatmap.version}" md5=${beatmap.checksum}`);
    }
    if (result.coverKey) console.log(`  cover: ${result.coverKey}`);
  } finally {
    await pool.end();
  }
}

main().catch((error) => {
  console.error('import: failed:', error instanceof Error ? error.message : error);
  process.exitCode = 1;
});
