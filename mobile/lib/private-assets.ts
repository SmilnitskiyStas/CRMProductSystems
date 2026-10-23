/** Build a native Image source without ever sending workspace credentials to an external host. */
export function privateAssetSource(source: string | null, token: string | null, apiBase: string | undefined) {
  if (!source || !token || !apiBase) return null;
  try {
    const base = new URL(apiBase);
    const url = new URL(source, base.origin);
    if (url.origin !== base.origin || url.search || url.hash) return null;
    const match = /^\/uploads\/(shelf-scans|floor-plans|contract-assets)\/([0-9a-f-]{36})\/([a-z0-9_-]+\.(?:png|jpe?g|webp))$/i.exec(url.pathname);
    if (!match) return null;
    return {
      uri: `${base.origin}/api/private-files/${match[1]}/${match[2]}/${match[3]}`,
      headers: { Authorization: `Bearer ${token}` },
      cache: 'reload' as const,
    };
  } catch {
    return null;
  }
}
