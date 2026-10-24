import { API_BASE } from "./api";

/** Never send an Authorization header to an asset's arbitrary/third-party origin. */
export function privateAssetPath(source: string): string | null {
  try {
    const base = new URL(API_BASE, "http://localhost");
    const url = new URL(source, base);
    if (url.origin !== base.origin || url.search || url.hash) return null;
    const match = /^\/uploads\/(shelf-scans|floor-plans|contract-assets)\/([0-9a-f-]{36})\/([a-z0-9_-]+\.(?:png|jpe?g|webp))$/i.exec(url.pathname);
    return match ? `/api/private-files/${match[1]}/${match[2]}/${match[3]}` : null;
  } catch {
    return null;
  }
}
