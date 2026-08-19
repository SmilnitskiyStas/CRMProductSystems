import AsyncStorage from '@react-native-async-storage/async-storage';
import {
  ACTIVE_TENANT_STORAGE_KEY,
  normalizeTenantId,
  persistActiveTenantId,
  readActiveTenantId,
} from '../storage';

describe('active tenant storage', () => {
  beforeEach(async () => AsyncStorage.clear());

  test('normalizes valid identifiers and rejects unsafe values', () => {
    expect(normalizeTenantId(' tenant-a ')).toBe('tenant-a');
    expect(normalizeTenantId('')).toBeNull();
    expect(normalizeTenantId('x'.repeat(129))).toBeNull();
    expect(normalizeTenantId(123)).toBeNull();
  });

  test('persists and restores a versioned active tenant', async () => {
    await persistActiveTenantId(' tenant-a ');
    await expect(readActiveTenantId()).resolves.toBe('tenant-a');
  });

  test.each(['{broken', JSON.stringify({ version: 2, tenantId: 'tenant-a' })])(
    'fails closed and removes invalid persisted state: %s',
    async (raw) => {
      await AsyncStorage.setItem(ACTIVE_TENANT_STORAGE_KEY, raw);
      await expect(readActiveTenantId()).resolves.toBeNull();
      await expect(AsyncStorage.getItem(ACTIVE_TENANT_STORAGE_KEY)).resolves.toBeNull();
    }
  );

  test('clears persisted selection when tenant is null', async () => {
    await persistActiveTenantId('tenant-a');
    await persistActiveTenantId(null);
    await expect(readActiveTenantId()).resolves.toBeNull();
  });
});
