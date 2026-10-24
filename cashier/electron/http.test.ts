import http from 'node:http';
import type { AddressInfo } from 'node:net';
import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import { configureHttpTransport, httpRequest, normalizeServerUrl, setNetworkErrorListener } from './http';

let server: http.Server;
let base: string;
let redirectDestinationHits = 0;
beforeAll(async () => {
  server = http.createServer((req, res) => {
    if (req.url === '/redirect') {
      res.writeHead(302, { Location: '/redirect-destination' });
      res.end();
      return;
    }
    if (req.url === '/redirect-destination') redirectDestinationHits++;
    res.writeHead(req.url === '/401' ? 401 : req.url === '/500' ? 500 : 404);
    res.end('{}');
  });
  await new Promise<void>((r) => server.listen(0, '127.0.0.1', r));
  base = `http://127.0.0.1:${(server.address() as AddressInfo).port}`;
});
afterAll(() => new Promise<void>((r) => server.close(() => r())));

describe('httpRequest reachability', () => {
  beforeEach(() => configureHttpTransport({ allowLoopbackHttp: true }));
  afterEach(() => configureHttpTransport({ allowLoopbackHttp: false }));
  it('refuses a redirect before credentials can be forwarded to its destination', async () => {
    const response = await httpRequest(base, '/redirect', {
      headers: { Authorization: 'Bearer synthetic-token' }, timeoutMs: 2000,
    });
    expect(response.networkError).toBe(true);
    expect(redirectDestinationHits).toBe(0);
  });
  it('any HTTP status (401/404/500) is a response, not a network error', async () => {
    for (const p of ['/401', '/404', '/500']) {
      const res = await httpRequest(base, p, { timeoutMs: 2000 });
      expect(res.networkError).toBe(false);
    }
  });

  it('a refused connection is a network error and notifies the monitor (unless it is a probe)', async () => {
    let calls = 0;
    setNetworkErrorListener(() => calls++);
    const dead = 'http://127.0.0.1:1'; // nothing listens
    expect((await httpRequest(dead, '/x', { timeoutMs: 1000 })).networkError).toBe(true);
    expect(calls).toBe(1);
    expect((await httpRequest(dead, '/x', { timeoutMs: 1000, probe: true })).networkError).toBe(true);
    expect(calls).toBe(1);
    setNetworkErrorListener(null);
  });
});

describe('POS TLS policy', () => {
  afterEach(() => {
    configureHttpTransport({ allowLoopbackHttp: false });
    vi.unstubAllGlobals();
  });

  it.each([
    'http://pos.example.com', 'http://10.0.0.1', 'http://localhost',
    'http://127.0.0.1', 'http://[::1]', 'ftp://pos.example.com',
    'https://user:password@pos.example.com', 'https://pos.example.com?access_token=marker',
    'https://pos.example.com#fragment',
  ])('rejects an unsafe server by default: %s', (url) => {
    expect(normalizeServerUrl(url)).toBeNull();
  });

  it('normalizes HTTPS including a base path', () => {
    expect(normalizeServerUrl(' https://pos.example.com/api-root/// ')).toBe('https://pos.example.com/api-root');
  });

  it('allows only exact loopback hosts after explicit development opt-in', () => {
    configureHttpTransport({ allowLoopbackHttp: true });
    for (const host of ['localhost', '127.0.0.1', '[::1]'])
      expect(normalizeServerUrl(`http://${host}:5000`)).not.toBeNull();
    for (const host of ['localhost.example.com', '127.0.0.2', '10.0.0.1', 'pos.example.com'])
      expect(normalizeServerUrl(`http://${host}:5000`)).toBeNull();
  });

  it('refuses an unsafe saved server before sending credentials or a receipt', async () => {
    const fetch = vi.fn();
    vi.stubGlobal('fetch', fetch);
    const response = await httpRequest('http://remote.example.com', '/api/pos/sales', {
      method: 'POST', headers: { Authorization: 'Bearer synthetic-token' }, body: { amount: 10 },
    });
    expect(response.transportError).toBe(true);
    expect(response.status).toBe(0); // never turn an accepted offline receipt into an HTTP 4xx rejection
    expect(fetch).not.toHaveBeenCalled();
  });

  it('disables redirects and never retries a failed TLS handshake over HTTP', async () => {
    const fetch = vi.fn().mockRejectedValue(new Error('certificate verification failed'));
    vi.stubGlobal('fetch', fetch);
    const response = await httpRequest('https://pos.example.com', '/api/pos/sales');
    expect(response.networkError).toBe(true);
    expect(fetch).toHaveBeenCalledTimes(1);
    expect(fetch.mock.calls[0][0].protocol).toBe('https:');
    expect(fetch.mock.calls[0][1].redirect).toBe('error');
  });
});
