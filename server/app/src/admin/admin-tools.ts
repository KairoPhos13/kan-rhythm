/**
 * Pure helpers for the admin CLI: command-line parsing and password
 * generation. Nothing here touches the database or the network, so it is
 * unit-testable offline (test/admin-tools.spec.ts).
 */
import { randomInt } from 'node:crypto';

/** Usage text shown when the command line does not parse. */
export const USAGE = [
  'usage: node dist/admin/cli.js <command>',
  '',
  'commands:',
  '  users                          list accounts (id, username, join date, last visit)',
  '  reset-password <username>      set a new random password and sign the player out everywhere',
  '  delete-user <username> --yes   delete an account that owns no beatmap sets',
  '',
  'Quote usernames that contain spaces. The exit code is 0 on success, 1 on any problem.',
].join('\n');

/** Parsed admin command line. */
export type AdminCommand =
  | { kind: 'users' }
  | { kind: 'reset-password'; username: string }
  | { kind: 'delete-user'; username: string };

export type ParseResult =
  | { ok: true; command: AdminCommand }
  | { ok: false; error: string };

/**
 * Parses the arguments after the script name. The username is everything that
 * is not an option, joined with spaces (a username with spaces can be quoted or
 * typed unquoted).
 */
export function parseCommandLine(argv: string[]): ParseResult {
  const [command, ...rest] = argv;
  if (!command) return { ok: false, error: 'no command given' };

  const positionals: string[] = [];
  let confirmed = false;
  for (const arg of rest) {
    if (arg === '--yes') {
      confirmed = true;
    } else if (arg.startsWith('--')) {
      return { ok: false, error: `unknown option "${arg}"` };
    } else {
      positionals.push(arg);
    }
  }
  const username = positionals.join(' ').trim();

  switch (command) {
    case 'users':
      if (positionals.length > 0 || confirmed) {
        return { ok: false, error: 'users takes no arguments' };
      }
      return { ok: true, command: { kind: 'users' } };
    case 'reset-password':
      if (confirmed) return { ok: false, error: 'reset-password takes no options' };
      if (!username) return { ok: false, error: 'reset-password needs a username' };
      return { ok: true, command: { kind: 'reset-password', username } };
    case 'delete-user':
      if (!username) return { ok: false, error: 'delete-user needs a username' };
      if (!confirmed) return { ok: false, error: 'delete-user needs the --yes flag to confirm' };
      return { ok: true, command: { kind: 'delete-user', username } };
    default:
      return { ok: false, error: `unknown command "${command}"` };
  }
}

/**
 * Alphabet for generated passwords: letters and digits only, without the
 * look-alikes (0/O, 1/l/I), so the owner can read a password out to a player
 * without ambiguity.
 */
export const PASSWORD_ALPHABET = '23456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz';

/** Generated passwords are 20 characters, comfortably above the 16 minimum. */
export const GENERATED_PASSWORD_LENGTH = 20;

/**
 * A random password drawn with `crypto.randomInt` (uniform, rejection-free).
 * 20 characters from a 56-symbol alphabet is about 115 bits of entropy.
 */
export function generatePassword(length: number = GENERATED_PASSWORD_LENGTH): string {
  if (!Number.isInteger(length) || length < 16) {
    throw new Error('generated passwords must be at least 16 characters');
  }
  let password = '';
  for (let i = 0; i < length; i++) {
    password += PASSWORD_ALPHABET[randomInt(PASSWORD_ALPHABET.length)];
  }
  return password;
}
