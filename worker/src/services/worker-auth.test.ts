import assert from 'node:assert/strict';
import test from 'node:test';
import { getWorkerGrant, workerAccessToken } from './worker-auth';

const grant = () => ({ accessToken: 'synthetic-worker-token', tenantId: 'synthetic-tenant', expiresAt: new Date(Date.now() + 300_000).toISOString() });
const mockFetch = (body: unknown, capture?: (url: string, request: RequestInit) => void) => (async (url: string | URL | Request, init?: RequestInit) => {
  capture?.(String(url), init!); return new Response(JSON.stringify(body), { status: 200 });
}) as typeof fetch;
test('worker credentials go only to the loopback machine endpoint', async () => {
  delete process.env.INTERNAL_API_BASE_URL;
  const result = await getWorkerGrant(mockFetch(grant(), (url, request) => {
    assert.equal(url, 'http://127.0.0.1:5110/api/internal/worker-session'); assert.equal(request.method, 'POST');
  })); assert.equal(result.accessToken, 'synthetic-worker-token');
});
for (const base of ['https://example.test', 'http://127.0.0.1@example.test', 'file:///tmp/test', 'http://127.0.0.1:5110?token=synthetic']) {
  test('external or ambiguous worker credential URL is rejected: '+base, async () => {
    process.env.INTERNAL_API_BASE_URL = base;
    try { await assert.rejects(getWorkerGrant(mockFetch(grant())), /loopback-only/); }
    finally { delete process.env.INTERNAL_API_BASE_URL; }
  });
}
test('human MFA challenge is never treated as a worker token', async () => {
  await assert.rejects(getWorkerGrant(mockFetch({ requiresMfaEnrollment: true, enrollmentToken: 'synthetic-proof' })), /incomplete/);
});
test('long jobs renew expiring grants and refuse a tenant change', async () => {
  const expired = { ...grant(), expiresAt: new Date(Date.now() - 1).toISOString() };
  const access = workerAccessToken(expired, mockFetch({ ...grant(), accessToken: 'renewed' }));
  assert.equal(await access(), 'renewed');
  await assert.rejects(workerAccessToken(expired, mockFetch({ ...grant(), tenantId: 'another' }))(), /tenant changed/);
});
