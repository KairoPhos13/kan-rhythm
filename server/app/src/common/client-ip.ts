/**
 * Client IP extraction for rate limiting.
 *
 * Behind Cloudflare Tunnel every connection comes from the tunnel, so the
 * socket address is useless for rate limiting: the real client address travels
 * in Cloudflare's `CF-Connecting-IP` header (or, as a fallback, the first hop
 * of `X-Forwarded-For`). Those headers are trivially spoofable by anyone who
 * talks to the app directly, so they are only trusted when
 * `TRUST_PROXY=cloudflare` is set explicitly — never otherwise.
 */

export type TrustProxyMode = 'none' | 'cloudflare';

/** `TRUST_PROXY` environment flag: only the literal value `cloudflare` opts in. */
export function parseTrustProxy(value: string | undefined): TrustProxyMode {
  return value === 'cloudflare' ? 'cloudflare' : 'none';
}

type HeaderValue = string | string[] | undefined;

function headerValue(headers: Record<string, unknown>, name: string): string | undefined {
  const value = headers[name.toLowerCase()] as HeaderValue | undefined;
  const raw = Array.isArray(value) ? value[0] : value;
  return typeof raw === 'string' && raw.trim() !== '' ? raw.trim() : undefined;
}

/** Strips the IPv4-mapped IPv6 prefix node's sockets report (`::ffff:1.2.3.4`). */
function normalise(address: string | undefined): string | undefined {
  if (!address) return undefined;
  const trimmed = address.trim();
  if (trimmed === '') return undefined;
  return trimmed.replace(/^::ffff:/i, '');
}

/** First hop of `X-Forwarded-For`: `client, proxy1, proxy2` → `client`. */
function firstForwardedHop(value: string | undefined): string | undefined {
  return normalise(value?.split(',')[0]);
}

export function clientIpFrom(
  headers: Record<string, unknown> | undefined,
  remoteAddress: string | null | undefined,
  trustProxy: TrustProxyMode,
): string {
  const socketIp = normalise(remoteAddress ?? undefined) ?? 'unknown';
  if (trustProxy !== 'cloudflare') return socketIp;

  const connectingIp = normalise(headerValue(headers ?? {}, 'cf-connecting-ip'));
  if (connectingIp) return connectingIp;

  return firstForwardedHop(headerValue(headers ?? {}, 'x-forwarded-for')) ?? socketIp;
}

/** Client IP of a request, honouring the `TRUST_PROXY` policy. */
export function requestClientIp(
  request: { headers: Record<string, unknown>; socket?: { remoteAddress?: string | null } },
  trustProxy: TrustProxyMode,
): string {
  return clientIpFrom(request.headers, request.socket?.remoteAddress ?? null, trustProxy);
}
