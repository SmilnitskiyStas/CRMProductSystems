export interface ConsumerAnalyticsPayloads {
  tenant_selected: { source: 'membership_switch' | 'retailer_join' | 'restore' };
  promotion_opened: { promotionId: string };
  coupon_opened: { couponId?: string };
  loyalty_card_opened: Record<string, never>;
  product_opened: { productId: string; source: 'catalog' | 'promotion' | 'news' | 'direct' };
  retailer_joined: { source: 'qr' | 'link' | 'manual' };
}

export type ConsumerAnalyticsEventName = keyof ConsumerAnalyticsPayloads;

export interface ConsumerAnalyticsEvent {
  name: ConsumerAnalyticsEventName;
  tenantId: string;
  properties: Record<string, string>;
}

export interface ConsumerAnalyticsTransport {
  capture: (event: ConsumerAnalyticsEvent) => Promise<void> | void;
}

const noOpTransport: ConsumerAnalyticsTransport = { capture: () => undefined };
let transport: ConsumerAnalyticsTransport = noOpTransport;

function safeIdentifier(value: string | undefined): string | null {
  const normalized = value?.trim();
  return normalized && normalized.length <= 128 && /^[a-zA-Z0-9_-]+$/.test(normalized)
    ? normalized
    : null;
}

export function buildConsumerAnalyticsEvent<K extends ConsumerAnalyticsEventName>(
  name: K,
  tenantId: string,
  payload: ConsumerAnalyticsPayloads[K]
): ConsumerAnalyticsEvent | null {
  const safeTenantId = safeIdentifier(tenantId);
  if (!safeTenantId) return null;
  const input = payload as Record<string, string | undefined>;
  const properties: Record<string, string> = {};
  if (name === 'tenant_selected' && ['membership_switch', 'retailer_join', 'restore'].includes(input.source ?? '')) properties.source = input.source!;
  if (name === 'promotion_opened') {
    const id = safeIdentifier(input.promotionId);
    if (!id) return null;
    properties.promotionId = id;
  }
  if (name === 'coupon_opened' && input.couponId) {
    const id = safeIdentifier(input.couponId);
    if (!id) return null;
    properties.couponId = id;
  }
  if (name === 'product_opened') {
    const id = safeIdentifier(input.productId);
    if (!id || !['catalog', 'promotion', 'news', 'direct'].includes(input.source ?? '')) return null;
    properties.productId = id;
    properties.source = input.source!;
  }
  if (name === 'retailer_joined' && ['qr', 'link', 'manual'].includes(input.source ?? '')) properties.source = input.source!;
  return { name, tenantId: safeTenantId, properties };
}

export async function trackConsumerEvent<K extends ConsumerAnalyticsEventName>(
  name: K,
  tenantId: string,
  payload: ConsumerAnalyticsPayloads[K]
): Promise<void> {
  const event = buildConsumerAnalyticsEvent(name, tenantId, payload);
  if (!event) return;
  await transport.capture(event);
}

export function setConsumerAnalyticsTransport(next: ConsumerAnalyticsTransport | null): void {
  transport = next ?? noOpTransport;
}
