import { api } from "@/lib/api";
import type {
  CreateCoefficientPayload, DemandEvent, EventCoefficient, UpsertEventPayload,
} from "../types";

export const eventsApi = {
  /** GET /api/events — events in [from, to], optionally filtered by store (repeated
   * ?storeIds=, omitted entirely when empty — same serialization style as usersApi.getAll). */
  getAll: (from: string, to: string, storeIds?: string[]) => {
    const qs = new URLSearchParams({ from, to });
    for (const id of storeIds ?? []) qs.append("storeIds", id);
    return api.get<DemandEvent[]>(`/api/events?${qs.toString()}`);
  },

  create: (payload: UpsertEventPayload) =>
    api.post<DemandEvent>("/api/events", payload),

  update: (id: string, payload: UpsertEventPayload) =>
    api.put<DemandEvent>(`/api/events/${id}`, payload),

  delete: (id: string) => api.delete<void>(`/api/events/${id}`),

  addCoefficient: (eventId: string, payload: CreateCoefficientPayload) =>
    api.post<EventCoefficient>(`/api/events/${eventId}/coefficients`, payload),

  updateCoefficient: (eventId: string, coefId: string, coefficient: number) =>
    api.put<EventCoefficient>(`/api/events/${eventId}/coefficients/${coefId}`, { coefficient }),

  removeCoefficient: (eventId: string, coefId: string) =>
    api.delete<void>(`/api/events/${eventId}/coefficients/${coefId}`),

  seedDefaults: () =>
    api.post<{ eventsCreated: number; coefficientsCreated: number }>("/api/events/seed-defaults"),
};
