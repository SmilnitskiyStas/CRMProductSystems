import { createMockMobileConfig, SAFE_DEFAULT_TENANT_ID } from '../mock';

describe('Stage 1 mock mobile configuration', () => {
  test('is tenant-scoped and explicitly non-production schema version zero', () => {
    const config = createMockMobileConfig('tenant-a');

    expect(config.schemaVersion).toBe(0);
    expect(config.tenant.id).toBe('tenant-a');
    expect(config.navigation.length).toBeGreaterThanOrEqual(2);
    expect(config.navigation.length).toBeLessThanOrEqual(5);
  });

  test('provides a safe preview tenant before selection is restored', () => {
    const config = createMockMobileConfig(SAFE_DEFAULT_TENANT_ID);
    expect(config.tenant.name).toBe('Мій магазин');
  });
});
