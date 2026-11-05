import { api } from "@/lib/api";
import type {
  BeautyOwnerInviteResponse,
  BeautyTenantStateDto,
  BeautyTenantStatus,
  CreateBeautyTenantRequest,
  CreateBeautyTenantResponse,
} from "../types";

const base = "/api/provider/beauty/tenants";
const enc = (id: string) => encodeURIComponent(id);

// ShelfGuard -> Beauty CRM control plane proxy (ProviderOnly). Responses with `inviteUrl`
// carry a one-time secret: callers must not cache, log or persist them.
export const providerBeautyApi = {
  get: (id: string) => api.get<BeautyTenantStateDto>(`${base}/${enc(id)}`),
  create: (req: CreateBeautyTenantRequest) => api.post<CreateBeautyTenantResponse>(base, req),
  suspend: (id: string) => api.post<{ status: BeautyTenantStatus }>(`${base}/${enc(id)}/suspend`, {}),
  resume: (id: string) => api.post<{ status: BeautyTenantStatus }>(`${base}/${enc(id)}/resume`, {}),
  ownerInvite: (id: string) => api.post<BeautyOwnerInviteResponse>(`${base}/${enc(id)}/owner-invite`, {}),
  updateModules: (id: string, modules: string[]) =>
    api.put<{ modules: string[] }>(`${base}/${enc(id)}/modules`, { modules }),
};
