import { useEffect } from 'react';
import { ConfiguredRetailPage } from '@/features/server-driven-ui/ConfiguredRetailPage';
import { useSelectedConsumerContext } from '@/features/consumer-content/hooks';
import { trackConsumerEvent } from '@/features/consumer-analytics/analytics';

export default function CouponsScreen() {
  const { membership } = useSelectedConsumerContext();
  useEffect(() => {
    if (membership) void trackConsumerEvent('coupon_opened', membership.tenantId, {});
  }, [membership]);
  return <ConfiguredRetailPage pageKey="coupons" title="Купони" />;
}
