import http from 'node:http';
import type { AddressInfo } from 'node:net';
import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import { httpRequest, setNetworkErrorListener } from './http';

let server: http.Server;
let base: string;
beforeAll(async () => {
  server = http.createServer((req, res) => {
    res.writeHead(req.url === '/401' ? 401 : req.url === '/500' ? 500 : 404);
    res.end('{}');
  });
  await new Promise<void>((r) => server.listen(0, '127.0.0.1', r));
  base = `http://127.0.0.1:${(server.address() as AddressInfo).port}`;
});
afterAll(() => new Promise<void>((r) => server.close(() => r())));

describe('httpRequest reachability', () => {
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
