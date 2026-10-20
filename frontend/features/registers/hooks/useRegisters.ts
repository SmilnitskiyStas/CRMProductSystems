import { useMemo } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useMe } from "@/features/auth/hooks/useAuth";
import { useLocations } from "@/features/locations/hooks/useLocations";
import { registersApi } from "../api/registers";
import type { CreateRegisterPayload, StatsPeriod, UpdateRegisterPayload } from "../types";

const KEY = ["registers"] as const;

/**
 * Stores the current user may manage registers/PINs for. `useLocations()` is already scoped
 * server-side to the user's assigned stores (ADR-022 user_locations), which is the same
 * assignment the backend enforces (RegisterService.CanManageStoreAsync); enterprise admin /
 * provider see every store. Waits for the current user so nothing flashes before scoping.
 */
export function useManageableStores() {
  const { data: stores = [], ...rest } = useLocations();
  const { data: user } = useMe();
  const manageable = useMemo(() => (user ? stores : []), [stores, user]);
  return { data: manageable, ...rest };
}

export function useRegisters(storeId?: string) {
  return useQuery({ queryKey: [...KEY, "list", storeId ?? "all"], queryFn: () => registersApi.list(storeId) });
}

export function useRegisterOperators(storeId?: string) {
  return useQuery({
    queryKey: [...KEY, "operators", storeId ?? "all"],
    queryFn: () => registersApi.operators(storeId),
    refetchInterval: 30_000,
  });
}

function useInvalidate() {
  const qc = useQueryClient();
  return () => qc.invalidateQueries({ queryKey: KEY });
}

export function useCreateRegister() {
  const invalidate = useInvalidate();
  return useMutation({
    mutationFn: (data: CreateRegisterPayload) => registersApi.create(data),
    onSuccess: invalidate,
  });
}

export function useUpdateRegister() {
  const invalidate = useInvalidate();
  return useMutation({
    mutationFn: ({ id, data }: { id: string; data: UpdateRegisterPayload }) => registersApi.update(id, data),
    onSuccess: invalidate,
  });
}

export function useIssueActivationCode() {
  const invalidate = useInvalidate();
  return useMutation({ mutationFn: (id: string) => registersApi.issueActivationCode(id), onSuccess: invalidate });
}

export function useRevokeRegister() {
  const invalidate = useInvalidate();
  return useMutation({ mutationFn: (id: string) => registersApi.revoke(id), onSuccess: invalidate });
}

export function useUnlockRegister() {
  const invalidate = useInvalidate();
  return useMutation({ mutationFn: (id: string) => registersApi.unlock(id), onSuccess: invalidate });
}

export function usePosEmployees(storeId: string | undefined) {
  return useQuery({
    queryKey: [...KEY, "employees", storeId],
    queryFn: () => registersApi.employees(storeId!),
    enabled: !!storeId,
  });
}

export function useGeneratePin() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (userId: string) => registersApi.generatePin(userId),
    onSuccess: () => qc.invalidateQueries({ queryKey: [...KEY, "employees"] }),
  });
}

export function useClearPin() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (userId: string) => registersApi.clearPin(userId),
    onSuccess: () => qc.invalidateQueries({ queryKey: [...KEY, "employees"] }),
  });
}

/** [from, to) in UTC for the local-calendar period ending now. */
export function periodRange(period: StatsPeriod): { from: string; to: string } {
  const now = new Date();
  const start = new Date(now.getFullYear(), now.getMonth(), now.getDate());
  if (period === "7d") start.setDate(start.getDate() - 6);
  if (period === "30d") start.setDate(start.getDate() - 29);
  const end = new Date(now.getFullYear(), now.getMonth(), now.getDate() + 1);
  return { from: start.toISOString(), to: end.toISOString() };
}

export function useRegisterStats(period: StatsPeriod, storeId?: string) {
  return useQuery({
    queryKey: [...KEY, "stats", period, storeId ?? "all"],
    queryFn: () => {
      const { from, to } = periodRange(period);
      return registersApi.stats(from, to, storeId);
    },
  });
}

export function useRegisterSessions(registerId: string | undefined, page: number, pageSize: number) {
  return useQuery({
    queryKey: [...KEY, "sessions", registerId, page, pageSize],
    queryFn: () => registersApi.sessions(registerId!, page, pageSize),
    enabled: !!registerId,
    placeholderData: (prev) => prev,
  });
}

export function useRegisterShifts(registerId: string | undefined, page: number, pageSize: number) {
  return useQuery({
    queryKey: [...KEY, "shifts", registerId, page, pageSize],
    queryFn: () => registersApi.shifts(registerId!, page, pageSize),
    enabled: !!registerId,
    placeholderData: (prev) => prev,
  });
}
