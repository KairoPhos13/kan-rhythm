/**
 * Registration and profile lookups (server/docs/lazer-protocol.md §2-§3).
 */
import { HttpException, Injectable } from '@nestjs/common';
import argon2 from 'argon2';
import { DbService } from '../db/db.service';
import { UserRow, UserStatisticsSummary, emptyStatistics } from '../api/serializers';

export interface RegistrationInput {
  username: string;
  email: string;
  password: string;
}

const USERNAME_PATTERN = /^[A-Za-z0-9_[\] -]+$/;
const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

/**
 * Registration validation errors in the shape the client parses
 * (`APIAccess.cs:523` → `RegistrationRequest.RegistrationRequestErrors`).
 */
export function formError(fieldErrors: {
  username?: string[];
  user_email?: string[];
  password?: string[];
}): HttpException {
  const user: Record<string, string[]> = {};
  if (fieldErrors.username?.length) user['username'] = fieldErrors.username;
  if (fieldErrors.user_email?.length) user['user_email'] = fieldErrors.user_email;
  if (fieldErrors.password?.length) user['password'] = fieldErrors.password;
  return new HttpException({ form_error: { user } }, 422);
}

@Injectable()
export class UsersService {
  constructor(private readonly db: DbService) {}

  /** `POST /users` — validation happens before anything is written. */
  async register(input: RegistrationInput): Promise<UserRow> {
    const errors: { username?: string[]; user_email?: string[]; password?: string[] } = {};
    const username = (input.username ?? '').trim();
    const email = (input.email ?? '').trim();
    const password = input.password ?? '';

    if (username.length < 3 || username.length > 25) {
      errors.username = ['Username must be between 3 and 25 characters'];
    } else if (!USERNAME_PATTERN.test(username)) {
      errors.username = ['Username may only contain letters, numbers, spaces, _, - and []'];
    }
    if (!EMAIL_PATTERN.test(email) || email.length > 254) {
      errors.user_email = ['Email address is not valid'];
    }
    if (password.length < 8 || password.length > 200) {
      errors.password = ['Password must be at least 8 characters'];
    }

    if (!errors.username) {
      const takenName = await this.db.query(`SELECT 1 FROM users WHERE username_lower = lower($1)`, [
        username,
      ]);
      if (takenName.rows.length > 0) errors.username = ['Username has already been taken'];
    }
    if (!errors.user_email) {
      const takenEmail = await this.db.query(`SELECT 1 FROM users WHERE email_lower = lower($1)`, [
        email,
      ]);
      if (takenEmail.rows.length > 0) errors.user_email = ['Email address has already been taken'];
    }

    if (errors.username || errors.user_email || errors.password) throw formError(errors);

    const passwordHash = await argon2.hash(password, { type: argon2.argon2id });
    const result = await this.db.query<UserRow>(
      `INSERT INTO users (username, email, password_hash)
       VALUES ($1, $2, $3)
       RETURNING id, username, country_code, join_date, last_visit, title, is_supporter,
                 avatar_url, cover_url`,
      [username, email, passwordHash],
    );
    return result.rows[0];
  }

  findById(id: number): Promise<UserRow | null> {
    return this.one(`WHERE id = $1`, [id]);
  }

  findByUsername(username: string): Promise<UserRow | null> {
    return this.one(`WHERE username_lower = lower($1)`, [username]);
  }

  private async one(where: string, params: unknown[]): Promise<UserRow | null> {
    const result = await this.db.query<UserRow>(
      `SELECT id, username, country_code, join_date, last_visit, title, is_supporter,
              avatar_url, cover_url
         FROM users ${where}`,
      params,
    );
    return result.rows[0] ?? null;
  }

  /** Aggregates the profile statistics block from the user's scores. */
  async statisticsFor(userId: number): Promise<UserStatisticsSummary> {
    const result = await this.db.query<{
      play_count: number;
      total_score: string;
      hit_accuracy: number;
      max_combo: number;
      ssh: number;
      ss: number;
      sh: number;
      s: number;
      a: number;
      best_count: number;
    }>(
      `SELECT count(*)::int AS play_count,
              coalesce(sum(total_score), 0)::bigint AS total_score,
              coalesce(avg(accuracy), 0) * 100 AS hit_accuracy,
              coalesce(max(max_combo), 0)::int AS max_combo,
              count(*) FILTER (WHERE rank = 'XH')::int AS ssh,
              count(*) FILTER (WHERE rank = 'X')::int AS ss,
              count(*) FILTER (WHERE rank = 'SH')::int AS sh,
              count(*) FILTER (WHERE rank = 'S')::int AS s,
              count(*) FILTER (WHERE rank = 'A')::int AS a,
              count(DISTINCT beatmap_id)::int AS best_count
         FROM scores
        WHERE user_id = $1`,
      [userId],
    );
    const row = result.rows[0];
    if (!row) return emptyStatistics();
    return {
      playCount: row.play_count,
      playTime: 0,
      totalScore: Number(row.total_score),
      totalHits: 0,
      hitAccuracy: Number(row.hit_accuracy.toFixed(2)),
      maxCombo: row.max_combo,
      rankedScore: Number(row.total_score),
      grades: { ssh: row.ssh, ss: row.ss, sh: row.sh, s: row.s, a: row.a },
      bestCount: row.best_count,
    };
  }
}
