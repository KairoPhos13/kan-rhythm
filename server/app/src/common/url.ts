/**
 * Absolute URL building. Behind Cloudflare Tunnel TLS terminates at the edge,
 * so the app answers plain HTTP on the compose network while the public URL is
 * `https://…` — anything absolute (health output today, media URLs later per
 * server/docs/mvp-scope.md §8) must say `https` when `TRUST_PROXY=cloudflare`.
 */
import { TrustProxyMode } from './client-ip';

const HOST_PATTERN = /^[A-Za-z0-9.\-:[\]]+$/;

/** Sanitised `Host` header (the public hostname the client used). */
export function requestHost(headers: Record<string, unknown> | undefined): string {
  const raw = headers?.['host'];
  const value = Array.isArray(raw) ? raw[0] : raw;
  const host = typeof value === 'string' ? value.trim() : '';
  // Reject anything but a plain hostname[:port] — a poisoned Host header must
  // never end up inside a generated URL.
  return host !== '' && HOST_PATTERN.test(host) ? host : 'localhost';
}

/** Public base URL (`https://api.example.org`) of the running server. */
export function publicBaseUrl(
  request: { headers: Record<string, unknown>; protocol?: string },
  trustProxy: TrustProxyMode,
): string {
  // The tunnel (or a TLS-terminating proxy) is in front: always https there.
  const secure = trustProxy === 'cloudflare' || request.protocol === 'https';
  return `${secure ? 'https' : 'http'}://${requestHost(request.headers)}`;
}

/** Joins a base URL and a path exactly once around the slash. */
export function absoluteUrl(base: string, path: string): string {
  return `${base.replace(/\/+$/, '')}/${path.replace(/^\/+/, '')}`;
}
