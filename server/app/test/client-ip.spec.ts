/**
 * Unit tests for client IP extraction behind the two supported proxies
 * (Cloudflare Tunnel, Caddy) and for the absolute URL building that goes with
 * it (review note: forwarded headers are only trusted when `TRUST_PROXY` names
 * the proxy in front, and only in the form that proxy writes).
 */
import { clientIpFrom, parseTrustProxy, requestClientIp } from '../src/common/client-ip';
import { absoluteUrl, publicBaseUrl, requestHost } from '../src/common/url';

describe('parseTrustProxy', () => {
  it('only opts in for the literal proxy names', () => {
    expect(parseTrustProxy('cloudflare')).toBe('cloudflare');
    expect(parseTrustProxy('caddy')).toBe('caddy');
    expect(parseTrustProxy(undefined)).toBe('none');
    expect(parseTrustProxy('')).toBe('none');
    expect(parseTrustProxy('true')).toBe('none');
    expect(parseTrustProxy('Cloudflare')).toBe('none');
  });
});

describe('clientIpFrom', () => {
  const headers = {
    'cf-connecting-ip': '203.0.113.9',
    'x-forwarded-for': '203.0.113.9, 198.51.100.1',
  };

  it('never trusts forwarded headers without the flag', () => {
    expect(clientIpFrom(headers, '192.0.2.55', 'none')).toBe('192.0.2.55');
  });

  it('uses CF-Connecting-IP when TRUST_PROXY=cloudflare', () => {
    expect(clientIpFrom(headers, '192.0.2.55', 'cloudflare')).toBe('203.0.113.9');
  });

  it('takes the rightmost X-Forwarded-For hop: the one our proxy appended', () => {
    // A client that forges the header only adds hops to the left; the last one
    // is the address the trusted proxy saw (audit S4).
    expect(clientIpFrom({ 'x-forwarded-for': '203.0.113.9, 198.51.100.1' }, '192.0.2.55', 'cloudflare')).toBe(
      '198.51.100.1',
    );
    expect(clientIpFrom({ 'x-forwarded-for': '203.0.113.9,198.51.100.1 , 10.0.0.1' }, '192.0.2.55', 'cloudflare')).toBe(
      '10.0.0.1',
    );
  });

  it('ignores CF-Connecting-IP behind Caddy, where anyone could set it', () => {
    expect(clientIpFrom(headers, '192.0.2.55', 'caddy')).toBe('198.51.100.1');
    expect(clientIpFrom({ 'cf-connecting-ip': '203.0.113.9' }, '192.0.2.55', 'caddy')).toBe('192.0.2.55');
  });

  it('takes the forwarded address behind Caddy', () => {
    expect(clientIpFrom({ 'x-forwarded-for': '203.0.113.9' }, '192.0.2.55', 'caddy')).toBe('203.0.113.9');
  });

  it('falls back to the socket address when no forwarded header is present', () => {
    expect(clientIpFrom({}, '192.0.2.55', 'cloudflare')).toBe('192.0.2.55');
    expect(clientIpFrom({ 'cf-connecting-ip': '   ' }, '192.0.2.55', 'cloudflare')).toBe('192.0.2.55');
    expect(clientIpFrom(headers, null, 'cloudflare')).toBe('203.0.113.9');
  });

  it('normalises IPv4-mapped IPv6 socket addresses', () => {
    expect(clientIpFrom({}, '::ffff:192.0.2.55', 'none')).toBe('192.0.2.55');
    expect(clientIpFrom({ 'x-forwarded-for': '::ffff:198.51.100.1' }, '192.0.2.55', 'caddy')).toBe(
      '198.51.100.1',
    );
  });

  it('reports "unknown" without any address at all', () => {
    expect(clientIpFrom({}, undefined, 'none')).toBe('unknown');
    expect(clientIpFrom({}, '', 'none')).toBe('unknown');
  });

  it('reads the client IP from a request (headers may repeat)', () => {
    const request = {
      headers: { 'cf-connecting-ip': ['203.0.113.9'] },
      socket: { remoteAddress: '::ffff:192.0.2.55' },
    };
    expect(requestClientIp(request, 'cloudflare')).toBe('203.0.113.9');
    expect(requestClientIp(request, 'caddy')).toBe('192.0.2.55');
    expect(requestClientIp(request, 'none')).toBe('192.0.2.55');
  });
});

describe('absolute URLs behind the tunnel', () => {
  const request = (headers: Record<string, unknown>, protocol = 'http') => ({ headers, protocol });

  it('uses https when a proxy is trusted even over plain HTTP', () => {
    expect(publicBaseUrl(request({ host: 'api.example.org' }), 'cloudflare')).toBe(
      'https://api.example.org',
    );
    expect(publicBaseUrl(request({ host: 'api.example.org' }), 'caddy')).toBe(
      'https://api.example.org',
    );
  });

  it('keeps the request protocol when no proxy is trusted', () => {
    expect(publicBaseUrl(request({ host: 'localhost:3000' }), 'none')).toBe('http://localhost:3000');
    expect(publicBaseUrl(request({ host: 'localhost:3000' }, 'https'), 'none')).toBe(
      'https://localhost:3000',
    );
  });

  it('sanitises the Host header', () => {
    expect(requestHost({ host: 'api.example.org' })).toBe('api.example.org');
    expect(requestHost({ host: 'api.example.org\r\nX-Evil: 1' })).toBe('localhost');
    expect(requestHost({})).toBe('localhost');
  });

  it('joins base and path exactly once', () => {
    expect(absoluteUrl('https://api.example.org/', '/api/v2')).toBe('https://api.example.org/api/v2');
    expect(absoluteUrl('https://api.example.org', 'api/v2')).toBe('https://api.example.org/api/v2');
  });
});
