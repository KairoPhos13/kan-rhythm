/**
 * `POST /oauth/token` — the only authentication endpoint the client uses
 * (server/docs/lazer-protocol.md §1). Password + refresh_token grants,
 * form-encoded bodies.
 */
import { Body, Controller, Post, Req } from '@nestjs/common';
import { Request } from 'express';
import { bodyField, oauthError } from '../common/http';
import { RateLimiter } from '../common/rate-limiter';
import { AuthService, IssuedTokens } from './auth.service';

@Controller()
export class OAuthController {
  /** Simple in-memory limit on login attempts (per IP). */
  private readonly loginLimiter = new RateLimiter(60_000, 10);

  constructor(private readonly auth: AuthService) {}

  @Post('oauth/token')
  async token(@Body() body: unknown, @Req() req: Request): Promise<IssuedTokens> {
    if (!this.loginLimiter.check(req.ip ?? 'unknown')) {
      throw oauthError(429, 'too_many_requests', 'Too many attempts, please try again shortly');
    }

    const clientId = bodyField(body, 'client_id');
    const clientSecret = bodyField(body, 'client_secret');
    this.auth.verifyClient(clientId, clientSecret);

    const grantType = bodyField(body, 'grant_type');
    switch (grantType) {
      case 'password': {
        const username = bodyField(body, 'username');
        const password = bodyField(body, 'password');
        if (!username || !password) {
          throw oauthError(400, 'invalid_request', 'Username and password are required');
        }
        return this.auth.passwordGrant(username, password);
      }
      case 'refresh_token': {
        const refreshToken = bodyField(body, 'refresh_token');
        if (!refreshToken) {
          throw oauthError(400, 'invalid_request', 'A refresh token is required');
        }
        return this.auth.refreshGrant(refreshToken);
      }
      default:
        throw oauthError(400, 'unsupported_grant_type', 'Unsupported grant type');
    }
  }
}
