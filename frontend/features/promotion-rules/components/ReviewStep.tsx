"use client";

import { useState } from "react";
import { useTranslations } from "next-intl";
import { Trash2 } from "lucide-react";
import { Btn } from "@/components/ui/Btn";
import { useLocations } from "@/features/locations/hooks/useLocations";
import { usePrimaryStoreId } from "@/lib/useStoreContext";
import { useSimulate } from "../hooks/usePromotionRules";
import { buildDefinition, parseIntStrict, type WizardState } from "../lib/definition";
import { fieldKey, formatMoney, issueKey, segmentsText, stepOfField, summarizeDefinition } from "../lib/messages";
import type { ItemRef, SimulateResult, ValidationIssue, ValidationResult } from "../types";
import { ItemPicker } from "./ItemPicker";
import { cardStyle, inputStyle, Note } from "./shared";

function IssueList({
  issues,
  tone,
  onGoToStep,
}: {
  issues: ValidationIssue[];
  tone: "error" | "warn";
  onGoToStep: (step: number) => void;
}) {
  const t = useTranslations("Dashboard.promotionRules");
  if (issues.length === 0) return null;
  const color = tone === "error" ? "#F87171" : "#FBBF24";
  return (
    <ul style={{ listStyle: "none", margin: "0 0 12px", padding: 0, display: "grid", gap: 6 }}>
      {issues.map((i, idx) => {
        const fk = fieldKey(i.field);
        return (
          <li
            key={`${i.code}-${idx}`}
            style={{
              border: `1px solid ${tone === "error" ? "#7F1D1D" : "#78500F"}`,
              background: tone === "error" ? "#2d0a0a" : "#2a1f08",
              color,
              borderRadius: 8,
              padding: "8px 12px",
              fontSize: 13,
            }}
          >
            <div>{t(`issues.${issueKey(i.code)}`)}</div>
            <div style={{ fontSize: 11.5, opacity: 0.85, marginTop: 2 }}>
              {fk ? t(`fields.${fk}`) : i.field}
              {i.field && " · "}
              <Btn variant="link" size="xs" onClick={() => onGoToStep(stepOfField(i.field))}>
                {t("review.goFix")}
              </Btn>
            </div>
          </li>
        );
      })}
    </ul>
  );
}

interface SimLine {
  item: ItemRef;
  quantity: string;
}

