/** Unit test for the offline password rules used at registration. */
import { passwordProblem } from '../src/users/password-rules';

describe('password rules', () => {
  it('accepts a long, personal password', () => {
    expect(passwordProblem('a line from my favourite song', 'KairoPhos')).toBeNull();
  });

  it('rejects short and overlong passwords', () => {
    expect(passwordProblem('short', 'someone')).toContain('8 characters');
    expect(passwordProblem('x'.repeat(201), 'someone')).toContain('8 characters');
  });

  it('rejects the username as password', () => {
    expect(passwordProblem('KairoPhos', 'kairophos')).toContain('username');
  });

  it('rejects common passwords and one repeated character', () => {
    expect(passwordProblem('Password123', 'someone')).toContain('too common');
    expect(passwordProblem('12345678', 'someone')).toContain('too common');
    expect(passwordProblem('zzzzzzzzzz', 'someone')).toContain('too common');
  });
});
