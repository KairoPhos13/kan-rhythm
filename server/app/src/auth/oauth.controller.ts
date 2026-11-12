/**
 * `POST /oauth/token`: the only authentication endpoint the client uses
 * (server/docs/lazer-protocol.md §1). Password + refresh_token grants,
 * form-encoded bodies.
 */
import { Body, Controller, HttpCode, Inject, Post, Req } from '@nestjs/common';
import { Request } from 'express';
import { APP_CONFIG, AppConfig } from '../config';
import { bodyField, oauthError } from '../common/http';
import { requestClientIp } from '../common/client-ip';
import { RateLimiter } from '../common/rate-limiter';
import { AuthService, IssuedTokens } from './auth.service';

@Controller()
export class OAuthController {
  /** Simple in-memory limit on token requests (per client IP). */
  private readonly loginLimiter = new RateLimiter(60_000, 10);
  /**
   * The same limit per account (audit S4): without it, one account's password
   * could be guessed from many addresses at once, and the per-IP limit alone
   * does not stop that.
   */
  private readonly accountLimiter = new RateLimiter(60_000, 10);

  constructor(
    private readonly auth: AuthService,
    @Inject(APP_CONFIG) private readonly config: AppConfig,
  ) {}

  @Post('oauth/token')
  @HttpCode(200)
  async token(@Body() body: unknown, @Req() req: Request): Promise<IssuedTokens> {
    if (!this.loginLimiter.check(requestClientIp(req, this.config.trustProxy))) {
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
        if (!this.accountLimiter.check(username.toLowerCase())) {
          throw oauthError(
            429,
            'too_many_requests',
            'Too many sign-in attempts for this account, please try again shortly',
          );
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
