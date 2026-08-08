import type { QueryKey } from '@tanstack/react-query';
import type { OfflineReadFamily } from './types';

export const OFFLINE_READ_SCHEMA_VERSION = 1;
export const OFFLINE_READ_ENVIRONMENT = 'production';
export const OFFLINE_READ_RETENTION_MS = 7 * 24 * 60 * 60 * 1000;
export const OFFLINE_READ_MAX_ENTRY_BYTES = 256 * 1024;
export const OFFLINE_READ_MAX_CACHE_BYTES = 2 * 1024 * 1024;

export function utf8ByteLength(value: string): number {
  let bytes = 0;
  for (const character of value) {
    const code = character.codePointAt(0) ?? 0;
    bytes += code <= 0x7f ? 1 : code <= 0x7ff ? 2 : code <= 0xffff ? 3 : 4;
  }
  return bytes;
}

const DAY = 24 * 60 * 60 * 1000;

const FAMILY_POLICY: Record<OfflineReadFamily, { softTtlMs: number }> = {
  schedules: { softTtlMs: DAY },
  'marketplace-suppliers': { softTtlMs: 6 * 60 * 60 * 1000 },
  'production-recipes': { softTtlMs: DAY },
};

function isOptionalString(value: unknown): boolean {
  return value == null || typeof value === 'string';
}

function isMarketplaceListScope(value: unknown): boolean {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return false;
  const scope = value as Record<string, unknown>;
  if (Object.keys(scope).some((key) => !['region', 'page', 'pageSize'].includes(key))) return false;
  return isOptionalString(scope.region)
    && (scope.page === undefined || (Number.isInteger(scope.page) && (scope.page as number) > 0))
    && (scope.pageSize === undefined || (Number.isInteger(scope.pageSize) && (scope.pageSize as number) > 0));
}

export function getOfflineReadFamily(queryKey: QueryKey): OfflineReadFamily | null {
  const root = queryKey[0];
  if (root === 'schedules' && queryKey.length === 3
    && isOptionalString(queryKey[1]) && isOptionalString(queryKey[2])) return root;
  if (root === 'marketplace-suppliers' && queryKey.length === 2
    && isMarketplaceListScope(queryKey[1])) return root;
  if (root === 'production-recipes' && queryKey.length === 2
    && typeof queryKey[1] === 'boolean') return root;
  return null;
}

export function getOfflineReadSoftTtl(family: OfflineReadFamily): number {
  return FAMILY_POLICY[family].softTtlMs;
}

function pickString(value: unknown): string | null {
  return typeof value === 'string' ? value : null;
}

function pickNumber(value: unknown): number | null {
  return typeof value === 'number' && Number.isFinite(value) ? value : null;
}

function pickBoolean(value: unknown): boolean | null {
  return typeof value === 'boolean' ? value : null;
}

function sanitizeSchedules(data: unknown): unknown | null {
  if (!Array.isArray(data)) return null;
  return data.map((raw) => {
    const item = raw as Record<string, unknown>;
    return {
      id: pickString(item.id), locationId: pickString(item.locationId),
      locationName: pickString(item.locationName), name: pickString(item.name),
      weekStart: pickString(item.weekStart), status: pickString(item.status),
      shiftCount: pickNumber(item.shiftCount), createdAt: pickString(item.createdAt),
    };
  }).filter((item) => item.id !== null);
}

function sanitizeMarketplace(data: unknown): unknown | null {
  if (!data || typeof data !== 'object') return null;
  const page = data as Record<string, unknown>;
  if (!Array.isArray(page.items)) return null;
  return {
    items: page.items.map((raw) => {
      const item = raw as Record<string, unknown>;
      return {
        id: pickString(item.id), name: pickString(item.name), region: pickString(item.region),
        plan: pickString(item.plan),
        categories: Array.isArray(item.categories) ? item.categories.filter((v): v is string => typeof v === 'string') : null,
        rating: pickNumber(item.rating), avgDeliveryDays: pickNumber(item.avgDeliveryDays),
        isPublic: pickBoolean(item.isPublic),
      };
    }).filter((item) => item.id !== null),
    total: pickNumber(page.total), page: pickNumber(page.page), pageSize: pickNumber(page.pageSize),
  };
}

function sanitizeRecipes(data: unknown): unknown | null {
  if (!Array.isArray(data)) return null;
  return data.map((raw) => {
    const item = raw as Record<string, unknown>;
    return {
      id: pickString(item.id), name: pickString(item.name), outputItemName: pickString(item.outputItemName),
      outputQty: pickNumber(item.outputQty), unit: pickString(item.unit),
      ingredientCount: pickNumber(item.ingredientCount), isActive: pickBoolean(item.isActive),
    };
  }).filter((item) => item.id !== null);
}

export function sanitizeOfflineReadData(family: OfflineReadFamily, data: unknown): unknown | null {
  if (family === 'schedules') return sanitizeSchedules(data);
  if (family === 'marketplace-suppliers') return sanitizeMarketplace(data);
  return sanitizeRecipes(data);
}

export const OFFLINE_READ_EXPLICIT_EXCLUSIONS = [
  'auth', 'settings', 'permissions', 'modules', 'loyalty', 'pos', 'stock', 'customers',
  'notifications', 'payment', 'two-factor', 'recovery', 'qr', 'mutations',
] as const;
