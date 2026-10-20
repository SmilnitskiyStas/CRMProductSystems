import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { shelvesApi } from "../api/shelves";

export function useShelfItems(locationId: string, zoneId: string) {
  return useQuery({
    queryKey: ["locations", locationId, "zones", zoneId, "shelf-items"],
    queryFn: () => shelvesApi.getShelfItems(locationId, zoneId),
    enabled: !!locationId && !!zoneId,
  });
}

export function useShelfScans(locationId: string, zoneId: string) {
  return useQuery({
    queryKey: ["locations", locationId, "zones", zoneId, "scans"],
    queryFn: () => shelvesApi.getScans(locationId, zoneId, 20),
    enabled: !!locationId && !!zoneId,
  });
}

export function useCreateShelfScan(locationId: string, zoneId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ file, shelfId }: { file: Blob; shelfId?: string | null }) =>
      shelvesApi.createScan(locationId, zoneId, file, shelfId),
    onSuccess: () =>
      qc.invalidateQueries({ queryKey: ["locations", locationId, "zones", zoneId, "scans"] }),
  });
}

/** Re-fetches the zone planogram after the page persisted it. */
export function invalidateShelfItems(
  qc: ReturnType<typeof useQueryClient>,
  locationId: string,
  zoneId: string,
) {
  return qc.invalidateQueries({
    queryKey: ["locations", locationId, "zones", zoneId, "shelf-items"],
  });
}
