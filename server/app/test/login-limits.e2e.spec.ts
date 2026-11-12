/**
 * e2e: the login rate limits behind a proxy, in the requests OAuth.cs sends
 * (audit S4). `TRUST_PROXY=caddy` stands in for the deployed Caddy, so every
 * request may name its own client address in `X-Forwarded-For`, exactly as
 * Caddy forwards it:
 *
 *   - per client address: ten token requests a minute (whatever the account);
 *   - per account: ten password attempts a minute (whatever the address), so
 *     one account cannot be guessed from many addresses at once.
 */
import path from 'node:path';
import { INestApplication } from '@nestjs/common';
import { NestFactory } from '@nestjs/core';
import request from 'supertest';
import { Pool } from 'pg';
import { AppModule } from '../src/app.module';
import { configureApp } from '../src/app.setup';
import { createPool } from '../src/db/db.service';
import { runMigrations } from '../src/db/migrate';

const TEST_DATABASE_URL =
  process.env.TEST_DATABASE_URL ?? 'postgres://kan:kan@localhost:55432/kan_test';
/** Its own database, on the server TEST_DATABASE_URL points at. */
const LOGIN_DATABASE_URL = (() => {
  const url = new URL(TEST_DATABASE_URL);
  url.pathname = '/kan_login_test';
  return url.toString();
})();
const OAUTH_CLIENT_SECRET = 'e2e-client-secret';

jest.setTimeout(120_000);

let pool: Pool;

async function createTestApp(): Promise<INestApplication> {
  const app = await NestFactory.create(AppModule, { bodyParser: false, logger: false });
  configureApp(app);
  await app.init();
  return app;
}

/** Registration exactly as the game sends it (RegistrationRequest.cs). */
async function register(app: INestApplication, username: string, from: string) {
  return request(app.getHttpServer())
    .post('/users')
    .set('X-Forwarded-For', from)
    .field('user[username]', username)
    .field('user[user_email]', `${username.toLowerCase()}@example.org`)
    .field('user[password]', 'a line from a song');
}

/** The token request exactly as the game sends it (OAuth.cs). */
function login(app: INestApplication, username: string, password: string, from: string) {
  return request(app.getHttpServer())
    .post('/oauth/token')
    .set('X-Forwarded-For', from)
    .field('grant_type', 'password')
    .field('client_id', 'kan-client')
    .field('client_secret', OAUTH_CLIENT_SECRET)
    .field('scope', '*')
    .field('username', username)
    .field('password', password);
}

beforeAll(async () => {
  const adminUrl = new URL(LOGIN_DATABASE_URL);
  const dbName = adminUrl.pathname.replace(/^\//, '');
  adminUrl.pathname = '/postgres';
  const admin = createPool(adminUrl.toString());
  await admin.query(`DROP DATABASE IF EXISTS ${dbName} WITH (FORCE)`);
  await admin.query(`CREATE DATABASE ${dbName}`);
  await admin.end();

  pool = createPool(LOGIN_DATABASE_URL);
  await runMigrations(pool, path.join(__dirname, '..', 'migrations'));

  process.env.DATABASE_URL = LOGIN_DATABASE_URL;
  process.env.OAUTH_CLIENT_ID = 'kan-client';
  process.env.OAUTH_CLIENT_SECRET = OAUTH_CLIENT_SECRET;
  // The deployed stacks sit behind a proxy (tunnel or Caddy); the tests play
  // Caddy by forwarding one client address per request.
  process.env.TRUST_PROXY = 'caddy';
});

afterAll(async () => {
  await pool?.end();
});

describe('login rate limits behind the proxy', () => {
  let app: INestApplication;

  beforeAll(async () => {
    app = await createTestApp();
    expect((await register(app, 'LimitOne', '192.0.2.1')).status).toBe(201);
    expect((await register(app, 'LimitTwo', '192.0.2.2')).status).toBe(201);
    expect((await register(app, 'LimitThree', '192.0.2.3')).status).toBe(201);
  });

  afterAll(async () => {
    await app?.close();
  });

  it('limits password attempts per account, whatever the address', async () => {
    // Ten attempts from ten addresses: the per-address limit never fires...
    for (let i = 0; i < 10; i += 1) {
      const attempt = await login(app, 'LimitOne', 'wrong every time', `198.51.100.${i + 1}`);
      expect(attempt.status).toBe(400);
    }

    // ... but the eleventh for the same account is refused (audit S4).
    const refused = await login(app, 'LimitOne', 'wrong every time', '198.51.100.11');
    expect(refused.status).toBe(429);
    expect(refused.body.hint).toContain('account');

    // Another account is unaffected: this is not a global limit.
    const other = await login(app, 'LimitTwo', 'wrong every time', '198.51.100.12');
    expect(other.status).toBe(400);
  });

  it('limits token requests per address', async () => {
    // One address, one account at its own limit minus one: the per-address
    // limit is what fires here.
    for (let i = 0; i < 10; i += 1) {
      const attempt = await login(app, 'LimitThree', 'wrong every time', '203.0.113.50');
      expect(attempt.status).toBe(400);
    }
    const refused = await login(app, 'LimitThree', 'wrong every time', '203.0.113.50');
    expect(refused.status).toBe(429);
    expect(refused.body.hint).toContain('Too many attempts');

    // A different address still works (its own bucket); the account used here
    // is one with attempts left.
    const elsewhere = await login(app, 'LimitTwo', 'wrong every time', '203.0.113.51');
    expect(elsewhere.status).toBe(400);
  });
});
