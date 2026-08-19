import { createMockMobileConfig } from '@/features/mobile-config/mock';
import { retailFeatureEnabled } from '../policy';

describe('retail feature policy', () => {
  test('only enables explicitly true features', () => {
    const { features } = createMockMobileConfig('tenant-a');
    expect(retailFeatureEnabled(features, 'catalog')).toBe(true);
    expect(retailFeatureEnabled(features, 'delivery')).toBe(false);
  });
});
