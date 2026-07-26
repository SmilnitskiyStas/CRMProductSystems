import { apiClient } from '@/lib/api-client';
import type {
  ConsumerAuthResult,
  ConsumerLoginRequest,
  ConsumerRegisterRequest,
  ConsumerUser,
} from '../types';

/** Wire shape of `ConsumerAuthResponse` (backend `Features/ConsumerAuth/Dtos/ConsumerAuthDtos.cs`). */
interface ConsumerAuthWireResponse {
  accessToken: string;
  consumerAccountId: string;
  fullName: string;
  phone: string;
}

function mapConsumerUser(wire: ConsumerAuthWireResponse): ConsumerUser {
  return {
    id: wire.consumerAccountId,
    fullName: wire.fullName,
    phone: wire.phone,
    role: 'consumer',
  };
}

export async function registerConsumer(body: ConsumerRegisterRequest): Promise<ConsumerAuthResult> {
  const { data } = await apiClient.post<ConsumerAuthWireResponse>('/consumer-auth/register', body);
  return { accessToken: data.accessToken, user: mapConsumerUser(data) };
}

export async function loginConsumer(body: ConsumerLoginRequest): Promise<ConsumerAuthResult> {
  const { data } = await apiClient.post<ConsumerAuthWireResponse>('/consumer-auth/login', body);
  return { accessToken: data.accessToken, user: mapConsumerUser(data) };
}
