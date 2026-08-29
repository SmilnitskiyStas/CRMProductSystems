import { personalApiClient } from '@/lib/api-client';
import type { PagedResult } from '@/features/loyalty/types';
import type { ConsumerProfile, ConsumerProfileChange } from './types';

export async function getConsumerProfile(): Promise<ConsumerProfile> {
  return (await personalApiClient.get<ConsumerProfile>('/consumer/profile')).data;
}
export async function updateConsumerProfile(body: { fullName?: string; email?: string }): Promise<ConsumerProfile> {
  return (await personalApiClient.put<ConsumerProfile>('/consumer/profile', body)).data;
}
export async function changeConsumerPhone(body: { newPhone: string; currentPassword: string }): Promise<ConsumerProfile> {
  return (await personalApiClient.put<ConsumerProfile>('/consumer/profile/phone', body)).data;
}
export async function getConsumerProfileHistory(): Promise<PagedResult<ConsumerProfileChange>> {
  return (await personalApiClient.get<PagedResult<ConsumerProfileChange>>('/consumer/profile/history')).data;
}
