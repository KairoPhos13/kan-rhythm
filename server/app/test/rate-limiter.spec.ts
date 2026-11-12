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

  it('evicts the oldest buckets past the hard cap (audit S11)', () => {
    // Two tracked keys at most: the third insert evicts the oldest ('a'), so
    // 'a' gets a fresh window while 'c' is still limited.
    const limiter = new RateLimiter(1000, 1, 2);
    expect(limiter.check('a', 0)).toBe(true);
    expect(limiter.check('b', 0)).toBe(true);
    expect(limiter.check('c', 0)).toBe(true);
    expect(limiter.check('c', 500)).toBe(false);
    expect(limiter.check('a', 500)).toBe(true);
  });

  it('re-arms a bucket in place when its window expires', () => {
    const limiter = new RateLimiter(1000, 1, 2);
    expect(limiter.check('a', 0)).toBe(true);
    expect(limiter.check('a', 1_500)).toBe(true);
    expect(limiter.check('a', 1_500)).toBe(false);
  });
});
