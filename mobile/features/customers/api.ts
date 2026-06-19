import { apiClient } from '@/lib/api-client';
import type {
  Customer,
  CustomerDetail,
  CustomersPage,
  CreateCustomerPayload,
  UpdateCustomerPayload,
} from './types';

export async function getCustomers(page = 1, search = ''): Promise<CustomersPage> {
  const { data } = await apiClient.get<CustomersPage>('/customers', {
    params: { page, pageSize: 50, search: search || undefined },
  });
  return data;
}

export async function getCustomer(id: string): Promise<CustomerDetail> {
  const { data } = await apiClient.get<CustomerDetail>(`/customers/${id}`);
  return data;
}

export async function createCustomer(payload: CreateCustomerPayload): Promise<Customer> {
  const { data } = await apiClient.post<Customer>('/customers', payload);
  return data;
}

export async function updateCustomer(
  id: string,
  payload: UpdateCustomerPayload
): Promise<Customer> {
  const { data } = await apiClient.put<Customer>(`/customers/${id}`, payload);
  return data;
}

export async function deleteCustomer(id: string): Promise<void> {
  await apiClient.delete(`/customers/${id}`);
}
