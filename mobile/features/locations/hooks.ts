import { useQuery } from '@tanstack/react-query';
import { getWorkspaceLocations } from './api';

export function useWorkspaceLocations() {
  return useQuery({
    queryKey: ['workspace-locations'],
    queryFn: getWorkspaceLocations,
    staleTime: 5 * 60_000,
  });
}
