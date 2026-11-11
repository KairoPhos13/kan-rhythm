/**
 * CLI: `node dist/admin/cli.js <command>`, admin tools for the server owner.
 *
 * kan! sends no email, so a player who forgets their password asks the owner
 * (the website's "Forgot your password?" note); this CLI is how the owner
 * handles that without raw SQL. It prints account names, level titles and
 * dates only: never email addresses, password hashes or tokens.
 */
import 'dotenv/config';
import argon2 from 'argon2';
import type { Pool } from 'pg';
import { loadConfig } from '../config';
import { createPool } from '../db/db.service';
import { createStorage } from '../storage/create-storage';
import { statusName } from '../beatmaps/status';
import { passwordProblem } from '../users/password-rules';
import { generatePassword, parseCommandLine, USAGE } from './admin-tools';
import { deleteLevel, LevelSummary, listLevels, setLevelStatus } from './level-admin';

/** Dates are shown as YYYY-MM-DD; a missing date as "-". */
function day(value: Date | null): string {
  return value ? value.toISOString().slice(0, 10) : '-';
}

function fail(message: string): void {
  console.error(`admin: ${message}`);
  process.exitCode = 1;
}

/** `users`: id, username, join date, last visit. No emails, no hashes. */
async function listUsers(pool: Pool): Promise<void> {
  const result = await pool.query<{
    id: number;
    username: string;
    join_date: Date;
    last_visit: Date | null;
  }>(`SELECT id, username, join_date, last_visit FROM users ORDER BY id`);
  if (result.rows.length === 0) {
    console.log('no users');
    return;
  }
  console.log('id   username                  joined      last visit');
  for (const row of result.rows) {
    const id = String(row.id).padEnd(4);
    const name = row.username.padEnd(25);
    console.log(`${id} ${name} ${day(row.join_date)}  ${day(row.last_visit)}`);
  }
  console.log(`${result.rows.length} user(s)`);
}

/**
 * `reset-password`: a fresh random password, hashed exactly like
 * `UsersService.register`, and every OAuth token of the player revoked so all
 * clients must sign in again.
 */
async function resetPassword(pool: Pool, username: string): Promise<void> {
  const found = await pool.query<{ id: number }>(
    `SELECT id FROM users WHERE username_lower = lower($1)`,
    [username],
  );
  const user = found.rows[0];
  if (!user) {
    fail(`no user named "${username}"`);
    return;
  }

  // A generated password is random, so it never trips the registration rules;
  // checked anyway so the two code paths stay in agreement.
  let password = generatePassword();
  for (let attempt = 0; attempt < 8 && passwordProblem(password, username) !== null; attempt++) {
    password = generatePassword();
  }

  const passwordHash = await argon2.hash(password, { type: argon2.argon2id });
  await pool.query(`UPDATE users SET password_hash = $1 WHERE id = $2`, [passwordHash, user.id]);
  const revoked = await pool.query(`DELETE FROM oauth_tokens WHERE user_id = $1`, [user.id]);

  console.log(`new password for ${username} (id ${user.id}):`);
  console.log(`  ${password}`);
  console.log(`revoked ${revoked.rowCount ?? 0} OAuth token(s); the player must sign in again.`);
  console.log('The password is shown only this once. kan! has no change-password screen,');
  console.log('so give it to the player privately (a direct message, never the public issue)');
  console.log('and tell them to keep it safe. Running reset-password again is how it changes later.');
}

/**
 * `delete-user`: the account and everything that references it (scores, score
 * tokens, OAuth tokens) in one transaction. Refused while the player owns beatmap
 * sets: the schema could leave them without an owner (`ON DELETE SET NULL`) but
 * ownerless sets have no place in the catalog.
 */
async function deleteUser(pool: Pool, username: string): Promise<void> {
  const found = await pool.query<{ id: number }>(
    `SELECT id FROM users WHERE username_lower = lower($1)`,
    [username],
  );
  const user = found.rows[0];
  if (!user) {
    fail(`no user named "${username}"`);
    return;
  }

  const sets = await pool.query<{ n: string }>(
    `SELECT count(*) AS n FROM beatmapsets WHERE user_id = $1`,
    [user.id],
  );
  const owned = Number(sets.rows[0]?.n ?? 0);
  if (owned > 0) {
    fail(`"${username}" owns ${owned} beatmap set(s); refusing to delete`);
    console.error('The database would leave those sets without an owner, which we do not allow.');
    console.error('Move them to another account first (UPDATE beatmapsets SET user_id = ...)');
    console.error('or delete them with delete-level <id> --yes, then run delete-user again.');
    return;
  }

  const client = await pool.connect();
  try {
    await client.query('BEGIN');
    const scores = await client.query(`DELETE FROM scores WHERE user_id = $1`, [user.id]);
    const scoreTokens = await client.query(`DELETE FROM score_tokens WHERE user_id = $1`, [user.id]);
    const oauthTokens = await client.query(`DELETE FROM oauth_tokens WHERE user_id = $1`, [user.id]);
    await client.query(`DELETE FROM users WHERE id = $1`, [user.id]);
    await client.query('COMMIT');
    console.log(
      `deleted ${username} (id ${user.id}): ${scores.rowCount ?? 0} score(s), ` +
        `${scoreTokens.rowCount ?? 0} score token(s), ${oauthTokens.rowCount ?? 0} OAuth token(s)`,
    );
  } catch (error) {
    await client.query('ROLLBACK');
    throw error;
  } finally {
    client.release();
  }
}

/**
 * `levels`: id, title, creator and status of every level, in catalogue order.
 * Titles only: the listing exists to find a level again, not to browse it.
 */
function printLevels(levels: LevelSummary[]): void {
  if (levels.length === 0) {
    console.log('no levels');
    return;
  }
  console.log('id   status     creator                  title');
  for (const level of levels) {
    const id = String(level.id).padEnd(4);
    const status = statusName(level.status).padEnd(10);
    const creator = level.creator.padEnd(24);
    console.log(`${id} ${status} ${creator} ${level.title}`);
  }
  console.log(`${levels.length} level(s)`);
}

async function main(): Promise<void> {
  const parsed = parseCommandLine(process.argv.slice(2));
  if (!parsed.ok) {
    console.error(`admin: ${parsed.error}`);
    console.error(USAGE);
    process.exitCode = 1;
    return;
  }

  const config = loadConfig();
  const pool = createPool(config.databaseUrl);
  const storage = createStorage(config);
  const command = parsed.command;
  try {
    switch (command.kind) {
      case 'users':
        await listUsers(pool);
        break;
      case 'reset-password':
        await resetPassword(pool, command.username);
        break;
      case 'delete-user':
        await deleteUser(pool, command.username);
        break;
      case 'levels':
        printLevels(await listLevels(pool));
        break;
      case 'set-status': {
        const level = await setLevelStatus(pool, command.levelId, command.status);
        if (!level) {
          fail(`no level with id ${command.levelId}`);
          break;
        }
        console.log(`level ${level.id} "${level.title}" is now ${statusName(level.status)}`);
        break;
      }
      case 'delete-level': {
        const level = await deleteLevel(pool, storage, command.levelId);
        if (!level) {
          fail(`no level with id ${command.levelId}`);
          break;
        }
        console.log(`deleted level ${level.id} "${level.title}"; its package and cover are gone`);
        break;
      }
    }
  } finally {
    await pool.end();
  }
}

main().catch((error) => {
  console.error('admin: failed:', error instanceof Error ? error.message : error);
  process.exitCode = 1;
});
