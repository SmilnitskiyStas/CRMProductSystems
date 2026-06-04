import type { CreateProductPayload, Product, UpdateProductPayload } from "../types";

const API_BASE = process.env.NEXT_PUBLIC_API_URL ?? "http://localhost:5000";

async function apiFetch<T>(path: string, init?: RequestInit): Promise<T> {
  const res = await fetch(`${API_BASE}${path}`, {
    headers: { "Content-Type": "application/json", ...init?.headers },
    ...init,
  });

  if (!res.ok) {
    const body = await res.text().catch(() => "");
    throw new Error(body || `HTTP ${res.status}`);
  }

  // 204 No Content — nothing to parse
  if (res.status === 204) return undefined as T;

  return res.json() as Promise<T>;
}

export const productsApi = {
  getAll: () => apiFetch<Product[]>("/api/products"),

  getById: (id: string) => apiFetch<Product>(`/api/products/${id}`),

  create: (payload: CreateProductPayload) =>
    apiFetch<Product>("/api/products", {
      method: "POST",
      body: JSON.stringify(payload),
    }),

  update: (id: string, payload: UpdateProductPayload) =>
    apiFetch<Product>(`/api/products/${id}`, {
      method: "PUT",
      body: JSON.stringify(payload),
    }),

  delete: (id: string) =>
    apiFetch<void>(`/api/products/${id}`, { method: "DELETE" }),
};
