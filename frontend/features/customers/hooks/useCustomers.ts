import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { customersApi } from "../api/customers";
import type { CreateCustomerPayload, UpdateCustomerPayload } from "../types";

const CUSTOMERS_KEY = ["customers"] as const;

export function useCustomers(page: number, pageSize: number, search: string) {
  return useQuery({
    queryKey: [...CUSTOMERS_KEY, page, pageSize, search],
    queryFn: () => customersApi.getAll(page, pageSize, search || undefined),
    placeholderData: (prev) => prev,
  });
}

export function useCustomer(id: string) {
  return useQuery({
    queryKey: [...CUSTOMERS_KEY, id],
    queryFn: () => customersApi.getById(id),
    enabled: !!id,
  });
}

export function useCustomerActivity(id: string) {
  return useQuery({
    queryKey: [...CUSTOMERS_KEY, id, "activity"],
    queryFn: () => customersApi.getActivity(id),
    enabled: !!id,
  });
}

export function useCustomerReceipts(id: string, page: number, pageSize: number) {
  return useQuery({
    queryKey: [...CUSTOMERS_KEY, id, "receipts", page, pageSize],
    queryFn: () => customersApi.getReceipts(id, page, pageSize),
    enabled: !!id,
    placeholderData: (prev) => prev,
  });
}

/**
 * TASK-621b. `enabled` is driven by the drawer's active tab (the "Історія профілю" tab), not
 * mount — the handoff explicitly asks for lazy-load-on-open since history can be long for an
 * old account.
 */
export function useCustomerProfileHistory(customerId: string, page: number, pageSize: number, enabled: boolean) {
  return useQuery({
    queryKey: [...CUSTOMERS_KEY, customerId, "profile-history", page, pageSize],
    queryFn: () => customersApi.getProfileHistory(customerId, page, pageSize),
    enabled: enabled && !!customerId,
    placeholderData: (prev) => prev,
  });
}

export function useCreateCustomer() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (data: CreateCustomerPayload) => customersApi.create(data),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: CUSTOMERS_KEY }),
  });
}

export function useUpdateCustomer() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, data }: { id: string; data: UpdateCustomerPayload }) =>
      customersApi.update(id, data),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: CUSTOMERS_KEY }),
  });
}

export function useDeleteCustomer() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => customersApi.delete(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: CUSTOMERS_KEY }),
  });
}
