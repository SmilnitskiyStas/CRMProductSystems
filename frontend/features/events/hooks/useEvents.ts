import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { eventsApi } from "../api/events";
import type { CreateCoefficientPayload, UpsertEventPayload } from "../types";

const EVENTS_KEY = ["demand-events"] as const;

export function useEvents(from: string, to: string) {
  return useQuery({
    queryKey: [...EVENTS_KEY, from, to],
    queryFn: () => eventsApi.getAll(from, to),
  });
}

function useInvalidate() {
  const qc = useQueryClient();
  return () => qc.invalidateQueries({ queryKey: EVENTS_KEY });
}

export function useCreateEvent() {
  const invalidate = useInvalidate();
  return useMutation({
    mutationFn: (payload: UpsertEventPayload) => eventsApi.create(payload),
    onSuccess: invalidate,
  });
}

export function useUpdateEvent() {
  const invalidate = useInvalidate();
  return useMutation({
    mutationFn: ({ id, payload }: { id: string; payload: UpsertEventPayload }) =>
      eventsApi.update(id, payload),
    onSuccess: invalidate,
  });
}

export function useDeleteEvent() {
  const invalidate = useInvalidate();
  return useMutation({
    mutationFn: (id: string) => eventsApi.delete(id),
    onSuccess: invalidate,
  });
}

export function useAddCoefficient() {
  const invalidate = useInvalidate();
  return useMutation({
    mutationFn: ({ eventId, payload }: { eventId: string; payload: CreateCoefficientPayload }) =>
      eventsApi.addCoefficient(eventId, payload),
    onSuccess: invalidate,
  });
}

export function useUpdateCoefficient() {
  const invalidate = useInvalidate();
  return useMutation({
    mutationFn: ({ eventId, coefId, coefficient }: { eventId: string; coefId: string; coefficient: number }) =>
      eventsApi.updateCoefficient(eventId, coefId, coefficient),
    onSuccess: invalidate,
  });
}

export function useSeedDefaults() {
  const invalidate = useInvalidate();
  return useMutation({
    mutationFn: () => eventsApi.seedDefaults(),
    onSuccess: invalidate,
  });
}
