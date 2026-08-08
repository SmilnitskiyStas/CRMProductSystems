import { apiClient } from '@/lib/api-client';
import type { ModulesSettings } from './types';

export async function getModulesSettings(): Promise<ModulesSettings> {
  const { data } = await apiClient.get<ModulesSettings>('/settings/modules');
  return {
    businessType: typeof data.businessType === 'string' ? data.businessType : '',
    modules: Array.isArray(data.modules) ? data.modules.filter((item): item is string => typeof item === 'string') : [],
  };
}
