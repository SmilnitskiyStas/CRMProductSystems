"use client";

import { useState, useEffect } from "react";
import { useTranslations } from "next-intl";
import { RegionSelect } from "@/features/geo/components/RegionSelect";
import { DeliveryCoverageEditor } from "@/features/geo/components/DeliveryCoverageEditor";
import { CollapsibleSection } from "@/components/ui/CollapsibleSection";
import {
  useMySupplierProfile,
  useUpdateMySupplierProfile,
  useItemCategories,
} from "../hooks/useMarketplace";
import type { SupplierProfileUpdateRequest, SupplierPlan } from "../types";

// ─── Main form ────────────────────────────────────────────────────────────────

const EMPTY_FORM: SupplierProfileUpdateRequest = {
  region: "",
  categories: [],
  website: "",
  deliveryCoverage: null,
  workingHours: "",
  paymentTerms: "",
  isPublic: false,
  plan: "free",
};

export function SupplierProfileForm() {
  const t = useTranslations("Dashboard.marketplace.profileForm");
  const tPlan = useTranslations("Dashboard.marketplace.planLabel");
  const { data, isLoading, isError } = useMySupplierProfile();
  const { data: itemCategories = [] } = useItemCategories();
  const { mutate, isPending } = useUpdateMySupplierProfile();

  const [form, setForm] = useState<SupplierProfileUpdateRequest>(EMPTY_FORM);
  const [saved, setSaved] = useState(false);
  const [saveError, setSaveError] = useState<string | null>(null);

  useEffect(() => {
    if (data) setForm(data);
  }, [data]);

  const labelStyle: React.CSSProperties = {
    color: "#9CA3AF",
    fontSize: 12,
    marginBottom: 6,
    display: "block",
  };

  const hintStyle: React.CSSProperties = {
    color: "#6B7280",
    fontSize: 11,
    margin: "-2px 0 6px",
  };

  const inputStyle: React.CSSProperties = {
    width: "100%",
    padding: "9px 12px",
    background: "#0D1117",
    border: "1px solid #1F2937",
    borderRadius: 8,
    color: "#E8EDF5",
    fontSize: 13,
    outline: "none",
    boxSizing: "border-box",
  };

  const fieldStyle: React.CSSProperties = {
    display: "flex",
    flexDirection: "column",
  };

  // Category is single + read-only after tenant creation (TASK-665/667).
  const categoryLabel = itemCategories.find(
    (c) => c.key === (form.categories ?? [])[0]
  )?.labelUa;

  function handleSave() {
    setSaveError(null);
    setSaved(false);
    // `categories` is intentionally omitted — the update endpoint ignores it (TASK-665).
    mutate(
      {
        region: form.region,
        website: form.website || undefined,
        deliveryCoverage: form.deliveryCoverage ?? null,
        workingHours: form.workingHours || undefined,
        paymentTerms: form.paymentTerms || undefined,
        isPublic: form.isPublic,
        plan: form.plan,
      },
      {
        onSuccess: () => setSaved(true),
        onError: (err) => {
          setSaveError(err instanceof Error ? err.message : t("saveErrorDefault"));
        },
      }
    );
  }

  if (isLoading) {
    return (
      <div style={{ color: "#4B5563", fontSize: 13, padding: "16px 0" }}>
        {t("loading")}
      </div>
    );
  }

  if (isError) {
    return (
      <div style={{ color: "#F87171", fontSize: 13, padding: "16px 0" }}>
        {t("loadError")}
      </div>
    );
  }

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 16, maxWidth: 600 }}>
      <div>
        <h2 style={{ color: "#E8EDF5", fontSize: 16, fontWeight: 600, margin: 0 }}>
          {t("title")}
        </h2>
        <p style={{ color: "#4B5563", fontSize: 13, marginTop: 6 }}>
          {t("subtitle")}
        </p>
      </div>

      {/* General — origin region + website */}
      <CollapsibleSection title={t("sectionGeneralLabel")} defaultOpen>
        <div style={{ display: "flex", flexDirection: "column", gap: 16 }}>
          {/* Region — supplier HQ / dispatch origin, single code (TASK-655/667) */}
          <div style={fieldStyle}>
            <label style={labelStyle}>{t("regionLabel")}</label>
            <p style={hintStyle}>{t("regionHint")}</p>
            <RegionSelect
              value={form.region || null}
              onChange={(code) => setForm({ ...form, region: code ?? "" })}
              allowEmpty
              placeholder={t("regionSelectPlaceholder")}
            />
          </div>

          {/* Website */}
          <div style={fieldStyle}>
            <label style={labelStyle}>{t("websiteLabel")}</label>
            <input
              type="url"
              value={form.website ?? ""}
              onChange={(e) => setForm({ ...form, website: e.target.value })}
              placeholder="https://example.com"
              style={{ ...inputStyle, opacity: form.plan === "free" ? 0.5 : 1 }}
              disabled={form.plan === "free"}
            />
          </div>
        </div>
      </CollapsibleSection>

      {/* Category — read-only, set at tenant creation (TASK-665/667) */}
      <CollapsibleSection title={t("categoryReadonlyLabel")} defaultOpen>
        <div
          style={{
            color: categoryLabel ? "#E8EDF5" : "#6B7280",
            fontSize: 13,
          }}
        >
          {categoryLabel ?? t("categoryNone")}
        </div>
      </CollapsibleSection>

      {/* Delivery coverage — structured taxonomy (TASK-655), NOT premium-gated */}
      <CollapsibleSection title={t("deliveryCoverageLabel")} defaultOpen>
        <DeliveryCoverageEditor
          value={form.deliveryCoverage ?? null}
          onChange={(deliveryCoverage) => setForm({ ...form, deliveryCoverage })}
        />
      </CollapsibleSection>

      {/* Schedule & payment */}
      <CollapsibleSection title={t("sectionScheduleLabel")} defaultOpen>
        <div style={{ display: "flex", flexDirection: "column", gap: 16 }}>
          <div style={fieldStyle}>
            <label style={labelStyle}>{t("workingHoursLabel")}</label>
            <input
              type="text"
              value={form.workingHours ?? ""}
              onChange={(e) => setForm({ ...form, workingHours: e.target.value })}
              placeholder={t("workingHoursPlaceholder")}
              style={{ ...inputStyle, opacity: form.plan === "free" ? 0.5 : 1 }}
              disabled={form.plan === "free"}
            />
          </div>

          <div style={fieldStyle}>
            <label style={labelStyle}>{t("paymentTermsLabel")}</label>
            <input
              type="text"
              value={form.paymentTerms ?? ""}
              onChange={(e) => setForm({ ...form, paymentTerms: e.target.value })}
              placeholder={t("paymentTermsPlaceholder")}
              style={{ ...inputStyle, opacity: form.plan === "free" ? 0.5 : 1 }}
              disabled={form.plan === "free"}
            />
          </div>
        </div>
      </CollapsibleSection>

      {/* Is public toggle */}
      <div
        style={{
          display: "flex",
          alignItems: "center",
          justifyContent: "space-between",
          padding: "14px 16px",
          background: "#0D1117",
          border: "1px solid #1F2937",
          borderRadius: 10,
        }}
      >
        <div>
          <div style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 600 }}>
            {t("visibleToggleTitle")}
          </div>
          <div style={{ color: "#4B5563", fontSize: 12, marginTop: 3 }}>
            {t("visibleToggleBody")}
          </div>
        </div>
        <button
          type="button"
          role="switch"
          aria-checked={form.isPublic}
          onClick={() => setForm({ ...form, isPublic: !form.isPublic })}
          style={{
            width: 42,
            height: 24,
            borderRadius: 12,
            background: form.isPublic ? "#1D4ED8" : "#1F2937",
            border: "none",
            cursor: "pointer",
            position: "relative",
            transition: "background 0.15s",
            flexShrink: 0,
          }}
        >
          <div
            style={{
              width: 18,
              height: 18,
              borderRadius: "50%",
              background: "#E8EDF5",
              position: "absolute",
              top: 3,
              left: form.isPublic ? 21 : 3,
              transition: "left 0.15s",
            }}
          />
        </button>
      </div>

      {/* Plan selector */}
      <div style={fieldStyle}>
        <label style={labelStyle}>{t("planFieldLabel")}</label>
        <div style={{ display: "flex", gap: 10 }}>
          {(["free", "premium"] as SupplierPlan[]).map((plan) => {
            const active = form.plan === plan;
            return (
              <button
                key={plan}
                type="button"
                onClick={() => setForm({ ...form, plan })}
                style={{
                  flex: 1,
                  padding: "10px 0",
                  borderRadius: 8,
                  border: `1px solid ${active ? "#3B82F6" : "#1F2937"}`,
                  background: active ? "#1D3461" : "transparent",
                  color: active ? "#93C5FD" : "#6B7280",
                  fontSize: 13,
                  fontWeight: active ? 600 : 400,
                  cursor: "pointer",
                  transition: "all 0.1s",
                }}
              >
                {tPlan(plan)}
              </button>
            );
          })}
        </div>
      </div>

      {/* Feedback */}
      {saved && (
        <div
          style={{
            padding: "10px 14px",
            background: "#052e16",
            border: "1px solid #166534",
            borderRadius: 8,
            color: "#4ADE80",
            fontSize: 13,
          }}
        >
          {t("saved")}
        </div>
      )}
      {saveError && (
        <div
          style={{
            padding: "10px 14px",
            background: "#1c0707",
            border: "1px solid #7f1d1d",
            borderRadius: 8,
            color: "#F87171",
            fontSize: 13,
          }}
        >
          {saveError}
        </div>
      )}

      {/* Save */}
      <button
        type="button"
        onClick={handleSave}
        disabled={isPending}
        style={{
          alignSelf: "flex-start",
          padding: "10px 24px",
          borderRadius: 8,
          border: "none",
          background: isPending ? "#1F2937" : "#1D4ED8",
          color: isPending ? "#4B5563" : "#E8EDF5",
          fontSize: 13,
          fontWeight: 600,
          cursor: isPending ? "not-allowed" : "pointer",
          transition: "background 0.1s",
        }}
      >
        {isPending ? t("saving") : t("save")}
      </button>
    </div>
  );
}
