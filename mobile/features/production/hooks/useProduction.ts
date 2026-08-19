import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  cancelProductionOrder,
  completeProductionOrder,
  createProductionOrder,
  getProductionOrderById,
  getProductionOrders,
  getRecipeById,
  getRecipes,
} from '../api';
import type { ProductionOrderCreate } from '../types';

export function useRecipes(includeInactive = false, enabled = true) {
  return useQuery({
    queryKey: ['production-recipes', includeInactive],
    queryFn: () => getRecipes(includeInactive),
    enabled,
  });
}

export function useRecipeById(id: string) {
  return useQuery({
    queryKey: ['production-recipe', id],
    queryFn: () => getRecipeById(id),
    enabled: !!id,
  });
}

export function useProductionOrders(params?: { status?: string }) {
  return useQuery({
    queryKey: ['production-orders', params],
    queryFn: () => getProductionOrders(params),
  });
}

export function useProductionOrderById(id: string) {
  return useQuery({
    queryKey: ['production-order', id],
    queryFn: () => getProductionOrderById(id),
    enabled: !!id,
  });
}

export function useCreateProductionOrder() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (dto: ProductionOrderCreate) => createProductionOrder(dto),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['production-orders'] });
    },
  });
}

export function useCompleteProductionOrder() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => completeProductionOrder(id),
    onSuccess: (_data, id) => {
      void qc.invalidateQueries({ queryKey: ['production-orders'] });
      void qc.invalidateQueries({ queryKey: ['production-order', id] });
    },
  });
}

export function useCancelProductionOrder() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => cancelProductionOrder(id),
    onSuccess: (_data, id) => {
      void qc.invalidateQueries({ queryKey: ['production-orders'] });
      void qc.invalidateQueries({ queryKey: ['production-order', id] });
    },
  });
}
