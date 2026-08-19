import AsyncStorage from '@react-native-async-storage/async-storage';

export const ACTIVE_TENANT_STORAGE_KEY = 'retail.active-tenant.v1';

interface StoredActiveTenant {
  version: 1;
  tenantId: string;
}

export function normalizeTenantId(value: unknown): string | null {
  if (typeof value !== 'string') return null;
  const normalized = value.trim();
  return normalized.length > 0 && normalized.length <= 128 ? normalized : null;
}

export async function readActiveTenantId(): Promise<string | null> {
  const raw = await AsyncStorage.getItem(ACTIVE_TENANT_STORAGE_KEY);
  if (!raw) return null;

  try {
    const parsed = JSON.parse(raw) as Partial<StoredActiveTenant>;
    if (parsed.version !== 1) {
      await AsyncStorage.removeItem(ACTIVE_TENANT_STORAGE_KEY);
      return null;
    }
    const tenantId = normalizeTenantId(parsed.tenantId);
    if (!tenantId) await AsyncStorage.removeItem(ACTIVE_TENANT_STORAGE_KEY);
    return tenantId;
  } catch {
    await AsyncStorage.removeItem(ACTIVE_TENANT_STORAGE_KEY);
    return null;
  }
}

export async function persistActiveTenantId(tenantId: string | null): Promise<void> {
  const normalized = normalizeTenantId(tenantId);
  if (!normalized) {
    await AsyncStorage.removeItem(ACTIVE_TENANT_STORAGE_KEY);
    return;
  }

  const value: StoredActiveTenant = { version: 1, tenantId: normalized };
  await AsyncStorage.setItem(ACTIVE_TENANT_STORAGE_KEY, JSON.stringify(value));
}
