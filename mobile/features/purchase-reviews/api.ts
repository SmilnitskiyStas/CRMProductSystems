import { personalApiClient } from '@/lib/api-client';
import type { PagedResult } from '@/features/loyalty/types';
import type { PurchaseReview } from './types';
export const getMyReviews = async (tenantId:string) => (await personalApiClient.get<PagedResult<PurchaseReview>>('/consumer/reviews', { params:{tenantId} })).data;
export const createPurchaseReview = async (body:{tenantId:string;posTransactionId:string;rating:number;comment?:string}) => (await personalApiClient.post<PurchaseReview>('/consumer/reviews', body)).data;
