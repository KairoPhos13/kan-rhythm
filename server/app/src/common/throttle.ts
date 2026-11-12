/**
 * Per-user and per-address rate limits for the API routes that cost the server
 * real work (audit S7: score tokens, score submission, level reservation,
 * search and download had no throttle at all).
 *
 * The limits are deliberately generous (calm by design): a stray 429 only
 * shows as an error in the client, which has no retry logic
 * (`lazer-protocol.md` §2), so a real player should never see one.
 */
import { apiError } from './http';
import { RateLimiter } from './rate-limiter';

export class RouteThrottle {
  private readonly perUser: RateLimiter;
  private readonly perAddress: RateLimiter;

  constructor(
    userPerMinute: number,
    addressPerMinute: number,
    private readonly message = 'Too many requests, please try again in a minute',
  ) {
    this.perUser = new RateLimiter(60_000, userPerMinute);
    this.perAddress = new RateLimiter(60_000, addressPerMinute);
  }

  /** Throws 429 once either the user or the address is over its limit. */
  check(userId: number | null, clientIp: string): void {
    if (userId != null && !this.perUser.check(`user:${userId}`)) {
      throw apiError(429, this.message);
    }
    if (!this.perAddress.check(`ip:${clientIp}`)) {
      throw apiError(429, this.message);
    }
  }
}
