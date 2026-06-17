import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import {
  getWorkOrders,
  getWorkOrder,
  completeWorkOrder,
  addWorkOrderLine,
  getCustomers,
  createCustomer,
  getServiceCatalog,
} from '../api';
import type { CreateWorkOrderLinePayload, CreateCustomerPayload } from '../types';

// Work Orders
export function useWorkOrders(status?: string) {
  return useQuery({
    queryKey: ['auto-service-work-orders', status ?? 'all'],
    queryFn: () => getWorkOrders(status),
  });
}

export function useWorkOrder(id: string) {
  return useQuery({
    queryKey: ['auto-service-work-order', id],
    queryFn: () => getWorkOrder(id),
    enabled: !!id,
  });
}

export function useCompleteWorkOrder() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => completeWorkOrder(id),
    onSuccess: (_, id) => {
      void qc.invalidateQueries({ queryKey: ['auto-service-work-orders'] });
      void qc.invalidateQueries({ queryKey: ['auto-service-work-order', id] });
    },
  });
}

export function useAddWorkOrderLine(workOrderId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (payload: CreateWorkOrderLinePayload) =>
      addWorkOrderLine(workOrderId, payload),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['auto-service-work-order', workOrderId] });
    },
  });
}

// Customers
export function useCustomers() {
  return useQuery({
    queryKey: ['auto-service-customers'],
    queryFn: getCustomers,
  });
}

export function useCreateCustomer() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (payload: CreateCustomerPayload) => createCustomer(payload),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['auto-service-customers'] });
    },
  });
}

// Service catalog
export function useServiceCatalog() {
  return useQuery({
    queryKey: ['auto-service-catalog'],
    queryFn: getServiceCatalog,
  });
}
