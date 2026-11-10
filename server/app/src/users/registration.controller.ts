/**
 * `POST /users`: registration exactly as the client sends it
 * (server/docs/lazer-protocol.md §2): form fields `user[username]`,
 * `user[user_email]`, `user[password]`; validation failures answer with the
 * `form_error` shape the client parses.
 */
import { Body, Controller, Inject, Post, Req } from '@nestjs/common';
import { Request } from 'express';
import { APP_CONFIG, AppConfig } from '../config';
import { apiError } from '../common/http';
import { requestClientIp } from '../common/client-ip';
import { RateLimiter } from '../common/rate-limiter';
import { UsersService } from './users.service';

const DAILY_ACCOUNT_LIMIT = 200;

@Controller('users')
export class RegistrationController {
  /** Simple in-memory limit on registration attempts (per client IP). */
  private readonly registrationLimiter = new RateLimiter(60 * 60_000, 5);

  constructor(
    private readonly users: UsersService,
    @Inject(APP_CONFIG) private readonly config: AppConfig,
  ) {}

  @Post()
  async register(@Body() body: unknown, @Req() req: Request): Promise<Record<string, unknown>> {
    if (!this.registrationLimiter.check(requestClientIp(req, this.config.trustProxy))) {
      throw apiError(429, 'Too many registration attempts, please try again later');
    }
    // A ceiling on accounts created in the last 24 h for the whole server,
    // whatever the IP: a bot spread over many addresses cannot fill the
    // database. Only created accounts count, so failed attempts cannot lock
    // real players out. Generous for a small community; raise it as kan! grows.
    if ((await this.users.countJoinedSince(24)) >= DAILY_ACCOUNT_LIMIT) {
      throw apiError(429, 'kan! has had many new accounts today; please try again tomorrow');
    }

    const input = readRegistrationFields(body);
    const user = await this.users.register(input);
    return { id: user.id, username: user.username };
  }
}

/** Accepts both nested (`user[username]`) and flat parsed bodies. */
export function readRegistrationFields(body: unknown): {
  username: string;
  email: string;
  password: string;
} {
  const record = (body ?? {}) as Record<string, unknown>;
  const nested = record['user'];
  const source: Record<string, unknown> =
    nested && typeof nested === 'object' && !Array.isArray(nested)
      ? (nested as Record<string, unknown>)
      : record;
  const pick = (...names: string[]): string => {
    for (const name of names) {
      const value = source[name];
      if (typeof value === 'string') return value;
      if (Array.isArray(value) && typeof value[0] === 'string') return value[0];
    }
    return '';
  };
  return {
    username: pick('username', 'user[username]'),
    email: pick('user_email', 'user[user_email]'),
    password: pick('password', 'user[password]'),
  };
}
