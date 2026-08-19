import AsyncStorage from '@react-native-async-storage/async-storage';
import { createMockMobileConfig } from '../mock';
import {
  mobileConfigStorageKey,
  persistLastValidMobileConfig,
  readLastValidMobileConfig,
  readLastValidMobileConfigEntry,
} from '../storage';

describe('last valid mobile configuration storage', () => {
  beforeEach(async () => AsyncStorage.clear());

  test('stores and restores a tenant-scoped validated config', async () => {
    const config = createMockMobileConfig('tenant-a');
    await persistLastValidMobileConfig(config, 123456);
    await expect(readLastValidMobileConfig('tenant-a')).resolves.toEqual(config);
    await expect(readLastValidMobileConfigEntry('tenant-a')).resolves.toEqual({
      config,
      cachedAt: 123456,
    });
    await expect(readLastValidMobileConfig('tenant-b')).resolves.toBeNull();
  });

  test('reads legacy config-only entries without inventing a timestamp', async () => {
    const config = createMockMobileConfig('tenant-a');
    await AsyncStorage.setItem(mobileConfigStorageKey('tenant-a'), JSON.stringify(config));
    await expect(readLastValidMobileConfigEntry('tenant-a')).resolves.toEqual({
      config,
      cachedAt: null,
    });
  });

  test('removes corrupt and cross-tenant values', async () => {
    const key = mobileConfigStorageKey('tenant-a');
    await AsyncStorage.setItem(key, JSON.stringify(createMockMobileConfig('tenant-b')));
    await expect(readLastValidMobileConfig('tenant-a')).resolves.toBeNull();
    await expect(AsyncStorage.getItem(key)).resolves.toBeNull();
  });

  test('never persists an invalid config', async () => {
    const invalid = { ...createMockMobileConfig('tenant-a'), schemaVersion: 9 };
    await expect(persistLastValidMobileConfig(invalid)).rejects.toThrow('INVALID_MOBILE_CONFIG');
  });
});
