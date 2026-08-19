import AsyncStorage from '@react-native-async-storage/async-storage';
import { loadMobileConfig, loadPreviewMobileConfig } from '../loader';
import { createMockMobileConfig } from '../mock';
import { persistLastValidMobileConfig, readLastValidMobileConfig } from '../storage';

describe('mobile configuration loading policy', () => {
  beforeEach(async () => AsyncStorage.clear());

  test('validates and persists a freshly loaded config', async () => {
    const config = createMockMobileConfig('tenant-a');
    const result = await loadMobileConfig('tenant-a', async () => config);
    expect(result).toMatchObject({ config, source: 'mock', error: null });
  });

  test('marks a canonical repository result as published', async () => {
    const config = createMockMobileConfig('tenant-a');
    const result = await loadMobileConfig('tenant-a', async () => config, 'published');
    expect(result.source).toBe('published');
  });

  test('uses only the same tenant last-valid config after a load failure', async () => {
    const config = createMockMobileConfig('tenant-a');
    await persistLastValidMobileConfig(config);
    const result = await loadMobileConfig('tenant-a', async () => {
      throw new Error('offline');
    });
    expect(result.source).toBe('last-valid');
    expect(result.config.tenant.id).toBe('tenant-a');
    expect(result.error?.message).toBe('offline');
  });

  test('falls back safely when remote and cache are unavailable', async () => {
    const result = await loadMobileConfig('tenant-b', async () => ({ schemaVersion: 999 }));
    expect(result.source).toBe('safe-default');
    expect(result.config.tenant.id).toBe('tenant-b');
    expect(result.error?.message).toBe('INVALID_MOBILE_CONFIG');
  });

  test('validates preview without writing it to production last-valid storage', async () => {
    const config = createMockMobileConfig('tenant-preview');
    const loaded = await loadPreviewMobileConfig(
      'tenant-preview',
      'preview-token-1234',
      async () => config
    );
    expect(loaded).toEqual(config);
    expect(await readLastValidMobileConfig('tenant-preview')).toBeNull();
  });

  test('rejects cross-tenant preview configuration', async () => {
    await expect(
      loadPreviewMobileConfig('tenant-a', 'preview-token-1234', async () =>
        createMockMobileConfig('tenant-b')
      )
    ).rejects.toThrow('INVALID_PREVIEW_MOBILE_CONFIG');
  });

  test('never uses Tenant A cache after switching to Tenant B', async () => {
    await loadMobileConfig('tenant-a', async () => createMockMobileConfig('tenant-a'));
    await loadMobileConfig('tenant-b', async () => createMockMobileConfig('tenant-b'));
    const result = await loadMobileConfig('tenant-b', async () => {
      throw new Error('timeout');
    });
    expect(result.source).toBe('last-valid');
    expect(result.config.tenant.id).toBe('tenant-b');
  });

  test('a newer valid configuration replaces the older cached version', async () => {
    await loadMobileConfig('tenant-a', async () => createMockMobileConfig('tenant-a'));
    const updated = { ...createMockMobileConfig('tenant-a'), configVersion: 2 };
    await loadMobileConfig('tenant-a', async () => updated);
    expect((await readLastValidMobileConfig('tenant-a'))?.configVersion).toBe(2);
  });
});
