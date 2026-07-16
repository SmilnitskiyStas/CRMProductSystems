import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { posApi } from "../api/pos";
import { ApiError } from "@/lib/api";
import type { OpenShiftRequest, CloseShiftRequest } from "../types";

const SHIFT_KEY = ["pos-shift-current"];

export function useCurrentShift() {
  return useQuery({
    queryKey: SHIFT_KEY,
    queryFn: () => posApi.getCurrentShift(),
    retry: (failureCount, error) => {
      // 404 means no open shift — not an error worth retrying
      if (error instanceof ApiError && error.status === 404) return false;
      return failureCount < 2;
    },
  });
}

export function useShiftSales(shiftId: string | undefined) {
  return useQuery({
    queryKey: ["pos-shift-sales", shiftId],
    queryFn: () => posApi.getShiftSales(shiftId!),
    enabled: !!shiftId,
  });
}

export function useOpenShift() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: OpenShiftRequest) => posApi.openShift(body),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: SHIFT_KEY });
    },
  });
}

export function useCloseShift() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body?: CloseShiftRequest) => posApi.closeShift(body),
    onSuccess: (closed) => {
      // Place the closed shift into the cache so the Z-report renders immediately
      qc.setQueryData(SHIFT_KEY, closed);
      qc.invalidateQueries({ queryKey: ["pos-shift-sales"] });
    },
  });
}
