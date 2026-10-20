import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  createShelfScan,
  getShelfCheckLocations,
  getZoneScans,
  getZoneShelves,
  type CreateShelfScanInput,
} from '../api/shelfCheckApi';

export function useShelfCheckLocations() {
  return useQuery({
    queryKey: ['shelf-check', 'locations'],
    queryFn: getShelfCheckLocations,
    staleTime: 5 * 60_000,
  });
}

export function useZoneShelves(locationId?: string, zoneId?: string) {
  return useQuery({
    queryKey: ['shelf-check', 'shelves', locationId, zoneId],
    queryFn: () => getZoneShelves(locationId as string, zoneId as string),
    enabled: !!locationId && !!zoneId,
  });
}

export function useZoneScans(locationId?: string, zoneId?: string) {
  return useQuery({
    queryKey: ['shelf-check', 'scans', locationId, zoneId],
    queryFn: () => getZoneScans(locationId as string, zoneId as string),
    enabled: !!locationId && !!zoneId,
  });
}

export function useCreateShelfScan() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (input: CreateShelfScanInput) => createShelfScan(input),
    onSuccess: (_scan, input) =>
      void qc.invalidateQueries({ queryKey: ['shelf-check', 'scans', input.locationId, input.zoneId] }),
  });
}
