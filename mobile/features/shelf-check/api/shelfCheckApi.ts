import { apiClient } from '@/lib/api-client';
import type { PlanogramShelf, ShelfCheckLocation, ShelfScan } from '../types';

export const SHELF_SCAN_UPLOAD_TIMEOUT_MS = 90_000;

export async function getShelfCheckLocations(): Promise<ShelfCheckLocation[]> {
  const { data } = await apiClient.get<ShelfCheckLocation[]>('/locations');
  return Array.isArray(data) ? data.filter((l) => l.isActive) : [];
}

export async function getZoneShelves(locationId: string, zoneId: string): Promise<PlanogramShelf[]> {
  const { data } = await apiClient.get<PlanogramShelf[]>(
    `/locations/${locationId}/zones/${zoneId}/shelves`,
  );
  return Array.isArray(data) ? data : [];
}

export async function getZoneScans(locationId: string, zoneId: string, limit = 20): Promise<ShelfScan[]> {
  const { data } = await apiClient.get<ShelfScan[]>(
    `/locations/${locationId}/zones/${zoneId}/scans`,
    { params: { limit } },
  );
  return Array.isArray(data) ? data : [];
}

export interface CreateShelfScanInput {
  locationId: string;
  zoneId: string;
  shelfId?: string | null;
  photoUri: string;
}

export async function createShelfScan(input: CreateShelfScanInput): Promise<ShelfScan> {
  const form = new FormData();
  // React Native FormData file part: { uri, name, type }.
  form.append('file', {
    uri: input.photoUri,
    name: 'shelf.jpg',
    type: 'image/jpeg',
  } as unknown as Blob);
  if (input.shelfId) form.append('shelfId', input.shelfId);

  const { data } = await apiClient.post<ShelfScan>(
    `/locations/${input.locationId}/zones/${input.zoneId}/scans`,
    form,
    {
      headers: { 'Content-Type': 'multipart/form-data' },
      timeout: SHELF_SCAN_UPLOAD_TIMEOUT_MS,
      transformRequest: (body) => body,
    },
  );
  return data;
}

/** Maps backend errors of POST .../scans to a Ukrainian user message. */
export function shelfScanErrorMessage(error: unknown): string {
  const err = error as { response?: { status?: number; data?: { error?: string } }; code?: string };
  const status = err.response?.status;
  switch (status) {
    case 400: return err.response?.data?.error ?? 'Фото не підходить (формат JPG/PNG/WebP, до 5 МБ).';
    case 403: return 'Недостатньо прав для перевірки полиць.';
    case 404: return 'Зону не знайдено.';
    case 422: return 'Для цієї полиці не задано товарів у планограмі';
    case 429: return 'Забагато перевірок. Зачекайте кілька хвилин і спробуйте знову.';
    case 502: return 'AI не зміг обробити фото. Спробуйте зробити фото ще раз.';
    case 503: return 'AI-перевірка полиць не налаштована для вашої компанії.';
    default:
      if (err.code === 'ECONNABORTED') return 'Час очікування вичерпано. Спробуйте ще раз.';
      return err.response?.data?.error ?? 'Не вдалося надіслати фото. Перевірте зʼєднання.';
  }
}
