/**
 * Client IP extraction for rate limiting.
 *
 * Behind a proxy (the Cloudflare Tunnel, or the Caddy of the direct stack)
 * every connection comes from the proxy, so the socket address is useless for
 * rate limiting: all players would share one bucket. The real client address
 * travels in a forwarded header, and those headers are trivially spoofable by
 * anyone who talks to the app directly, so they are only trusted when
 * `TRUST_PROXY` says which proxy is in front, never otherwise:
 *
 *  - `cloudflare`: Cloudflare's edge (the tunnel stack). `CF-Connecting-IP` is
 *    set and overwritten by Cloudflare itself; when it is missing the request
 *    did not come through Cloudflare and the rightmost `X-Forwarded-For` hop
 *    (the address Cloudflare appended) is used, then the socket address.
 *    The leftmost hop is never taken: a client can write it itself.
 *  - `caddy`: our own Caddy (the direct stack, deploy/DEPLOY.md §15). The
 *    rightmost `X-Forwarded-For` hop is the one Caddy appended from its own
 *    TCP peer, so it is unspoofable through Caddy. `CF-Connecting-IP` is not
 *    honoured in this mode: anything that can reach Caddy could set it. (If
 *    Cloudflare is put in front of Caddy, close port 443 to everything but
 *    Cloudflare's IP ranges and use `cloudflare` instead.)
 */

export type TrustProxyMode = 'none' | 'cloudflare' | 'caddy';

/** `TRUST_PROXY` environment flag: only these literal values opt in. */
export function parseTrustProxy(value: string | undefined): TrustProxyMode {
  if (value === 'cloudflare') return 'cloudflare';
  if (value === 'caddy') return 'caddy';
  return 'none';
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

/**
 * Rightmost hop of `X-Forwarded-For`: `client, proxy1, proxy2` → `proxy2`, the
 * entry the proxy closest to us appended. A client that forges the header only
 * adds hops to the left, which we never read.
 */
function lastForwardedHop(value: string | undefined): string | undefined {
  const hops = (value ?? '').split(',');
  return normalise(hops[hops.length - 1]);
}

export function clientIpFrom(
  headers: Record<string, unknown> | undefined,
  remoteAddress: string | null | undefined,
  trustProxy: TrustProxyMode,
): string {
  const socketIp = normalise(remoteAddress ?? undefined) ?? 'unknown';
  if (trustProxy === 'none') return socketIp;

  if (trustProxy === 'cloudflare') {
    const connectingIp = normalise(headerValue(headers ?? {}, 'cf-connecting-ip'));
    if (connectingIp) return connectingIp;
  }

  return lastForwardedHop(headerValue(headers ?? {}, 'x-forwarded-for')) ?? socketIp;
}

/** Client IP of a request, honouring the `TRUST_PROXY` policy. */
export function requestClientIp(
  request: { headers: Record<string, unknown>; socket?: { remoteAddress?: string | null } },
  trustProxy: TrustProxyMode,
): string {
  return clientIpFrom(request.headers, request.socket?.remoteAddress ?? null, trustProxy);
}
