/**
 * Minimal in-memory sliding-window rate limiter (no Redis in the MVP budget).
 * Used on login and registration; a stray 429 simply shows as an error in the
 * client, which has no retry logic (`lazer-protocol.md` §2).
 */
export class RateLimiter {
  private readonly buckets = new Map<string, { count: number; resetAt: number }>();

  constructor(
    private readonly windowMs: number,
    private readonly max: number,
  ) {}

  /** Consumes one slot; returns false when the key is over its limit. */
  check(key: string, now: number = Date.now()): boolean {
    if (this.buckets.size > 10_000) this.sweep(now);
    const bucket = this.buckets.get(key);
    if (!bucket || bucket.resetAt <= now) {
      this.buckets.set(key, { count: 1, resetAt: now + this.windowMs });
      return true;
    }
    if (bucket.count >= this.max) return false;
    bucket.count += 1;
    return true;
  }

  private sweep(now: number): void {
    for (const [key, bucket] of this.buckets) {
      if (bucket.resetAt <= now) this.buckets.delete(key);
    }
  }
}
