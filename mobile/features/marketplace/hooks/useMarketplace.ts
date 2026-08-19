import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  createReview,
  getSupplierById,
  getSupplierItems,
  getSuppliers,
  searchSuppliers,
} from '../api';

export function useSuppliers(params: {
  region?: string;
  page?: number;
  pageSize?: number;
}, enabled = true) {
  return useQuery({
    queryKey: ['marketplace-suppliers', params],
    queryFn: () => getSuppliers(params),
    enabled,
  });
}

export function useSearchSuppliers(itemName: string, region?: string, enabled = true) {
  return useQuery({
    queryKey: ['marketplace-search', itemName, region],
    queryFn: () => searchSuppliers(itemName, region),
    enabled: enabled && itemName.trim().length > 0,
  });
}

export function useSupplierById(id: string) {
  return useQuery({
    queryKey: ['marketplace-supplier', id],
    queryFn: () => getSupplierById(id),
    enabled: !!id,
  });
}

export function useSupplierItems(id: string) {
  return useQuery({
    queryKey: ['marketplace-supplier-items', id],
    queryFn: () => getSupplierItems(id),
    enabled: !!id,
  });
}

export function useCreateReview(supplierId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ rating, comment }: { rating: number; comment: string | null }) =>
      createReview(supplierId, rating, comment),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['marketplace-supplier', supplierId] });
    },
  });
}
