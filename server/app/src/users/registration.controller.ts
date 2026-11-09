/**
 * `POST /users` — registration exactly as the client sends it
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
