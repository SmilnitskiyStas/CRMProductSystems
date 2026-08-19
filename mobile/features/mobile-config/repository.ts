import { createMockMobileConfig } from './mock';
import { apiClient, personalApiClient } from '@/lib/api-client';
import { previewRequestHeaders } from '@/features/mobile-preview/policy';
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

export async function getPreviewMobileConfig(tenantId: string, token: string): Promise<unknown> {
  const { data } = await apiClient.get('/v1/mobile/config/preview', {
    params: { tenantId },
    headers: previewRequestHeaders(token),
  });
  return data;
}
