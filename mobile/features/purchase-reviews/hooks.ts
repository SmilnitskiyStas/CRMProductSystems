import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { createPurchaseReview, getMyReviews } from './api';
export const useMyReviews=(tenantId:string|null)=>useQuery({queryKey:['purchase-reviews',tenantId],queryFn:()=>getMyReviews(tenantId!),enabled:!!tenantId});
export function useCreatePurchaseReview(){const qc=useQueryClient();return useMutation({mutationFn:createPurchaseReview,onSuccess:(r)=>void qc.invalidateQueries({queryKey:['purchase-reviews',r.tenantId]})});}
