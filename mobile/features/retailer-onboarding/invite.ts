export interface RetailerInvite {
  slug: string;
  source: 'custom-link' | 'universal-link';
}

const SLUG = /^[a-z0-9]+(?:-[a-z0-9]+)*$/i;
const trustedUniversalLinkHosts = new Set(['app.shelfguard.ua']);

export function parseRetailerInvite(value: string): RetailerInvite | null {
  const normalized = value.trim();
  try {
    const url = new URL(normalized);
    const parts = url.pathname.split('/').filter(Boolean);
    const slug = parts.at(-1);
    if (!slug || !SLUG.test(slug) || url.search || url.hash) return null;
    if (url.protocol === 'shelfguard:' && url.hostname === 'join' && parts.length === 1) {
      return { slug: slug.toLowerCase(), source: 'custom-link' };
    }
    if (
      url.protocol === 'https:' &&
      trustedUniversalLinkHosts.has(url.hostname.toLowerCase()) &&
      parts.length === 2 &&
      parts[0].toLowerCase() === 'join'
    ) {
      return { slug: slug.toLowerCase(), source: 'universal-link' };
    }
  } catch {
    return null;
  }
  return null;
}
