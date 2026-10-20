"use client";

import { useMemo, useRef, useState } from "react";
import { useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import { toast } from "sonner";
import { ArrowLeft, ArrowRight, Check } from "lucide-react";
import { Btn } from "@/components/ui/Btn";
import { useMe } from "@/features/auth/hooks/useAuth";
import { useLocations } from "@/features/locations/hooks/useLocations";
import { usePrimaryStoreId } from "@/lib/useStoreContext";
import { useQuery } from "@tanstack/react-query";
import { useCategories } from "@/features/inventory/hooks/useCategories";
import { ApiError } from "@/lib/api";
import { promotionRulesApi } from "../api/promotionRules";
import { usePromotionRule, useRuleAction, useSaveRule } from "../hooks/usePromotionRules";
import {
  INITIAL_STATE,
  defaultStoreIds,
  buildDefinition,
  stateFromDefinition,
  stepProblems,
  type WizardState,
} from "../lib/definition";
import { apiErrorText, issueKey } from "../lib/messages";
import type { RuleDetail, ValidationResult } from "../types";
import { ReviewStep } from "./ReviewStep";
import { StepAudience, StepParams, StepScope, StepTargets, StepType } from "./WizardSteps";
import { Note, cardStyle } from "./shared";

const STEP_KEYS = ["type", "items", "params", "scope", "audience", "review"] as const;
const LAST = STEP_KEYS.length;

function isValidationBody(b: unknown): b is ValidationResult {
  return !!b && typeof b === "object" && Array.isArray((b as ValidationResult).errors);
}

function Inner({ initial, initialDetail }: { initial: WizardState; initialDetail: RuleDetail | null }) {
  const t = useTranslations("Dashboard.promotionRules");
  const router = useRouter();
  const [state, setState] = useState<WizardState>(initial);
  const [step, setStep] = useState(1);
  const [detail, setDetail] = useState<RuleDetail | null>(initialDetail);
  const [problems, setProblems] = useState<string[]>([]);
  const [validation, setValidation] = useState<ValidationResult | null>(null);
  const [validating, setValidating] = useState(false);
  const [busy, setBusy] = useState(false);
  const savingRef = useRef(false);
  const saveMut = useSaveRule();
  const actionMut = useRuleAction();

  const patch = (p: Partial<WizardState>) => {
    setState((s) => ({ ...s, ...p }));
    setValidation(null); // edits invalidate the last server check
  };

  const ruleId = detail?.id ?? null;
  const isLive = detail?.status === "published" || detail?.status === "paused";

  async function save(): Promise<RuleDetail | null> {
    if (savingRef.current) return detail;
    savingRef.current = true;
    try {
      const data = {
        name: state.name.trim(),
        description: state.description.trim() || null,
        definition: buildDefinition(state),
      };
      const saved = await saveMut.mutateAsync({ id: ruleId ?? undefined, data });
      setDetail(saved);
      return saved;
    } finally {
      savingRef.current = false;
    }
  }

  async function runValidation(id: string) {
    setValidating(true);
    try {
      setValidation(await promotionRulesApi.validate(id));
    } catch (e) {
      toast.error(apiErrorText(e, t));
    } finally {
      setValidating(false);
    }
  }

  async function goTo(target: number) {
    // Autosave the draft on every step change (needs a name to create; errors never block navigation).
    let saved: RuleDetail | null = detail;
    if (state.type && state.name.trim()) {
      try {
        saved = await save();
      } catch (e) {
        toast.error(t("toast.autosaveFailed", { message: apiErrorText(e, t) }));
      }
    }
    setStep(target);
    if (target === LAST && saved) void runValidation(saved.id);
  }

  async function next() {
    const p = stepProblems(state, step);
    setProblems(p);
    if (p.length > 0) return;
    setBusy(true);
    await goTo(step + 1);
    setBusy(false);
  }

  async function back() {
    setProblems([]);
    setBusy(true);
    await goTo(step - 1);
    setBusy(false);
  }

  async function jump(target: number) {
    // From the review step's "go fix it" links.
    setProblems([]);
    setBusy(true);
    await goTo(target);
    setBusy(false);
  }

  async function saveDraft() {
    setBusy(true);
    try {
      await save();
      toast.success(t("toast.saved"));
      router.push("/promotion-rules");
    } catch (e) {
      toast.error(apiErrorText(e, t));
    } finally {
      setBusy(false);
    }
  }

  async function publish() {
    const p = stepProblems(state, LAST);
    setProblems(p);
    if (p.length > 0) return;
    setBusy(true);
    try {
      const saved = await save();
      if (!saved) return;
      await actionMut.mutateAsync({ id: saved.id, action: "publish" });
      toast.success(t("toast.published"));
      router.push("/promotion-rules");
    } catch (e) {
      if (e instanceof ApiError && isValidationBody(e.body)) {
        setValidation({ errors: e.body.errors, warnings: e.body.warnings ?? [] });
      } else {
        toast.error(apiErrorText(e, t));
      }
    } finally {
      setBusy(false);
    }
  }

  const hasErrors = (validation?.errors.length ?? 0) > 0;
  const canPublish = step === LAST && !hasErrors && !validating && !busy && stepProblems(state, LAST).length === 0;

  return (
    <div style={{ display: "grid", gridTemplateColumns: "minmax(0, 1fr) auto", gap: 24, alignItems: "start" }}>
      <div>
        {isLive && <Note tone="info">{t("wizard.liveBanner")}</Note>}

        <div style={{ display: "flex", gap: 6, flexWrap: "wrap", marginBottom: 20 }}>
          {STEP_KEYS.map((k, i) => {
            const n = i + 1;
            const done = n < step;
            const active = n === step;
            return (
              <div
                key={k}
                style={{
                  display: "flex",
                  alignItems: "center",
                  gap: 8,
                  padding: "6px 12px",
                  borderRadius: 999,
                  fontSize: 12.5,
                  fontWeight: 600,
                  background: active ? "#1D3461" : "transparent",
                  border: `1px solid ${active ? "#3B82F6" : "#1F2937"}`,
                  color: active ? "#BFDBFE" : done ? "#4ADE80" : "#6B7280",
                }}
              >
                <span>{done ? <Check size={13} /> : n}</span>
                {t(`steps.${k}`)}
              </div>
            );
          })}
        </div>

        <div style={{ ...cardStyle, padding: 24 }}>
          <h2 style={{ color: "#E8EDF5", fontSize: 17, fontWeight: 700, margin: "0 0 4px" }}>{t(`steps.${STEP_KEYS[step - 1]}`)}</h2>
          <p style={{ color: "#6B7280", fontSize: 13, margin: "0 0 18px" }}>{t(`stepHelp.${STEP_KEYS[step - 1]}`)}</p>

          {step === 1 && <StepType state={state} patch={patch} />}
          {step === 2 && <StepTargets state={state} patch={patch} />}
          {step === 3 && <StepParams state={state} patch={patch} />}
          {step === 4 && <StepScope state={state} patch={patch} />}
          {step === 5 && <StepAudience state={state} patch={patch} />}
          {step === 6 && (
            <ReviewStep state={state} ruleId={ruleId} validation={validation} validating={validating} onGoToStep={jump} />
          )}

          {problems.length > 0 && (
            <div style={{ marginTop: 14 }}>
              <Note tone="error">
                {problems.map((c) => (
                  <div key={c}>{t(`issues.${issueKey(c)}`)}</div>
                ))}
              </Note>
            </div>
          )}

          <div style={{ display: "flex", justifyContent: "space-between", gap: 10, marginTop: 22, flexWrap: "wrap" }}>
            <div>
              {step > 1 && (
                <Btn variant="ghost" icon={<ArrowLeft size={14} />} onClick={back} disabled={busy}>
                  {t("wizard.back")}
                </Btn>
              )}
            </div>
            <div style={{ display: "flex", gap: 10 }}>
              <Btn variant="ghost" onClick={saveDraft} disabled={busy || !state.type || !state.name.trim()}>
                {t("wizard.saveDraft")}
              </Btn>
              {step < LAST ? (
                <Btn icon={<ArrowRight size={14} />} onClick={next} disabled={busy}>
                  {t("wizard.next")}
                </Btn>
              ) : (
                <Btn variant="success" onClick={publish} disabled={!canPublish}>
                  {t("wizard.publish")}
                </Btn>
              )}
            </div>
          </div>
        </div>
      </div>

      {detail && detail.versions.length > 0 && (
        <aside style={{ ...cardStyle, width: 260 }}>
          <div style={{ color: "#E8EDF5", fontWeight: 700, fontSize: 13, marginBottom: 10 }}>{t("versions.title")}</div>
          <div style={{ display: "grid", gap: 10 }}>
            {detail.versions.map((v) => (
              <div key={v.id} style={{ fontSize: 12, color: "#9CA3AF" }}>
                <div style={{ color: "#E8EDF5", fontWeight: 600 }}>
                  {t("versions.version", { n: v.versionNumber })}
                  {v.isPublished && (
                    <span style={{ marginLeft: 8, color: "#4ADE80", fontSize: 11 }}>{t("versions.published")}</span>
                  )}
                  {v.versionNumber === detail.currentVersion.versionNumber && !v.isPublished && (
                    <span style={{ marginLeft: 8, color: "#FBBF24", fontSize: 11 }}>{t("versions.draft")}</span>
                  )}
                </div>
                <div>
                  {new Date(v.createdAt).toLocaleString("uk-UA")}
                  {v.createdByName ? ` - ${v.createdByName}` : ""}
                </div>
                {v.changeComment && <div style={{ color: "#6B7280" }}>{v.changeComment}</div>}
              </div>
            ))}
          </div>
        </aside>
      )}
    </div>
  );
}

/** Create (no `ruleId`) or edit a promotion rule through the 6-step wizard. */
export function RuleWizard({ ruleId }: { ruleId?: string }) {
  const t = useTranslations("Dashboard.promotionRules");
  const { data: detail, isLoading, error } = usePromotionRule(ruleId);
  const { data: categories, isLoading: catsLoading } = useCategories();
  const { data: me, isLoading: meLoading } = useMe();
  const { data: locations, isLoading: locsLoading } = useLocations();
  const primaryStoreId = usePrimaryStoreId();

  const itemIds = detail?.currentVersion.definition.targets?.itemIds ?? [];
  const { data: itemLookup, isLoading: itemsLoading } = useQuery({
    queryKey: ["promotion-rules", "items-by-id", detail?.id, itemIds.join(",")],
    queryFn: () => promotionRulesApi.lookupItemsByIds(itemIds),
    enabled: !!detail && itemIds.length > 0,
    staleTime: 60_000,
    retry: false,
  });

  const initial = useMemo(() => {
    if (!detail) return null;
    const cats = new Map((categories ?? []).map((c) => [c.id, c.name]));
    const items = new Map((itemLookup ?? []).map((i) => [i.id, i.name]));
    return stateFromDefinition(detail.name, detail.description, detail.currentVersion.definition, {
      categories: cats,
      items,
    });
  }, [detail, categories, itemLookup]);

  const newInitial = useMemo<WizardState>(() => {
    const storeIds = defaultStoreIds(me?.role, primaryStoreId, (locations ?? []).map((l) => l.id));
    return { ...INITIAL_STATE, allStores: storeIds.length === 0, storeIds };
  }, [me?.role, primaryStoreId, locations]);

  if (!ruleId) {
    if (meLoading || locsLoading) return <div style={{ color: "#6B7280" }}>{t("loading")}</div>;
    return <Inner initial={newInitial} initialDetail={null} />;
  }
  if (isLoading || (detail && (catsLoading || itemsLoading))) return <div style={{ color: "#6B7280" }}>{t("loading")}</div>;
  if (error || !detail || !initial) return <Note tone="error">{error?.message ?? t("notFound")}</Note>;
  return <Inner key={detail.id} initial={initial} initialDetail={detail} />;
}
