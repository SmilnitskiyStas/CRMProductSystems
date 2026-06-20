import { apiClient } from '@/lib/api-client';
import type { AiAssistantRequest, AiAssistantResponse } from './types';

export async function sendAssistantMessage(
  req: AiAssistantRequest,
): Promise<AiAssistantResponse> {
  const { data } = await apiClient.post<AiAssistantResponse>('/ai/assistant', req);
  return data;
}
