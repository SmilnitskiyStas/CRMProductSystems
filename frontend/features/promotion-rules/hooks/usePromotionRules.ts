import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { promotionRulesApi } from "../api/promotionRules";
import type { RuleStatus, SaveRulePayload, SimulatePayload } from "../types";

const KEY = ["promotion-rules"] as const;

export function usePromotionRules(status?: RuleStatus, storeId?: string) {
  return useQuery({
    queryKey: [...KEY, "list", status ?? "all", storeId ?? "all"],
    queryFn: () => promotionRulesApi.list(status, storeId),
  });
}

export function usePromotionRule(id: string | undefined) {
  return useQuery({
    queryKey: [...KEY, "detail", id],
    queryFn: () => promotionRulesApi.get(id!),
    enabled: !!id,
  });
}

export function useSaveRule() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, data }: { id?: string; data: SaveRulePayload }) =>
      id ? promotionRulesApi.update(id, data) : promotionRulesApi.create(data),
    onSuccess: () => qc.invalidateQueries({ queryKey: KEY }),
  });
}

export type RuleAction = "publish" | "pause" | "resume" | "archive";

export function useRuleAction() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, action }: { id: string; action: RuleAction }) => promotionRulesApi[action](id),
    onSuccess: () => qc.invalidateQueries({ queryKey: KEY }),
  });
}

export function useValidateRule() {
  return useMutation({ mutationFn: (id: string) => promotionRulesApi.validate(id) });
}

export function useSimulate() {
  return useMutation({ mutationFn: (data: SimulatePayload) => promotionRulesApi.simulate(data) });
}
