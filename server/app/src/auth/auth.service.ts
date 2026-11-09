/**
 * OAuth token endpoint and bearer-token verification
 * (server/docs/lazer-protocol.md §1).
 */
import { Inject, Injectable } from '@nestjs/common';
import argon2 from 'argon2';
import { QueryResultRow } from 'pg';
import { APP_CONFIG, AppConfig } from '../config';
import { oauthError } from '../common/http';
import { DbService } from '../db/db.service';
import { UserRow } from '../api/serializers';
import { generateToken, hashToken } from './tokens';

export interface IssuedTokens {
  access_token: string;
  expires_in: number;
  refresh_token: string;
  token_type: string;
  scope: string;
}

interface TokenRow extends QueryResultRow {
  user_id: number;
  access_expires_at: Date;
  refresh_expires_at: Date;
}

export interface AuthenticatedUser extends UserRow {
  password_hash: string;
}

@Injectable()
export class AuthService {
  constructor(
    private readonly db: DbService,
    @Inject(APP_CONFIG) private readonly config: AppConfig,
  ) {}

  /** Verifies the configured OAuth client credentials sent by the client. */
  verifyClient(clientId: string | undefined, clientSecret: string | undefined): void {
    const expected = this.config.oauth;
    if (!expected.clientSecret) {
      throw oauthError(500, 'server_error', 'The server OAuth client secret is not configured');
    }
    if (clientId !== expected.clientId || clientSecret !== expected.clientSecret) {
      throw oauthError(400, 'invalid_client', 'Unknown OAuth client');
    }
  }

  /** `grant_type=password`. Throws OAuth-shaped errors on failure. */
  async passwordGrant(username: string, password: string): Promise<IssuedTokens> {
    const result = await this.db.query<AuthenticatedUser>(
      `SELECT id, username, country_code, join_date, last_visit, title, is_supporter,
              avatar_url, cover_url, password_hash
         FROM users
        WHERE username_lower = lower($1)`,
      [username],
    );
    const user = result.rows[0];
    // Same message for "unknown user" and "wrong password" (no user enumeration).
    if (!user) throw oauthError(400, 'invalid_grant', 'Incorrect username or password');
    let ok: boolean;
    try {
      ok = await argon2.verify(user.password_hash, password);
    } catch {
      ok = false;
    }
    if (!ok) throw oauthError(400, 'invalid_grant', 'Incorrect username or password');
    return this.issueTokens(user.id);
  }

  /** `grant_type=refresh_token`; the refresh token is rotated on use. */
  async refreshGrant(refreshToken: string): Promise<IssuedTokens> {
    const hash = hashToken(refreshToken);
    const result = await this.db.query<TokenRow>(
      `SELECT user_id, access_expires_at, refresh_expires_at
         FROM oauth_tokens
        WHERE refresh_token_hash = $1`,
      [hash],
    );
    const token = result.rows[0];
    if (!token || token.refresh_expires_at.getTime() <= Date.now()) {
      throw oauthError(400, 'invalid_grant', 'Invalid or expired refresh token');
    }
    await this.db.query('DELETE FROM oauth_tokens WHERE refresh_token_hash = $1', [hash]);
    return this.issueTokens(token.user_id);
  }

  /** Issues a fresh access/refresh token pair for a user. */
  async issueTokens(userId: number): Promise<IssuedTokens> {
    const access = generateToken();
    const refresh = generateToken();
    const { oauth } = this.config;
    await this.db.query(
      `INSERT INTO oauth_tokens
         (user_id, access_token_hash, refresh_token_hash, access_expires_at, refresh_expires_at)
       VALUES ($1, $2, $3, now() + ($4 || ' seconds')::interval, now() + ($5 || ' seconds')::interval)`,
      [userId, hashToken(access), hashToken(refresh), String(oauth.accessTokenTtlSeconds), String(oauth.refreshTokenTtlSeconds)],
    );
    return {
      access_token: access,
      expires_in: oauth.accessTokenTtlSeconds,
      refresh_token: refresh,
      token_type: 'Bearer',
      scope: '*',
    };
  }

  /**
   * Resolves a bearer token to its user, or `null` when the token is missing,
   * unknown or expired. A 401 is only returned for genuinely invalid tokens:
   * any 401 logs the client out (`lazer-protocol.md` §0).
   */
  async authenticate(token: string | undefined): Promise<AuthenticatedUser | null> {
    if (!token) return null;
    const result = await this.db.query<AuthenticatedUser>(
      `SELECT u.id, u.username, u.country_code, u.join_date, u.last_visit, u.title,
              u.is_supporter, u.avatar_url, u.cover_url, u.password_hash
         FROM oauth_tokens t
         JOIN users u ON u.id = t.user_id
        WHERE t.access_token_hash = $1
          AND t.access_expires_at > now()`,
      [hashToken(token)],
    );
    return result.rows[0] ?? null;
  }
}
