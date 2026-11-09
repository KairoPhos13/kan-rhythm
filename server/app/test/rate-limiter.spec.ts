/** Unit tests for the in-memory rate limiter used on login/registration. */
import { RateLimiter } from '../src/common/rate-limiter';

describe('RateLimiter', () => {
  it('allows up to the maximum within the window and rejects afterwards', () => {
    const limiter = new RateLimiter(1000, 3);
    const now = 1_000_000;
    expect(limiter.check('ip', now)).toBe(true);
    expect(limiter.check('ip', now)).toBe(true);
    expect(limiter.check('ip', now)).toBe(true);
    expect(limiter.check('ip', now)).toBe(false);
  });

  it('starts a fresh window once the old one expires', () => {
    const limiter = new RateLimiter(1000, 1);
    expect(limiter.check('ip', 1_000)).toBe(true);
    expect(limiter.check('ip', 1_500)).toBe(false);
    expect(limiter.check('ip', 2_100)).toBe(true);
  });

  it('keeps separate buckets per key', () => {
    const limiter = new RateLimiter(1000, 1);
    expect(limiter.check('a', 5_000)).toBe(true);
    expect(limiter.check('b', 5_000)).toBe(true);
    expect(limiter.check('a', 5_000)).toBe(false);
  });
});
