import { api } from "@/lib/api";
import type { CreateLegalEntityDto, LegalEntityDto, UpdateLegalEntityDto } from "../types";

export const legalEntitiesApi = {
  getAll: () => api.get<LegalEntityDto[]>("/api/legal-entities"),
  getById: (id: string) => api.get<LegalEntityDto>(`/api/legal-entities/${id}`),
  create: (data: CreateLegalEntityDto) => api.post<LegalEntityDto>("/api/legal-entities", data),
  update: (id: string, data: UpdateLegalEntityDto) =>
    api.put<LegalEntityDto>(`/api/legal-entities/${id}`, data),
  deactivate: (id: string) => api.delete<void>(`/api/legal-entities/${id}`),
};
