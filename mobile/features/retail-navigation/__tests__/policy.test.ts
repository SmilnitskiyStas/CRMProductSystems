import { createMockMobileConfig } from '@/features/mobile-config/mock';
import { personalRouteAllowed, resolveRetailNavigation } from '../policy';

describe('retail navigation policy', () => {
  test('maps validated route and icon identifiers to local application values', () => {
    const config = createMockMobileConfig('tenant-a');
    const result = resolveRetailNavigation(config.navigation, config.features, true);

    expect(result.map((item) => item.screen)).toEqual(['index', 'catalog', 'wallet', 'account']);
    expect(result.map((item) => item.iconName)).toEqual([
      'home-outline',
      'grid-outline',
      'qr-code-outline',
      'person-outline',
    ]);
  });

  test('applies feature and personal-access gates centrally', () => {
    const config = createMockMobileConfig('tenant-a');
    const features = { ...config.features, catalog: false };
    const result = resolveRetailNavigation(config.navigation, features, false);

    expect(result.map((item) => item.type)).toEqual(['home', 'profile']);
  });

  test('ignores an unsupported runtime route even if data bypasses TypeScript', () => {
    const config = createMockMobileConfig('tenant-a');
    const result = resolveRetailNavigation(
      [{ type: 'external-url', label: 'Unsafe', icon: 'home' }] as never,
      config.features,
      true
    );

    expect(result).toEqual([]);
  });

  test('protects feature and identity routes opened through deep links', () => {
    const config = createMockMobileConfig('tenant-a');
    expect(personalRouteAllowed('catalog', { ...config.features, catalog: false }, true)).toBe(false);
    expect(personalRouteAllowed('wallet', config.features, false)).toBe(false);
    expect(personalRouteAllowed('wallet', config.features, true)).toBe(true);
    expect(personalRouteAllowed('account', config.features, false)).toBe(true);
  });
});