/** "Test a receipt" panel: runs POST /simulate with the unsaved definition. */
function Simulator({ state, ruleId }: { state: WizardState; ruleId: string | null }) {
  const t = useTranslations("Dashboard.promotionRules");
  const { data: stores = [] } = useLocations();
  const primary = usePrimaryStoreId();
  const sim = useSimulate();
  const [lines, setLines] = useState<SimLine[]>([]);
  const [result, setResult] = useState<SimulateResult | null>(null);

  const storeId = (!state.allStores ? state.storeIds[0] : undefined) ?? primary ?? stores[0]?.id;

  function run() {
    if (!storeId || lines.length === 0) return;
    sim.mutate(
      {
        ruleId,
        definition: buildDefinition(state),
        storeId,
        at: null,
        membership: null,
        lines: lines.map((l) => ({ itemId: l.item.id, quantity: parseIntStrict(l.quantity) ?? 1, unitPrice: null })),
      },
      { onSuccess: setResult },
    );
  }

  return (
    <div style={cardStyle}>
      <div style={{ color: "#E8EDF5", fontWeight: 700, fontSize: 14, marginBottom: 4 }}>{t("review.simTitle")}</div>
      <div style={{ color: "#6B7280", fontSize: 12, marginBottom: 12 }}>{t("review.simHelp")}</div>
      <ItemPicker
        excludeIds={lines.map((l) => l.item.id)}
        onPick={(i) => {
          setLines([...lines, { item: { id: i.id, name: i.name }, quantity: "1" }]);
          setResult(null);
        }}
      />
      {lines.length > 0 && (
        <div style={{ marginTop: 12, display: "grid", gap: 8 }}>
          {lines.map((l, idx) => (
            <div key={l.item.id} style={{ display: "flex", gap: 10, alignItems: "center" }}>
              <span style={{ flex: 1, color: "#E8EDF5", fontSize: 13 }}>{l.item.name}</span>
              <input
                aria-label={t("review.qty")}
                inputMode="numeric"
                style={{ ...inputStyle, width: 70 }}
                value={l.quantity}
                onChange={(e) => {
                  const v = e.target.value.replace(/\D/g, "");
                  setLines(lines.map((x, i) => (i === idx ? { ...x, quantity: v } : x)));
                  setResult(null);
                }}
              />
              <button
                type="button"
                aria-label="remove"
                onClick={() => {
                  setLines(lines.filter((_, i) => i !== idx));
                  setResult(null);
                }}
                style={{ background: "none", border: "none", color: "#6B7280", cursor: "pointer" }}
              >
                <Trash2 size={14} />
              </button>
            </div>
          ))}
        </div>
      )}
      <div style={{ marginTop: 12 }}>
        <Btn onClick={run} disabled={lines.length === 0 || !storeId || sim.isPending}>
          {sim.isPending ? t("review.simRunning") : t("review.simRun")}
        </Btn>
      </div>
      {sim.isError && <div style={{ color: "#F87171", fontSize: 12, marginTop: 8 }}>{sim.error.message}</div>}
      {result && (
        <div style={{ marginTop: 14, borderTop: "1px solid #1F2937", paddingTop: 12 }}>
          {result.lines.map((l) => (
            <div key={l.itemId} style={{ marginBottom: 10, fontSize: 13 }}>
              <div style={{ color: "#E8EDF5", fontWeight: 600 }}>{l.name}</div>
              <div style={{ color: "#9CA3AF" }}>
                {segmentsText(l.segments)} = <b style={{ color: "#E8EDF5" }}>{formatMoney(l.lineTotal)}</b>
                {l.lineTotal < l.regularLineTotal && (
                  <span style={{ color: "#6B7280" }}> ({t("review.was", { amount: formatMoney(l.regularLineTotal) })})</span>
                )}
              </div>
            </div>
          ))}
          <div style={{ display: "flex", justifyContent: "space-between", color: "#E8EDF5", fontSize: 14, fontWeight: 700 }}>
            <span>{t("review.total")}</span>
            <span>{formatMoney(result.total)}</span>
          </div>
          <div style={{ display: "flex", justifyContent: "space-between", color: "#4ADE80", fontSize: 13, marginTop: 4 }}>
            <span>{t("review.savings")}</span>
            <span>{formatMoney(result.savings)}</span>
          </div>
          {result.savings <= 0 && <div style={{ color: "#FBBF24", fontSize: 12, marginTop: 8 }}>{t("review.noEffect")}</div>}
        </div>
      )}
    </div>
  );
}

export function ReviewStep({
  state,
  ruleId,
  validation,
  validating,
  onGoToStep,
}: {
  state: WizardState;
  ruleId: string | null;
  validation: ValidationResult | null;
  validating: boolean;
  onGoToStep: (step: number) => void;
}) {
  const t = useTranslations("Dashboard.promotionRules");
  const def = buildDefinition(state);
  return (
    <div style={{ display: "grid", gap: 16 }}>
      <div style={cardStyle}>
        <div style={{ color: "#E8EDF5", fontWeight: 700, fontSize: 15 }}>{state.name}</div>
        <div style={{ color: "#93C5FD", fontSize: 13, marginTop: 4 }}>
          {state.type ? t(`types.${state.type}.title`) : ""} - {summarizeDefinition(def, t)}
        </div>
        <div style={{ color: "#6B7280", fontSize: 12, marginTop: 6 }}>
          {t("review.targetsLine", { items: state.items.length, categories: state.categories.length })}
        </div>
      </div>

      <div>
        <div style={{ color: "#9CA3AF", fontSize: 12, fontWeight: 600, marginBottom: 8, textTransform: "uppercase" }}>
          {t("review.check")}
        </div>
        {validating && <div style={{ color: "#6B7280", fontSize: 13 }}>{t("review.validating")}</div>}
        {!validating && !validation && <div style={{ color: "#6B7280", fontSize: 13 }}>{t("review.notChecked")}</div>}
        {validation && (
          <>
            <IssueList issues={validation.errors} tone="error" onGoToStep={onGoToStep} />
            <IssueList issues={validation.warnings} tone="warn" onGoToStep={onGoToStep} />
            {validation.errors.length === 0 && validation.warnings.length === 0 && (
              <Note tone="info">{t("review.allGood")}</Note>
            )}
            {validation.errors.length === 0 && validation.warnings.length > 0 && (
              <div style={{ color: "#9CA3AF", fontSize: 12 }}>{t("review.warningsOk")}</div>
            )}
          </>
        )}
      </div>

      <Simulator state={state} ruleId={ruleId} />
    </div>
  );
}
