import { useQuery } from '@tanstack/react-query';
import { getStockSummary, getAiOrders, getMovementProduct, getRecentMovements } from '../api/dashboardApi';

export function useDashboardStats(locationId?: string) {
  return useQuery({
    queryKey: ['dashboard', 'stock-summary', locationId],
    queryFn: () => getStockSummary(locationId),
  });
}

export function useAiOrders() {
  return useQuery({
    queryKey: ['dashboard', 'ai-orders'],
    queryFn: getAiOrders,
  });
}

export function useRecentMovements(locationId?: string, limit = 5) {
  return useQuery({
    queryKey: ['dashboard', 'recent-movements', locationId, limit],
    queryFn: () => getRecentMovements(limit, locationId),
  });
}

export function useMovementProduct(productId: string) {
  return useQuery({
    queryKey: ['items', productId],
    queryFn: () => getMovementProduct(productId),
    enabled: Boolean(productId),
    staleTime: 5 * 60_000,
  });
}
