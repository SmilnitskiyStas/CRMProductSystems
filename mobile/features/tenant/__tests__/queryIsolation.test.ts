import { QueryClient } from '@tanstack/react-query';
import { clearTenantQueries, consumerQueryBelongsToTenant } from '../queryIsolation';

describe('tenant query isolation', () => {
  test('identifies only tenant-owned personal query families', () => {
    expect(consumerQueryBelongsToTenant(['consumer-content', 'catalog', 'a', 'store'], 'a')).toBe(true);
    expect(consumerQueryBelongsToTenant(['loyalty', 'history', 'a', 1, 50], 'a')).toBe(true);
    expect(consumerQueryBelongsToTenant(['loyalty', 'consumer-code', 'a'], 'a')).toBe(true);
    expect(consumerQueryBelongsToTenant(['loyalty', 'memberships'], 'a')).toBe(false);
    expect(consumerQueryBelongsToTenant(['consumer-content', 'catalog', 'b'], 'a')).toBe(false);
  });

  test('removes Tenant A data without touching Tenant B or global memberships', async () => {
    const client = new QueryClient();
    client.setQueryData(['consumer-content', 'catalog', 'a', 'store'], { tenant: 'a' });
    client.setQueryData(['consumer-content', 'catalog', 'b', 'store'], { tenant: 'b' });
    client.setQueryData(['loyalty', 'memberships'], [{ tenantId: 'a' }, { tenantId: 'b' }]);

    await clearTenantQueries(client, 'a');

    expect(client.getQueryData(['consumer-content', 'catalog', 'a', 'store'])).toBeUndefined();
    expect(client.getQueryData(['consumer-content', 'catalog', 'b', 'store'])).toEqual({ tenant: 'b' });
    expect(client.getQueryData(['loyalty', 'memberships'])).toBeDefined();
    client.clear();
  });
});
