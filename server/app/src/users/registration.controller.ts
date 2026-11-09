/**
 * `POST /users` — registration exactly as the client sends it
 * (server/docs/lazer-protocol.md §2): form fields `user[username]`,
 * `user[user_email]`, `user[password]`; validation failures answer with the
 * `form_error` shape the client parses.
 */
import { Body, Controller, Post, Req } from '@nestjs/common';
import { Request } from 'express';
import { apiError } from '../common/http';
import { RateLimiter } from '../common/rate-limiter';
import { UsersService } from './users.service';

@Controller('users')
export class RegistrationController {
  /** Simple in-memory limit on registration attempts (per IP). */
  private readonly registrationLimiter = new RateLimiter(60 * 60_000, 5);

  constructor(private readonly users: UsersService) {}

  @Post()
  async register(@Body() body: unknown, @Req() req: Request): Promise<Record<string, unknown>> {
    if (!this.registrationLimiter.check(req.ip ?? 'unknown')) {
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
