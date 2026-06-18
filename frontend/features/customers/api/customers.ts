import { api } from "@/lib/api";
import type {
  Customer,
  CustomerDetail,
  CustomersPage,
  CreateCustomerPayload,
  UpdateCustomerPayload,
} from "../types";

export const customersApi = {
  getAll(page: number, pageSize: number, search?: string): Promise<CustomersPage> {
    const params = new URLSearchParams();
    params.set("page", String(page));
    params.set("pageSize", String(pageSize));
    if (search) params.set("search", search);
    return api.get<CustomersPage>(`/api/customers?${params.toString()}`);
  },

  getById(id: string): Promise<CustomerDetail> {
    return api.get<CustomerDetail>(`/api/customers/${id}`);
  },

  create(data: CreateCustomerPayload): Promise<Customer> {
    return api.post<Customer>("/api/customers", data);
  },

  update(id: string, data: UpdateCustomerPayload): Promise<Customer> {
    return api.put<Customer>(`/api/customers/${id}`, data);
  },

  delete(id: string): Promise<void> {
    return api.delete<void>(`/api/customers/${id}`);
  },
};
