import { apiClient } from '@/lib/api-client';
import type { ConsumerRegisterRequest, MobileLoginRequest, MobileLoginResponse } from '../types';

export async function mobileLogin(body: MobileLoginRequest): Promise<MobileLoginResponse> {
  const { data } = await apiClient.post<MobileLoginResponse>('/mobile-auth/login', body);
  return data;
}

export async function mobileRegister(body: ConsumerRegisterRequest): Promise<MobileLoginResponse> {
  const { data } = await apiClient.post<MobileLoginResponse>('/mobile-auth/register', body);
  return data;
}
