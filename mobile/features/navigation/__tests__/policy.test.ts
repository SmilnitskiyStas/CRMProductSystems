import { navigationDecision, visibleRoutes } from '../policy';
import type { AuthUser } from '@/features/auth/types';

function user(role: string, capabilities: string[] = [], tabs: string[] = []): AuthUser {
  return {
    id: 'user-1', email: 'staff@example.com', fullName: 'Staff', role,
    tenantId: 'tenant-1', locationId: 'location-1', permissions: {}, capabilities, tabs,
  };
}

const allModules = {
  businessType: 'restaurant',
  modules: ['inventory', 'procurement', 'pos', 'production', 'marketplace'],
};

describe('mobile navigation policy', () => {
  test.each([
    ['cashier', '/(app)/pos', true],
    ['cashier', '/(app)/transfers', false],
    ['storekeeper', '/(app)/stock', true],
    ['storekeeper', '/(app)/customers', false],
    ['store_manager', '/(app)/customers', true],
    ['enterprise_admin', '/(app)/production', true],
  ])('%s access to %s is %s', (role, route, allowed) => {
    expect(navigationDecision(route, { user: user(role), settings: allModules }).allowed).toBe(allowed);
  });

  test('capability and tab are preserved as independent access inputs', () => {
    expect(navigationDecision('/(app)/customers', {
      user: user('cashier', ['customers.manage'], ['customers']),
      settings: allModules,
    }).allowed).toBe(true);
  });

  test('disabled module and missing module context fail closed for direct links', () => {
    expect(navigationDecision('/(app)/pos/payment', {
      user: user('cashier'), settings: { ...allModules, modules: ['inventory'] },
    })).toEqual({ allowed: false, reason: 'module_disabled' });
    expect(navigationDecision('/(app)/pos', { user: user('cashier'), settings: null }))
      .toEqual({ allowed: false, reason: 'context_unavailable' });
  });

  test('unknown direct links fail closed instead of inheriting dashboard access', () => {
    expect(navigationDecision('/(app)/unknown', {
      user: user('enterprise_admin'), settings: allModules,
    })).toEqual({ allowed: false, reason: 'access_denied' });
  });

  test('AI assistant is hidden and guarded when inventory is disabled', () => {
    expect(navigationDecision('/(app)/ai-assistant', {
      user: user('store_manager'), settings: { businessType: 'retail', modules: [] },
    })).toEqual({ allowed: false, reason: 'module_disabled' });
  });

  test('business type gates production and auto service', () => {
    expect(navigationDecision('/(app)/production', {
      user: user('store_manager'), settings: { businessType: 'retail', modules: ['production'] },
    }).allowed).toBe(false);
    expect(navigationDecision('/(app)/auto-service', {
      user: user('store_manager'), settings: { businessType: 'auto_service', modules: ['auto_service'] },
    }).allowed).toBe(true);
  });

  test('provider may use shell but fails closed for tenant module routes', () => {
    const provider = { ...user('provider'), tenantId: null };
    expect(navigationDecision('/(app)', { user: provider, settings: null }).allowed).toBe(true);
    expect(navigationDecision('/(app)/stock', { user: provider, settings: null }).allowed).toBe(false);
  });

  test('supplier cannot enter unrelated tenant modules', () => {
    const supplier = user('supplier_admin');
    const settings = { businessType: 'supplier', modules: ['marketplace_supplier'] };
    expect(navigationDecision('/(app)/pos', { user: supplier, settings }).allowed).toBe(false);
    expect(navigationDecision('/(app)/production', { user: supplier, settings }).allowed).toBe(false);
  });

  test('shortcut filtering uses the same route policy', () => {
    const items = [{ href: '/(app)/pos' }, { href: '/(app)/transfers' }];
    expect(visibleRoutes(items, { user: user('cashier'), settings: allModules }))
      .toEqual([{ href: '/(app)/pos' }]);
  });
});
