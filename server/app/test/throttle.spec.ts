/**
 * Unit tests for the per-user and per-address route limits (audit S7): one
 * user or one address cannot flood a route that costs the server work.
 */
import { HttpException } from '@nestjs/common';
import { RouteThrottle } from '../src/common/throttle';

function statusOf(error: unknown): number {
  expect(error).toBeInstanceOf(HttpException);
  return (error as HttpException).getStatus();
}

describe('RouteThrottle', () => {
  it('limits one user however they are addressed', () => {
    const throttle = new RouteThrottle(2, 100);
    throttle.check(1, '192.0.2.1');
    throttle.check(1, '192.0.2.2');
    expect(() => throttle.check(1, '192.0.2.3')).toThrow(HttpException);
    try {
      throttle.check(1, '192.0.2.3');
    } catch (error) {
      expect(statusOf(error)).toBe(429);
    }

    // Another user and a guest are unaffected.
    throttle.check(2, '192.0.2.4');
    throttle.check(null, '192.0.2.5');
  });

  it('limits one address whatever the account', () => {
    const throttle = new RouteThrottle(100, 2);
    throttle.check(1, '192.0.2.1');
    throttle.check(2, '192.0.2.1');
    expect(() => throttle.check(null, '192.0.2.1')).toThrow(HttpException);

    // A different address has its own bucket.
    throttle.check(null, '192.0.2.2');
  });
});
