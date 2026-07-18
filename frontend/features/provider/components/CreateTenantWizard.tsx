"use client";

import { useState } from "react";
import { useTranslations } from "next-intl";
import { X, ChevronRight, ChevronLeft, Check, Building2 } from "lucide-react";
import { useCreateTenant } from "../hooks/useProvider";
import { slugify } from "@/lib/slug";
import type { BusinessType, TenantModule, TenantPlan } from "../types";
import {
  ALL_BUSINESS_TYPES,
  BUSINESS_TYPE_ICONS,
  BUSINESS_TYPE_PRESETS,
  ALL_MODULES,
  ALL_PLANS,
  PLAN_COLORS,
} from "../types";

interface Props {
  onClose: () => void;
  onCreated: (tenantId: string) => void;
}

export function CreateTenantWizard({ onClose, onCreated }: Props) {
  const t = useTranslations("Dashboard.provider.createTenantWizard");
  const tPlans = useTranslations("Dashboard.provider.plans");
  const tBusinessTypes = useTranslations("Dashboard.provider.businessTypes");
  const tModules = useTranslations("Dashboard.provider.modules");
  const tModuleDescriptions = useTranslations("Dashboard.provider.moduleDescriptions");
  const createTenant = useCreateTenant();

  const [step,         setStep]         = useState<1 | 2 | 3>(1);
  const [name,         setName]         = useState("");
  const [slug,         setSlug]         = useState("");
  const [slugTouched,  setSlugTouched]  = useState(false);
  const [plan,         setPlan]         = useState<TenantPlan>("trial");
  const [businessType, setBusinessType] = useState<BusinessType | null>(null);
  const [modules,      setModules]      = useState<TenantModule[]>([]);
  const [error,        setError]        = useState<string | null>(null);

  function handleNameChange(v: string) {
    setName(v);
    if (!slugTouched) setSlug(slugify(v));
  }

  function handleBusinessTypeSelect(bt: BusinessType) {
    setBusinessType(bt);
    setModules([...BUSINESS_TYPE_PRESETS[bt]]);
  }

  function toggleModule(m: TenantModule) {
    setModules((prev) =>
      prev.includes(m) ? prev.filter((x) => x !== m) : [...prev, m]
    );
  }

  async function handleSubmit() {
    if (!businessType) return;
    setError(null);
    try {
      const result = await createTenant.mutateAsync({
        name: name.trim(),
        slug: slug.trim(),
        businessType,
        plan,
        modules,
      });
      onCreated(result.id);
      onClose();
    } catch (e: unknown) {
      const msg = (e as { data?: { error?: string } })?.data?.error ?? t("errorCreateDefault");
      setError(msg);
    }
  }

  const canGoStep2 = name.trim().length >= 2 && slug.trim().length >= 2;
  const canGoStep3 = businessType !== null;

  return (
    <>
      {/* Backdrop */}
      <div
        onClick={onClose}
        style={{
          position: "fixed", inset: 0, background: "rgba(0,0,0,0.6)",
          zIndex: 100, backdropFilter: "blur(2px)",
        }}
      />

      {/* Modal */}
      <div style={{
        position: "fixed", top: "50%", left: "50%",
        transform: "translate(-50%, -50%)",
        width: "min(640px, calc(100vw - 32px))",
        background: "#0D1117",
        border: "1px solid #1F2937",
        borderRadius: 14,
        zIndex: 101,
        display: "flex", flexDirection: "column",
        maxHeight: "calc(100vh - 48px)",
        overflow: "hidden",
      }}>

        {/* Header */}
        <div style={{
          padding: "20px 24px 16px",
          borderBottom: "1px solid #1F2937",
          display: "flex", alignItems: "center", justifyContent: "space-between",
          flexShrink: 0,
        }}>
          <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
            <div style={{
              width: 32, height: 32, borderRadius: 8,
              background: "linear-gradient(135deg, #7C3AED, #3B82F6)",
              display: "flex", alignItems: "center", justifyContent: "center",
            }}>
              <Building2 size={15} color="#fff" />
            </div>
            <div>
              <div style={{ color: "#E8EDF5", fontSize: 15, fontWeight: 600 }}>
                {t("title")}
              </div>
              <div style={{ color: "#4B5563", fontSize: 12, marginTop: 1 }}>
                {t("stepLabel", {
                  step,
                  name:
                    step === 1
                      ? t("stepSubtitleCompany")
                      : step === 2
                        ? t("stepSubtitleBusinessType")
                        : t("stepSubtitleModules"),
                })}
              </div>
            </div>
          </div>
          <button
            onClick={onClose}
            style={{
              background: "transparent", border: "1px solid #1F2937",
              borderRadius: 8, padding: "6px 8px", cursor: "pointer",
              color: "#6B7280", display: "flex", alignItems: "center",
            }}
          >
            <X size={15} />
          </button>
        </div>

        {/* Step indicators */}
        <div style={{
          display: "flex", gap: 0, padding: "0 24px",
          borderBottom: "1px solid #1F2937", flexShrink: 0,
        }}>
          {([1, 2, 3] as const).map((s) => (
            <div key={s} style={{
              padding: "10px 0", marginRight: 24,
              borderBottom: `2px solid ${step === s ? "#3B82F6" : "transparent"}`,
              color: step === s ? "#60A5FA" : step > s ? "#4ADE80" : "#374151",
              fontSize: 12, fontWeight: 600, display: "flex", alignItems: "center", gap: 5,
            }}>
              {step > s ? <Check size={12} /> : null}
              {s === 1 ? t("stepIndicatorCompany") : s === 2 ? t("stepSubtitleBusinessType") : t("stepSubtitleModules")}
            </div>
          ))}
        </div>

        {/* Body */}
        <div style={{ padding: 24, overflowY: "auto", flex: 1 }}>

          {/* ── Step 1: Company info ── */}
          {step === 1 && (
            <div style={{ display: "flex", flexDirection: "column", gap: 18 }}>
              <Field label={t("companyNameLabel")}>
                <input
                  value={name}
                  onChange={(e) => handleNameChange(e.target.value)}
                  placeholder={t("companyNamePlaceholder")}
                  style={inputStyle}
                />
              </Field>

              {slug && (
                <div style={{ fontSize: 11, color: "#4B5563", marginTop: -10 }}>
                  {t("slugPrefix")} <span style={{ color: "#6B7280" }}>{slug}</span>
                </div>
              )}

              <Field label={t("planFieldLabel")}>
                <div style={{ display: "flex", gap: 8, flexWrap: "wrap" }}>
                  {ALL_PLANS.map((p) => {
                    const c = PLAN_COLORS[p];
                    const active = plan === p;
                    return (
                      <button
                        key={p}
                        onClick={() => setPlan(p)}
                        style={{
                          padding: "6px 14px", borderRadius: 8, cursor: "pointer",
                          background: active ? c.bg : "transparent",
                          border: `1px solid ${active ? c.border : "#1F2937"}`,
                          color: active ? c.text : "#6B7280",
                          fontSize: 13, fontWeight: active ? 600 : 400,
                        }}
                      >
                        {tPlans(p)}
                      </button>
                    );
                  })}
                </div>
              </Field>
            </div>
          )}

          {/* ── Step 2: Business type ── */}
          {step === 2 && (
            <div>
              <p style={{ color: "#6B7280", fontSize: 13, marginBottom: 16 }}>
                {t("businessTypeHint")}
              </p>
              <div style={{
                display: "grid", gridTemplateColumns: "repeat(auto-fill, minmax(200px, 1fr))", gap: 10,
              }}>
                {ALL_BUSINESS_TYPES.map((bt) => {
                  const active = businessType === bt;
                  return (
                    <button
                      key={bt}
                      onClick={() => handleBusinessTypeSelect(bt)}
                      style={{
                        background: active ? "#0f1f3d" : "#111827",
                        border: `1px solid ${active ? "#3B82F6" : "#1F2937"}`,
                        borderRadius: 10, padding: "14px 16px",
                        cursor: "pointer", textAlign: "left",
                        transition: "border-color 0.15s, background 0.15s",
                      }}
                    >
                      <div style={{ fontSize: 26, marginBottom: 6 }}>
                        {BUSINESS_TYPE_ICONS[bt]}
                      </div>
                      <div style={{ color: active ? "#93C5FD" : "#D1D5DB", fontSize: 13, fontWeight: 600 }}>
                        {tBusinessTypes(bt)}
                      </div>
                    </button>
                  );
                })}
              </div>
            </div>
          )}

          {/* ── Step 3: Modules ── */}
          {step === 3 && businessType && (
            <div>
              <p style={{ color: "#6B7280", fontSize: 13, marginBottom: 16 }}>
                {t("preselectedHintPrefix")}{" "}
                <span style={{ color: "#93C5FD" }}>{tBusinessTypes(businessType)}</span>
                {t("preselectedHintSuffix")}
              </p>
              <div style={{ display: "flex", flexDirection: "column", gap: 8 }}>
                {ALL_MODULES.map((m) => {
                  const checked = modules.includes(m);
                  return (
                    <button
                      key={m}
                      onClick={() => toggleModule(m)}
                      style={{
                        background: checked ? "#0f1f3d" : "#111827",
                        border: `1px solid ${checked ? "#3B82F6" : "#1F2937"}`,
                        borderRadius: 10, padding: "12px 16px",
                        cursor: "pointer", textAlign: "left",
                        display: "flex", alignItems: "center", gap: 12,
                        transition: "border-color 0.15s, background 0.15s",
                      }}
                    >
                      <div style={{
                        width: 20, height: 20, borderRadius: 5, flexShrink: 0,
                        background: checked ? "#3B82F6" : "transparent",
                        border: `2px solid ${checked ? "#3B82F6" : "#374151"}`,
                        display: "flex", alignItems: "center", justifyContent: "center",
                      }}>
                        {checked && <Check size={12} color="#fff" />}
                      </div>
                      <div>
                        <div style={{ color: checked ? "#93C5FD" : "#D1D5DB", fontSize: 13, fontWeight: 600 }}>
                          {tModules(m)}
                        </div>
                        <div style={{ color: "#4B5563", fontSize: 11, marginTop: 2 }}>
                          {tModuleDescriptions(m)}
                        </div>
                      </div>
                    </button>
                  );
                })}
              </div>
            </div>
          )}

          {error && (
            <div style={{
              marginTop: 16, padding: "10px 14px", borderRadius: 8,
              background: "#450a0a", border: "1px solid #7f1d1d",
              color: "#F87171", fontSize: 13,
            }}>
              {error}
            </div>
          )}
        </div>

        {/* Footer */}
        <div style={{
          padding: "16px 24px",
          borderTop: "1px solid #1F2937",
          display: "flex", justifyContent: "space-between", alignItems: "center",
          flexShrink: 0,
        }}>
          <button
            onClick={() => step > 1 ? setStep((s) => (s - 1) as 1 | 2 | 3) : onClose()}
            style={{
              display: "flex", alignItems: "center", gap: 6,
              padding: "8px 16px", borderRadius: 8, cursor: "pointer",
              background: "transparent", border: "1px solid #1F2937",
              color: "#6B7280", fontSize: 13,
            }}
          >
            <ChevronLeft size={14} />
            {step === 1 ? t("cancelButton") : t("backButton")}
          </button>

          {step < 3 ? (
            <button
              onClick={() => setStep((s) => (s + 1) as 2 | 3)}
              disabled={step === 1 ? !canGoStep2 : !canGoStep3}
              style={{
                display: "flex", alignItems: "center", gap: 6,
                padding: "8px 18px", borderRadius: 8, cursor: (step === 1 ? canGoStep2 : canGoStep3) ? "pointer" : "default",
                background: (step === 1 ? canGoStep2 : canGoStep3) ? "#3B82F6" : "#1F2937",
                border: "none",
                color: (step === 1 ? canGoStep2 : canGoStep3) ? "#fff" : "#374151",
                fontSize: 13, fontWeight: 600,
              }}
            >
              {t("nextButton")}
              <ChevronRight size={14} />
            </button>
          ) : (
            <button
              onClick={handleSubmit}
              disabled={createTenant.isPending || modules.length === 0}
              style={{
                display: "flex", alignItems: "center", gap: 6,
                padding: "8px 18px", borderRadius: 8,
                cursor: createTenant.isPending || modules.length === 0 ? "default" : "pointer",
                background: createTenant.isPending || modules.length === 0 ? "#1F2937" : "#3B82F6",
                border: "none",
                color: createTenant.isPending || modules.length === 0 ? "#374151" : "#fff",
                fontSize: 13, fontWeight: 600,
              }}
            >
              <Check size={14} />
              {createTenant.isPending ? t("creating") : t("createButton")}
            </button>
          )}
        </div>
      </div>
    </>
  );
}

// ── Internal helpers ──────────────────────────────────────────────────────────

const inputStyle: React.CSSProperties = {
  width: "100%",
  background: "#111827",
  border: "1px solid #1F2937",
  borderRadius: 8,
  padding: "9px 12px",
  color: "#E8EDF5",
  fontSize: 13,
  outline: "none",
  boxSizing: "border-box",
};

function Field({ label, hint, children }: { label: string; hint?: string; children: React.ReactNode }) {
  return (
    <div>
      <label style={{ display: "block", color: "#9CA3AF", fontSize: 12, fontWeight: 600, marginBottom: 6 }}>
        {label}
      </label>
      {children}
      {hint && <p style={{ color: "#374151", fontSize: 11, marginTop: 4 }}>{hint}</p>}
    </div>
  );
}
