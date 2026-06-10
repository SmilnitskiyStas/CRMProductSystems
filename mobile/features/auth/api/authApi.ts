import { apiClient } from '@/lib/api-client';
import type { LoginRequest, LoginResponse } from '../types';

export async function login(body: LoginRequest): Promise<LoginResponse> {
  const { data } = await apiClient.post<LoginResponse>('/auth/login', body);
  return data;
}

export async function logout(): Promise<void> {
  await apiClient.post('/auth/logout');
}

export async function getMe() {
  const { data } = await apiClient.get('/auth/me');
  return data;
}
