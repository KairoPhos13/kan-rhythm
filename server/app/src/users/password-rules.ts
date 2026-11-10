/**
 * Password rules for registration, checked offline (no breach API: nothing
 * about a player's password leaves the server). Length 8 to 200, not the
 * username, and not one of the passwords every credential-stuffing list tries
 * first.
 */

const COMMON = new Set([
  '12345678', '123456789', '1234567890', '0123456789', '87654321', '11111111', '00000000',
  '12341234', '11223344', '88888888', '99999999', '123123123', 'password', 'password1',
  'password123', 'passw0rd', 'p@ssw0rd', 'qwertyui', 'qwerty123', 'qwertyuiop', 'asdfghjk',
  'asdfghjkl', 'zxcvbnm1', '1q2w3e4r', '1qaz2wsx', 'iloveyou', 'abcd1234', 'abc12345',
  'aaaaaaaa', 'letmein1', 'welcome1', 'sunshine', 'princess', 'football', 'baseball',
  'superman', 'starwars', 'trustno1', 'whatever', 'computer', 'contraseña', 'contrasena',
  'contraseña1', 'contrasena1', 'teamo123', 'ocarina1', 'ocarina123', 'kan12345', 'kanrhythm',
]);

/** The problem with this password, or null when it is acceptable. */
export function passwordProblem(password: string, username: string): string | null {
  if (password.length < 8 || password.length > 200) return 'Password must be at least 8 characters';
  const lower = password.toLowerCase();
  if (lower === username.trim().toLowerCase()) return 'Password must not be your username';
  if (COMMON.has(lower)) return 'This password is too common; choose another one';
  if (/^(.)\1+$/.test(password)) return 'This password is too common; choose another one';
  return null;
}
