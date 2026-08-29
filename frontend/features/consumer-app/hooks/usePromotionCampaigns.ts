"use client";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { promotionCampaignsApi } from "../api/promotionCampaigns";
import type { UpsertPromotionCampaignRequest } from "../types";
export const PROMOTION_CAMPAIGNS_KEY = ["promotion-campaigns"] as const;
export const usePromotionCampaigns = () => useQuery({ queryKey: PROMOTION_CAMPAIGNS_KEY, queryFn: promotionCampaignsApi.getAll });
export const usePromotionCampaign = (id: string | null) => useQuery({ queryKey: [...PROMOTION_CAMPAIGNS_KEY,id], queryFn:()=>promotionCampaignsApi.getById(id!), enabled:!!id });
export function useSavePromotionCampaign() { const qc=useQueryClient(); return useMutation({ mutationFn:({id,body}:{id:string|null;body:UpsertPromotionCampaignRequest})=>id?promotionCampaignsApi.update(id,body):promotionCampaignsApi.create(body), onSuccess:()=>qc.invalidateQueries({queryKey:PROMOTION_CAMPAIGNS_KEY}) }); }
export function usePublishPromotionCampaign() { const qc=useQueryClient(); return useMutation({mutationFn:promotionCampaignsApi.publish,onSuccess:()=>qc.invalidateQueries({queryKey:PROMOTION_CAMPAIGNS_KEY})}); }
export function useCancelPromotionCampaign() { const qc=useQueryClient(); return useMutation({mutationFn:promotionCampaignsApi.cancel,onSuccess:()=>qc.invalidateQueries({queryKey:PROMOTION_CAMPAIGNS_KEY})}); }
export const useUploadPromotionCampaignImage = () => useMutation({mutationFn:({id,file}:{id:string;file:File})=>promotionCampaignsApi.uploadImage(id,file)});
