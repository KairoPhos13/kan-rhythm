/**
 * Minimal in-memory sliding-window rate limiter (no Redis in the MVP budget).
 * Used on login, registration and the write-heavy API routes; a stray 429
 * simply shows as an error in the client, which has no retry logic
 * (`lazer-protocol.md` §2).
 *
 * The bucket map is bounded (audit S11): expired buckets are swept once the
 * map passes 10 000 entries, and past `maxBuckets` the oldest buckets are
 * evicted to make room. Evicting a bucket only forgets its count; the map can
 * never grow with the number of keys the internet sends.
 */
export class RateLimiter {
  private readonly buckets = new Map<string, { count: number; resetAt: number }>();

  constructor(
    private readonly windowMs: number,
    private readonly max: number,
    /** Hard cap on tracked keys; the oldest are evicted beyond it. */
    private readonly maxBuckets: number = 50_000,
  ) {}

  /** Consumes one slot; returns false when the key is over its limit. */
  check(key: string, now: number = Date.now()): boolean {
    if (this.buckets.size > 10_000) this.sweep(now);
    const bucket = this.buckets.get(key);
    if (!bucket || bucket.resetAt <= now) {
      // Delete first: `Map.set` keeps the old position of a live key, and the
      // eviction order must follow how long ago a bucket started.
      this.buckets.delete(key);
      this.insert(key, now);
      return true;
    }
    if (bucket.count >= this.max) return false;
    bucket.count += 1;
    return true;
  }

  private insert(key: string, now: number): void {
    while (this.buckets.size >= this.maxBuckets) {
      const oldest = this.buckets.keys().next().value;
      if (oldest === undefined) break;
      this.buckets.delete(oldest);
    }
    this.buckets.set(key, { count: 1, resetAt: now + this.windowMs });
  }

  private sweep(now: number): void {
    for (const [key, bucket] of this.buckets) {
      if (bucket.resetAt <= now) this.buckets.delete(key);
    }
  }
}
