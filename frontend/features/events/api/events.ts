import { api } from "@/lib/api";
import type {
  CreateCoefficientPayload, DemandEvent, EventCoefficient, UpsertEventPayload,
} from "../types";

export const eventsApi = {
  getAll: (from: string, to: string) =>
    api.get<DemandEvent[]>(`/api/events?from=${from}&to=${to}`),

  create: (payload: UpsertEventPayload) =>
    api.post<DemandEvent>("/api/events", payload),

  update: (id: string, payload: UpsertEventPayload) =>
    api.put<DemandEvent>(`/api/events/${id}`, payload),

  delete: (id: string) => api.delete<void>(`/api/events/${id}`),

  addCoefficient: (eventId: string, payload: CreateCoefficientPayload) =>
    api.post<EventCoefficient>(`/api/events/${eventId}/coefficients`, payload),

  updateCoefficient: (eventId: string, coefId: string, coefficient: number) =>
    api.put<EventCoefficient>(`/api/events/${eventId}/coefficients/${coefId}`, { coefficient }),

  seedDefaults: () =>
    api.post<{ eventsCreated: number; coefficientsCreated: number }>("/api/events/seed-defaults"),
};
