import { api } from "@/lib/api";
import type { PlanogramEntry, ShelfItemDto, ShelfScanDto } from "../types";

const base = (locationId: string, zoneId: string) =>
  `/api/locations/${locationId}/zones/${zoneId}`;

export const shelvesApi = {
  getShelfItems: (locationId: string, zoneId: string, shelfId?: string) =>
    api.get<ShelfItemDto[]>(
      `${base(locationId, zoneId)}/shelf-items${shelfId ? `?shelfId=${shelfId}` : ""}`,
    ),
  setShelfItems: (locationId: string, zoneId: string, shelfId: string, items: PlanogramEntry[]) =>
    api.put<ShelfItemDto[]>(`${base(locationId, zoneId)}/shelf-items`, { shelfId, items }),
  createScan: (locationId: string, zoneId: string, file: Blob, shelfId?: string | null) => {
    const form = new FormData();
    form.append("file", file, "shelf.jpg");
    if (shelfId) form.append("shelfId", shelfId);
    return api.postForm<ShelfScanDto>(`${base(locationId, zoneId)}/scans`, form);
  },
  getScans: (locationId: string, zoneId: string, limit = 20) =>
    api.get<ShelfScanDto[]>(`${base(locationId, zoneId)}/scans?limit=${limit}`),
  getScan: (locationId: string, zoneId: string, scanId: string) =>
    api.get<ShelfScanDto>(`${base(locationId, zoneId)}/scans/${scanId}`),
};
