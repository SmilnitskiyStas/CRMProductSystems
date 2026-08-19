export interface RetailerInvite {
  tenantId: string;
  source: 'payload' | 'custom-link' | 'universal-link';
}

const UUID = '[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}';
const payloadPattern = new RegExp(`^SGRTL1\\.(${UUID})$`, 'i');
const trustedUniversalLinkHosts = new Set(['app.shelfguard.ua']);

export function parseRetailerInvite(value: string): RetailerInvite | null {
  const normalized = value.trim();
  const payload = payloadPattern.exec(normalized);
  if (payload) return { tenantId: payload[1].toLowerCase(), source: 'payload' };

  try {
    const url = new URL(normalized);
    const parts = url.pathname.split('/').filter(Boolean);
    const tenantId = parts.at(-1);
    if (!tenantId || !new RegExp(`^${UUID}$`, 'i').test(tenantId)) return null;
    if (url.protocol === 'shelfguard:' && url.hostname === 'retailer') {
      return { tenantId: tenantId.toLowerCase(), source: 'custom-link' };
    }
    if (
      url.protocol === 'https:' &&
      trustedUniversalLinkHosts.has(url.hostname.toLowerCase()) &&
      parts.length === 2 &&
      parts[0] === 'retailer'
    ) {
      return { tenantId: tenantId.toLowerCase(), source: 'universal-link' };
    }
  } catch {
    return null;
  }
  return null;
}
