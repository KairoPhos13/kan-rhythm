/**
 * Unit tests for the configuration (audit S12): the development default
 * `DATABASE_URL` carries a known password, so production must set its own.
 */
import { loadConfig } from '../src/config';

describe('loadConfig', () => {
  it('uses the development default outside production', () => {
    expect(loadConfig({}).databaseUrl).toBe('postgres://kan:kan@localhost:55432/kan');
  });

  it('refuses to start in production without DATABASE_URL', () => {
    expect(() => loadConfig({ NODE_ENV: 'production' })).toThrow(/DATABASE_URL is required/);
    // The blank value counts as missing too.
    expect(() => loadConfig({ NODE_ENV: 'production', DATABASE_URL: '  ' })).toThrow(
      /DATABASE_URL is required/,
    );
  });

  it('takes the configured DATABASE_URL in production', () => {
    const config = loadConfig({
      NODE_ENV: 'production',
      DATABASE_URL: 'postgres://kan:real@db:5432/kan',
    });
    expect(config.databaseUrl).toBe('postgres://kan:real@db:5432/kan');
  });

  it('reads the trust-proxy mode and the package limit', () => {
    const config = loadConfig({
      TRUST_PROXY: 'caddy',
      SUBMISSION_MAX_PACKAGE_BYTES: '2048',
    });
    expect(config.trustProxy).toBe('caddy');
    expect(config.submission.maxPackageBytes).toBe(2048);
  });
});
