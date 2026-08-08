import { useQuery } from '@tanstack/react-query';
import { getModulesSettings } from './api';

export function useModulesSettings(enabled = true) {
  return useQuery({
    queryKey: ['settings', 'modules'],
    queryFn: getModulesSettings,
    enabled,
    staleTime: 60_000,
  });
}
