"use client";

import { useState, useEffect, KeyboardEvent } from "react";
import { useTranslations } from "next-intl";
import { useMySupplierProfile, useUpdateMySupplierProfile } from "../hooks/useMarketplace";
import type { SupplierProfileUpdateRequest, SupplierPlan } from "../types";

// ─── Tag input helper ─────────────────────────────────────────────────────────

interface TagInputProps {
  value: string[];
  onChange: (tags: string[]) => void;
  placeholder?: string;
}

function TagInput({ value, onChange, placeholder }: TagInputProps) {
  const [input, setInput] = useState("");

  function addTag() {
    const t = input.trim();
    if (t && !value.includes(t)) {
      onChange([...value, t]);
    }
    setInput("");
  }

  function onKey(e: KeyboardEvent<HTMLInputElement>) {
    if (e.key === "Enter" || e.key === ",") {
      e.preventDefault();
      addTag();
    }
    if (e.key === "Backspace" && input === "" && value.length > 0) {
      onChange(value.slice(0, -1));
    }
  }

  return (
    <div
      style={{
        display: "flex",
        flexWrap: "wrap",
        gap: 6,
        padding: "6px 10px",
        background: "#0D1117",
        border: "1px solid #1F2937",
        borderRadius: 8,
        minHeight: 42,
        alignItems: "center",
      }}
    >
      {value.map((tag) => (
        <span
          key={tag}
          style={{
            display: "inline-flex",
            alignItems: "center",
            gap: 4,
            padding: "2px 8px",
            background: "#1F2937",
            borderRadius: 4,
            color: "#E8EDF5",
            fontSize: 12,
          }}
        >
          {tag}
          <button
            type="button"
            onClick={() => onChange(value.filter((t) => t !== tag))}
            style={{
              background: "none",
              border: "none",
              color: "#6B7280",
              cursor: "pointer",
              padding: 0,
              lineHeight: 1,
              fontSize: 14,
            }}
          >
            ×
          </button>
        </span>
      ))}
      <input
        type="text"
        value={input}
        onChange={(e) => setInput(e.target.value)}
        onKeyDown={onKey}
        onBlur={addTag}
        placeholder={value.length === 0 ? placeholder : ""}
        style={{
          background: "transparent",
          border: "none",
          outline: "none",
          color: "#E8EDF5",
          fontSize: 13,
          minWidth: 120,
          flex: 1,
        }}
      />
    </div>
  );
}

// ─── Main form ────────────────────────────────────────────────────────────────

const EMPTY_FORM: SupplierProfileUpdateRequest = {
  region: "",
  categories: [],
  website: "",
  deliveryRegions: [],
  workingHours: "",
  paymentTerms: "",
  isPublic: false,
  plan: "free",
};

export function SupplierProfileForm() {
  const t = useTranslations("Dashboard.marketplace.profileForm");
  const tPlan = useTranslations("Dashboard.marketplace.planLabel");
  const { data, isLoading, isError } = useMySupplierProfile();
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

  function handleSave() {
    setSaveError(null);
    setSaved(false);
    mutate(
      { ...form, website: form.website || undefined, workingHours: form.workingHours || undefined, paymentTerms: form.paymentTerms || undefined },
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
    <div style={{ display: "flex", flexDirection: "column", gap: 20, maxWidth: 600 }}>
      <div>
        <h2 style={{ color: "#E8EDF5", fontSize: 16, fontWeight: 600, margin: 0 }}>
          {t("title")}
        </h2>
        <p style={{ color: "#4B5563", fontSize: 13, marginTop: 6 }}>
          {t("subtitle")}
        </p>
      </div>

      {/* Region */}
      <div style={fieldStyle}>
        <label style={labelStyle}>{t("regionLabel")}</label>
        <input
          type="text"
          value={form.region}
          onChange={(e) => setForm({ ...form, region: e.target.value })}
          placeholder={t("regionPlaceholder")}
          style={inputStyle}
        />
      </div>

      {/* Categories */}
      <div style={fieldStyle}>
        <label style={labelStyle}>{t("categoriesLabel")}</label>
        <TagInput
          value={form.categories}
          onChange={(categories) => setForm({ ...form, categories })}
          placeholder={t("categoriesPlaceholder")}
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

      {/* Delivery regions */}
      <div style={fieldStyle}>
        <label style={labelStyle}>{t("deliveryRegionsLabel")}</label>
        <TagInput
          value={form.deliveryRegions}
          onChange={(deliveryRegions) => setForm({ ...form, deliveryRegions })}
          placeholder={t("deliveryRegionsPlaceholder")}
        />
      </div>

      {/* Working hours */}
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

      {/* Payment terms */}
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
