/**
 * Unit tests for the sign-in path (audit S10): the login endpoint answers the
 * same error in the same time whether the account does not exist or the
 * password is wrong. "Same time" is asserted as "same work": argon2 runs in
 * both cases (against a placeholder hash for unknown accounts).
 */
import argon2 from 'argon2';
import { HttpException } from '@nestjs/common';
import { AuthService } from '../src/auth/auth.service';
import { DbService } from '../src/db/db.service';
import { loadConfig } from '../src/config';

const config = loadConfig({});

/** The db stub: no user rows unless a test says otherwise. */
function dbReturning(rows: Record<string, unknown>[]): DbService {
  return {
    query: jest.fn().mockResolvedValue({ rows, rowCount: rows.length }),
  } as unknown as DbService;
}

async function errorOf(promise: Promise<unknown>): Promise<Record<string, unknown>> {
  try {
    await promise;
  } catch (error) {
    expect(error).toBeInstanceOf(HttpException);
    return (error as HttpException).getResponse() as Record<string, unknown>;
  }
  throw new Error('passwordGrant resolved; it must refuse');
}

describe('password sign-in', () => {
  it('runs argon2 for unknown users too and answers like a wrong password', async () => {
    const verify = jest.spyOn(argon2, 'verify');
    const auth = new AuthService(dbReturning([]), config);

    const unknown = await errorOf(auth.passwordGrant('Nobody', 'a guess'));
    expect(verify).toHaveBeenCalledTimes(1);

    const wrong = await errorOf(auth.passwordGrant('Nobody', 'a guess'));
    // Identical body: the message cannot tell the two cases apart.
    expect(wrong).toEqual(unknown);
    expect(unknown).toMatchObject({ error: 'invalid_grant', hint: 'Incorrect username or password' });
    // ... and the same amount of argon2 work in both cases.
    expect(verify).toHaveBeenCalledTimes(2);
  });

  it('answers the same way for a known user with the wrong password', async () => {
    const hash = await argon2.hash('the real password', { type: argon2.argon2id });
    const auth = new AuthService(
      dbReturning([
        {
          id: 2,
          username: 'KairoPhos',
          country_code: 'XX',
          join_date: new Date(),
          last_visit: null,
          title: '',
          is_supporter: false,
          avatar_url: '',
          cover_url: '',
          password_hash: hash,
        },
      ]),
      config,
    );

    const wrong = await errorOf(auth.passwordGrant('KairoPhos', 'a guess'));
    expect(wrong).toMatchObject({ hint: 'Incorrect username or password' });

    // The right password signs in (tokens land in the stub db).
    const tokens = await auth.passwordGrant('KairoPhos', 'the real password');
    expect(tokens.token_type).toBe('Bearer');
    expect(tokens.access_token).toBeTruthy();
    expect(tokens.refresh_token).toBeTruthy();
  });
});
