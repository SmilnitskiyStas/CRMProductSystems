import AsyncStorage from '@react-native-async-storage/async-storage';
import { ACTIVE_TENANT_STORAGE_KEY } from '../storage';
import { useActiveTenantStore } from '../store';

describe('active tenant store', () => {
  beforeEach(async () => {
    await AsyncStorage.clear();
    useActiveTenantStore.setState({ activeTenantId: null, hydrationStatus: 'idle' });
  });

  test('hydrates the last active tenant', async () => {
    await AsyncStorage.setItem(
      ACTIVE_TENANT_STORAGE_KEY,
      JSON.stringify({ version: 1, tenantId: 'tenant-b' })
    );

    await useActiveTenantStore.getState().hydrate();

    expect(useActiveTenantStore.getState()).toMatchObject({
      activeTenantId: 'tenant-b',
      hydrationStatus: 'ready',
    });
  });

  test('persists tenant switches and reset', async () => {
    await useActiveTenantStore.getState().setActiveTenantId('tenant-a');
    expect(useActiveTenantStore.getState().activeTenantId).toBe('tenant-a');

    await useActiveTenantStore.getState().reset();
    expect(useActiveTenantStore.getState()).toMatchObject({
      activeTenantId: null,
      hydrationStatus: 'ready',
    });
    await expect(AsyncStorage.getItem(ACTIVE_TENANT_STORAGE_KEY)).resolves.toBeNull();
  });
});
