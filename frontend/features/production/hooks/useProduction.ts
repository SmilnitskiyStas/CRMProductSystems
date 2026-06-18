"use client";

import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { productionApi } from "../api/production-api";
import type {
  RecipeCreateDto,
  RecipeUpdateDto,
  ProductionOrderCreateDto,
  ProductionOrderUpdateDto,
} from "../types";

// ─── Query Keys ───────────────────────────────────────────────────────────────

export const PRODUCTION_KEYS = {
  recipes: (includeInactive?: boolean) =>
    ["production", "recipes", includeInactive] as const,
  recipe: (id: string) => ["production", "recipe", id] as const,
  orders: (status?: string) => ["production", "orders", status] as const,
  order: (id: string) => ["production", "order", id] as const,
  items: ["production", "items"] as const,
  locations: ["production", "locations"] as const,
};

// ─── Recipes ─────────────────────────────────────────────────────────────────

export function useRecipes(includeInactive = false) {
  return useQuery({
    queryKey: PRODUCTION_KEYS.recipes(includeInactive),
    queryFn: () => productionApi.getRecipes(includeInactive),
    staleTime: 30_000,
  });
}

export function useRecipe(id: string) {
  return useQuery({
    queryKey: PRODUCTION_KEYS.recipe(id),
    queryFn: () => productionApi.getRecipe(id),
    enabled: !!id,
    staleTime: 30_000,
  });
}

export function useCreateRecipe() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: RecipeCreateDto) => productionApi.createRecipe(body),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["production", "recipes"] });
    },
  });
}

export function useUpdateRecipe(id: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: RecipeUpdateDto) => productionApi.updateRecipe(id, body),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["production", "recipes"] });
      queryClient.invalidateQueries({ queryKey: PRODUCTION_KEYS.recipe(id) });
    },
  });
}

export function useDeactivateRecipe() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => productionApi.deactivateRecipe(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["production", "recipes"] });
    },
  });
}

// ─── Production Orders ───────────────────────────────────────────────────────

export function useProductionOrders(status?: string) {
  return useQuery({
    queryKey: PRODUCTION_KEYS.orders(status),
    queryFn: () => productionApi.getOrders(status),
    staleTime: 30_000,
  });
}

export function useProductionOrder(id: string) {
  return useQuery({
    queryKey: PRODUCTION_KEYS.order(id),
    queryFn: () => productionApi.getOrder(id),
    enabled: !!id,
    staleTime: 30_000,
  });
}

export function useCreateProductionOrder() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: ProductionOrderCreateDto) =>
      productionApi.createOrder(body),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["production", "orders"] });
    },
  });
}

export function useUpdateProductionOrder(id: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: ProductionOrderUpdateDto) =>
      productionApi.updateOrder(id, body),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: PRODUCTION_KEYS.order(id) });
      queryClient.invalidateQueries({ queryKey: ["production", "orders"] });
    },
  });
}

export function useCompleteProductionOrder(id: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => productionApi.completeOrder(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: PRODUCTION_KEYS.order(id) });
      queryClient.invalidateQueries({ queryKey: ["production", "orders"] });
    },
  });
}

export function useCancelProductionOrder(id: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => productionApi.cancelOrder(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: PRODUCTION_KEYS.order(id) });
      queryClient.invalidateQueries({ queryKey: ["production", "orders"] });
    },
  });
}

// ─── Selectors ────────────────────────────────────────────────────────────────

export function useProductionItems() {
  return useQuery({
    queryKey: PRODUCTION_KEYS.items,
    queryFn: productionApi.getItems,
    staleTime: 60_000,
  });
}

export function useProductionLocations() {
  return useQuery({
    queryKey: PRODUCTION_KEYS.locations,
    queryFn: productionApi.getLocations,
    staleTime: 60_000,
  });
}
