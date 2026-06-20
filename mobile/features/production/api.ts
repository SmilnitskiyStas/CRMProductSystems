import { apiClient } from '@/lib/api-client';
import type {
  ProductionCompleteResult,
  ProductionOrderCreate,
  ProductionOrderDetail,
  ProductionOrderListItem,
  RecipeDetail,
  RecipeListItem,
} from './types';

export async function getRecipes(includeInactive = false): Promise<RecipeListItem[]> {
  const { data } = await apiClient.get('/production/recipes', {
    params: includeInactive ? { includeInactive: true } : undefined,
  });
  return data;
}

export async function getRecipeById(id: string): Promise<RecipeDetail> {
  const { data } = await apiClient.get(`/production/recipes/${id}`);
  return data;
}

export async function getProductionOrders(params?: {
  status?: string;
  recipeId?: string;
  locationId?: string;
}): Promise<ProductionOrderListItem[]> {
  const { data } = await apiClient.get('/production/orders', { params });
  return data;
}

export async function getProductionOrderById(id: string): Promise<ProductionOrderDetail> {
  const { data } = await apiClient.get(`/production/orders/${id}`);
  return data;
}

export async function createProductionOrder(
  dto: ProductionOrderCreate,
): Promise<ProductionOrderListItem> {
  const { data } = await apiClient.post('/production/orders', dto);
  return data;
}

export async function completeProductionOrder(id: string): Promise<ProductionCompleteResult> {
  const { data } = await apiClient.post(`/production/orders/${id}/complete`);
  return data;
}

export async function cancelProductionOrder(id: string): Promise<void> {
  await apiClient.post(`/production/orders/${id}/cancel`);
}
