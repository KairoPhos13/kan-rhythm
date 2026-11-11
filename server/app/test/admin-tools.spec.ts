/** Unit tests for the admin CLI's pure helpers (no database). */
import {
  generatePassword,
  parseCommandLine,
  PASSWORD_ALPHABET,
  USAGE,
} from '../src/admin/admin-tools';
import { passwordProblem } from '../src/users/password-rules';

describe('parseCommandLine', () => {
  it('parses the users command', () => {
    expect(parseCommandLine(['users'])).toEqual({ ok: true, command: { kind: 'users' } });
  });

  it('parses reset-password with the username', () => {
    expect(parseCommandLine(['reset-password', 'smoketest'])).toEqual({
      ok: true,
      command: { kind: 'reset-password', username: 'smoketest' },
    });
  });

  it('joins an unquoted username that contains spaces and trims it', () => {
    expect(parseCommandLine(['reset-password', 'some', 'player'])).toEqual({
      ok: true,
      command: { kind: 'reset-password', username: 'some player' },
    });
    expect(parseCommandLine(['reset-password', 'smoketest '])).toEqual({
      ok: true,
      command: { kind: 'reset-password', username: 'smoketest' },
    });
  });

  it('parses delete-user with --yes in any position', () => {
    expect(parseCommandLine(['delete-user', 'smoketest', '--yes'])).toEqual({
      ok: true,
      command: { kind: 'delete-user', username: 'smoketest' },
    });
    expect(parseCommandLine(['delete-user', '--yes', 'smoketest'])).toEqual({
      ok: true,
      command: { kind: 'delete-user', username: 'smoketest' },
    });
  });

  it('rejects a missing or unknown command', () => {
    expect(parseCommandLine([])).toEqual({ ok: false, error: 'no command given' });
    expect(parseCommandLine(['ban-user', 'smoketest'])).toEqual({
      ok: false,
      error: 'unknown command "ban-user"',
    });
  });

  it('rejects arguments for users and options for reset-password', () => {
    expect(parseCommandLine(['users', 'smoketest']).ok).toBe(false);
    expect(parseCommandLine(['users', '--yes']).ok).toBe(false);
    expect(parseCommandLine(['reset-password', 'smoketest', '--yes']).ok).toBe(false);
    expect(parseCommandLine(['reset-password']).ok).toBe(false);
  });

  it('rejects unknown options', () => {
    const result = parseCommandLine(['delete-user', 'smoketest', '--force']);
    expect(result).toEqual({ ok: false, error: 'unknown option "--force"' });
  });

  it('requires the --yes confirmation for delete-user', () => {
    expect(parseCommandLine(['delete-user', 'smoketest'])).toEqual({
      ok: false,
      error: 'delete-user needs the --yes flag to confirm',
    });
    expect(parseCommandLine(['delete-user', '--yes'])).toEqual({
      ok: false,
      error: 'delete-user needs a username',
    });
  });

  it('parses the levels command', () => {
    expect(parseCommandLine(['levels'])).toEqual({ ok: true, command: { kind: 'levels' } });
    expect(parseCommandLine(['levels', 'smoketest']).ok).toBe(false);
    expect(parseCommandLine(['levels', '--yes']).ok).toBe(false);
  });

  it('parses set-status with a level id and a status', () => {
    expect(parseCommandLine(['set-status', '12', 'rank'])).toEqual({
      ok: true,
      command: { kind: 'set-status', levelId: 12, status: 'rank' },
    });
    for (const status of ['rank', 'pending', 'wip', 'graveyard', 'loved']) {
      expect(parseCommandLine(['set-status', '7', status]).ok).toBe(true);
    }
  });

  it('rejects a set-status without a level id or with an unknown status', () => {
    expect(parseCommandLine(['set-status', '12'])).toEqual({
      ok: false,
      error: 'set-status needs a level id and a status',
    });
    expect(parseCommandLine(['set-status', 'twelve', 'rank'])).toEqual({
      ok: false,
      error: '"twelve" is not a level id',
    });
    expect(parseCommandLine(['set-status', '12', 'approved'])).toEqual({
      ok: false,
      error: 'status must be one of rank, pending, wip, graveyard, loved',
    });
  });

  it('requires the --yes confirmation for delete-level', () => {
    expect(parseCommandLine(['delete-level', '3'])).toEqual({
      ok: false,
      error: 'delete-level needs the --yes flag to confirm',
    });
    expect(parseCommandLine(['delete-level', '--yes'])).toEqual({
      ok: false,
      error: 'delete-level needs a level id',
    });
    expect(parseCommandLine(['delete-level', '3', '--yes'])).toEqual({
      ok: true,
      command: { kind: 'delete-level', levelId: 3 },
    });
  });

  it('describes every command in the usage text', () => {
    expect(USAGE).toContain('users');
    expect(USAGE).toContain('reset-password <username>');
    expect(USAGE).toContain('delete-user <username> --yes');
    expect(USAGE).toContain('levels');
    expect(USAGE).toContain('set-status <id> <status>');
    expect(USAGE).toContain('delete-level <id> --yes');
  });
});

describe('generatePassword', () => {
  it('generates 20 characters by default, above the 16 minimum', () => {
    const password = generatePassword();
    expect(password).toHaveLength(20);
    expect(password.length).toBeGreaterThanOrEqual(16);
  });

  it('draws every character from the readable alphabet', () => {
    const password = generatePassword();
    expect(password).toMatch(new RegExp(`^[${PASSWORD_ALPHABET}]+$`));
  });

  it('keeps look-alike characters out of the alphabet', () => {
    expect(PASSWORD_ALPHABET).not.toMatch(/[0O1Il]/);
  });

  it('honours a requested length and refuses too-short ones', () => {
    expect(generatePassword(24)).toHaveLength(24);
    expect(() => generatePassword(15)).toThrow('16 characters');
    expect(() => generatePassword(16.5)).toThrow('16 characters');
  });

  it('does not repeat itself', () => {
    const passwords = new Set<string>();
    for (let i = 0; i < 50; i++) passwords.add(generatePassword());
    expect(passwords.size).toBe(50);
  });

  it('passes the registration password rules', () => {
    expect(passwordProblem(generatePassword(), 'smoketest')).toBeNull();
  });
});
