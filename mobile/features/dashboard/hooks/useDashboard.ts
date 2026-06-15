import { useQuery } from '@tanstack/react-query';
import { getStockSummary, getAiOrders, getRecentMovements } from '../api/dashboardApi';

export function useDashboardStats() {
  return useQuery({
    queryKey: ['dashboard', 'stock-summary'],
    queryFn: getStockSummary,
  });
}

export function useAiOrders() {
  return useQuery({
    queryKey: ['dashboard', 'ai-orders'],
    queryFn: getAiOrders,
  });
}

export function useRecentMovements() {
  return useQuery({
    queryKey: ['dashboard', 'recent-movements'],
    queryFn: () => getRecentMovements(5),
  });
}
