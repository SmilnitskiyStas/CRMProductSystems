export type WorkerGrant = { accessToken: string; tenantId: string; expiresAt: string };

export async function getWorkerGrant(fetcher: typeof fetch = fetch): Promise<WorkerGrant> {
  const base = new URL(process.env.INTERNAL_API_BASE_URL ?? 'http://127.0.0.1:5110');
  if (!['http:', 'https:'].includes(base.protocol) || !['127.0.0.1', 'localhost', '[::1]'].includes(base.hostname)
      || base.username || base.password || base.search || base.hash) {
    throw new Error('Worker credential endpoint must be loopback-only.');
  }
  const response = await fetcher(new URL('/api/internal/worker-session', base), {
    method: 'POST', headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email: process.env.WORKER_API_EMAIL ?? '', password: process.env.WORKER_API_PASSWORD ?? '' }),
  });
  if (!response.ok) throw new Error(`worker credential grant failed: HTTP ${response.status}`);
  const grant = await response.json() as Partial<WorkerGrant>;
  if (!grant.accessToken || !grant.tenantId || !grant.expiresAt || !Number.isFinite(Date.parse(grant.expiresAt))) throw new Error('Worker credential grant is incomplete.');
  return grant as WorkerGrant;
}

export function workerAccessToken(initial: WorkerGrant, fetcher: typeof fetch = fetch): () => Promise<string> {
  let current = initial;
  return async () => {
    if (Date.now() >= Date.parse(current.expiresAt) - 30_000) {
      current = await getWorkerGrant(fetcher);
      if (current.tenantId !== initial.tenantId) throw new Error('Worker tenant changed during the job.');
    }
    return current.accessToken;
  };
}
