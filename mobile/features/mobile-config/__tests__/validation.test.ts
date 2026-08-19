import { createMockMobileConfig } from '../mock';
import { validateMobileConfig } from '../validation';

describe('mobile configuration validation boundary', () => {
  test('accepts the complete Stage 3 draft configuration', () => {
    const result = validateMobileConfig(createMockMobileConfig('tenant-a'));
    expect(result.valid).toBe(true);
    expect(result.config?.tenant.id).toBe('tenant-a');
  });

  test('accepts and normalizes the canonical production schema version', () => {
    const base = createMockMobileConfig('123e4567-e89b-42d3-a456-426614174000');
    const candidate = {
      ...base,
      schemaVersion: 1,
      theme: { ...base.theme, logoUrl: null },
      features: { loyalty: true, catalog: true },
      navigation: [
        { type: 'home', label: 'Головна', icon: 'home-outline' },
        { type: 'profile', label: 'Профіль', icon: 'person-outline' },
      ],
    };
    const result = validateMobileConfig(candidate);
    expect(result.valid).toBe(true);
    expect(result.config?.schemaVersion).toBe(1);
    expect(result.config?.navigation.map((item) => item.icon)).toEqual(['home', 'user']);
    expect(result.config?.features).toMatchObject({ loyalty: true, catalog: true, coupons: false });
  });

  test('rejects unknown schema versions and unrecognized canonical icons', () => {
    const config = createMockMobileConfig('tenant-a');
    expect(validateMobileConfig({ ...config, schemaVersion: 99 }).valid).toBe(false);
    expect(validateMobileConfig({
      ...config,
      schemaVersion: 1,
      navigation: config.navigation.map((item, index) =>
        index === 0 ? { ...item, icon: 'arbitrary-backend-icon' } : item
      ),
    }).valid).toBe(false);
  });

  test('rejects arbitrary theme fields, invalid colors and navigation overflow', () => {
    const config = createMockMobileConfig('tenant-a');
    const candidate = {
      ...config,
      theme: {
        ...config.theme,
        arbitraryCss: 'position: fixed',
        colors: { ...config.theme.colors, primary: 'javascript:alert(1)' },
      },
      navigation: [...config.navigation, ...config.navigation],
    };
    expect(validateMobileConfig(candidate).valid).toBe(false);
  });

  test('rejects unknown root properties and incomplete feature flags', () => {
    const config = createMockMobileConfig('tenant-a');
    const candidate = {
      ...config,
      executableCode: 'run()',
      features: { loyalty: true },
    };
    expect(validateMobileConfig(candidate).valid).toBe(false);
  });

  test('rejects arbitrary icons, duplicate routes and missing critical routes', () => {
    const config = createMockMobileConfig('tenant-a');
    expect(
      validateMobileConfig({
        ...config,
        navigation: config.navigation.map((item, index) =>
          index === 0 ? { ...item, icon: 'logo-javascript' } : item
        ),
      }).valid
    ).toBe(false);
    expect(
      validateMobileConfig({
        ...config,
        navigation: [config.navigation[0], config.navigation[0], config.navigation[3]],
      }).valid
    ).toBe(false);
    expect(
      validateMobileConfig({
        ...config,
        navigation: config.navigation.filter((item) => item.type !== 'profile'),
      }).valid
    ).toBe(false);
  });

  test('accepts future block types structurally but rejects executable block fields', () => {
    const config = createMockMobileConfig('tenant-a');
    const valid = {
      ...config,
      pages: {
        home: { blocks: [{ id: 'future-1', type: 'futureBlock', props: { title: 'Safe data' } }] },
      },
    };
    expect(validateMobileConfig(valid).valid).toBe(true);
    const invalid = {
      ...valid,
      pages: {
        home: {
          blocks: [{ ...valid.pages.home.blocks[0], executableCode: 'run()' }],
        },
      },
    };
    expect(validateMobileConfig(invalid).valid).toBe(false);
  });

  test('accepts whitelisted block feature requirements and rejects unknown ones', () => {
    const config = createMockMobileConfig('tenant-a');
    const withFeature = (feature: string) => ({
      ...config,
      pages: {
        home: { blocks: [{ id: 'widget', type: 'sectionHeader', feature, props: { title: 'Title' } }] },
      },
    });

    expect(validateMobileConfig(withFeature('catalog')).valid).toBe(true);
    expect(validateMobileConfig(withFeature('arbitraryFeature')).valid).toBe(false);
  });
});
