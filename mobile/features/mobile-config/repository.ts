import { createMockMobileConfig } from './mock';
import { personalApiClient } from '@/lib/api-client';
import type { MobileConfig } from './types';

export interface MobileConfigRepository {
  getConfig: (tenantId: string) => Promise<unknown>;
  source: 'mock' | 'published';
}

export const mockMobileConfigRepository: MobileConfigRepository = {
  source: 'mock',
  getConfig: async (tenantId): Promise<MobileConfig> => createMockMobileConfig(tenantId),
};

export const publishedMobileConfigRepository: MobileConfigRepository = {
  source: 'published',
  getConfig: async (tenantId) => {
    const { data } = await personalApiClient.get('/v1/mobile/config', { params: { tenantId } });
    return data;
  },
};
