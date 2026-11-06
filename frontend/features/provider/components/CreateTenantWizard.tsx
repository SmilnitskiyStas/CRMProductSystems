"use client";

import { useEffect, useId, useRef, useState } from "react";
import type { ReactNode } from "react";
import { useTranslations } from "next-intl";
import { ChevronRight, ChevronLeft, Check } from "lucide-react";
import { ApiError } from "@/lib/api";
import { useCreateTenant } from "../hooks/useProvider";
import { useCreateBeautyTenant } from "../hooks/useProviderBeauty";
import {
  BeautyInviteDialog,
  EMAIL_RE,
  ModuleChecklist,
  beautyErrorKey,
  type Invite,
} from "./BeautySection";
import { BeautyDialog } from "./BeautyDialog";
import { useItemCategories } from "@/features/marketplace/hooks/useMarketplace";
import { slugify } from "@/lib/slug";
import { Btn } from "@/components/ui/Btn";
import { Modal } from "@/components/ui/Modal";
import { inputStyle, labelStyle } from "@/components/ui/fieldStyles";
import type { BusinessType, TenantModule, TenantPlan } from "../types";
import {
  ALL_BUSINESS_TYPES,
  BUSINESS_TYPE_ICONS,
  BUSINESS_TYPE_PRESETS,
  ALL_MODULES,
  ALL_PLANS,
  PLAN_COLORS,
  BEAUTY_DEFAULT_MODULES,
} from "../types";

const BEAUTY_SLUG_RE = /^[a-z0-9][a-z0-9-]{0,62}$/;

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
  const tBeauty = useTranslations("Dashboard.provider.beauty");
  const createTenant = useCreateTenant();
  const createBeauty = useCreateBeautyTenant();
  const submitting = useRef(false);
  const emailRef = useRef<HTMLInputElement>(null);
  // The control that opened the wizard ("Add client"); captured before anything inside takes focus.
  const openerRef = useRef<HTMLElement | null>(null);
  if (openerRef.current === null && typeof document !== "undefined") {
    openerRef.current = document.activeElement as HTMLElement | null;
  }
  useEffect(() => {
    const opener = openerRef.current;
    return () => {
      if (opener && opener !== document.body) opener.focus?.();
    };
  }, []);
  const { data: itemCategories = [] } = useItemCategories();

  const [step,         setStep]         = useState<1 | 2 | 3>(1);
  const [name,         setName]         = useState("");
  const [slug,         setSlug]         = useState("");
  const [slugTouched,  setSlugTouched]  = useState(false);
  const [plan,         setPlan]         = useState<TenantPlan>("trial");
  const [businessType, setBusinessType] = useState<BusinessType | null>(null);
  const [supplierCategory, setSupplierCategory] = useState("");
  const [modules,      setModules]      = useState<TenantModule[]>([]);
  const [error,        setError]        = useState<string | null>(null);
  const [ownerEmail,   setOwnerEmail]   = useState("");
  const [emailError,   setEmailError]   = useState<string | null>(null);
  const [ownerName,    setOwnerName]    = useState("");
  const [beautyModules, setBeautyModules] = useState<string[]>([...BEAUTY_DEFAULT_MODULES]);
  const [createdTenantId, setCreatedTenantId] = useState<string | null>(null);
  const [beautyResult, setBeautyResult] = useState<{ invite: Invite | null } | null>(null);

  function handleNameChange(v: string) {
    setName(v);
    if (!slugTouched) setSlug(slugify(v));
  }

  function handleBusinessTypeSelect(bt: BusinessType) {
    setBusinessType(bt);
    setModules(bt === "beauty" ? [] : [...BUSINESS_TYPE_PRESETS[bt]]);
    if (bt !== "supplier") setSupplierCategory("");
  }

  function toggleModule(m: TenantModule) {
    setModules((prev) =>
      prev.includes(m) ? prev.filter((x) => x !== m) : [...prev, m]
    );
  }

  function finish(tenantId: string) {
    onCreated(tenantId);
    onClose();
  }

  /** Never surfaces raw server text: only known statuses map to i18n copy. */
  function createErrorText(e: unknown): string {
    return e instanceof ApiError && e.status === 409 ? t("tenantSlugTaken") : t("errorCreateDefault");
  }

  // Closing while a request is in flight would lose the one-time invite; after a partial failure
  // the already-created tenant is opened so the Beauty section can finish the job.
  function handleClose() {
    if (isBusy || submitting.current) return;
    if (createdTenantId) finish(createdTenantId);
    else onClose();
  }

  async function handleSubmit() {
    if (!businessType || submitting.current) return;
    if (businessType === "beauty") {
      await handleBeautySubmit();
      return;
    }
    submitting.current = true;
    setError(null);
    try {
      const result = await createTenant.mutateAsync({
        name: name.trim(),
        slug: slug.trim(),
        businessType,
        plan,
        modules,
        ...(businessType === "supplier" && supplierCategory
          ? { supplierCategory }
          : {}),
      });
      onCreated(result.id);
      onClose();
    } catch (e: unknown) {
      setError(createErrorText(e));
    } finally {
      submitting.current = false;
    }
  }

  async function handleBeautySubmit() {
    const trimmedSlug = slug.trim();
    if (!BEAUTY_SLUG_RE.test(trimmedSlug)) {
      setError(t("slugInvalid"));
      return;
    }
    if (!EMAIL_RE.test(ownerEmail.trim())) {
      setError(null);
      setEmailError(t("emailInvalid"));
      emailRef.current?.focus();
      return;
    }
    if (beautyModules.length === 0) {
      setError(t("modulesRequired"));
      return;
    }
    submitting.current = true;
    setError(null);
    setEmailError(null);
    let tenantId = createdTenantId;
    try {
      if (!tenantId) {
        try {
          const result = await createTenant.mutateAsync({
            name: name.trim(),
            slug: trimmedSlug,
            businessType: "beauty",
            plan,
            modules: [],
          });
          tenantId = result.id;
          setCreatedTenantId(tenantId);
        } catch (e: unknown) {
          setError(createErrorText(e));
          return;
        }
      }
      try {
        const res = await createBeauty.mutateAsync({
          shelfGuardTenantId: tenantId,
          name: name.trim(),
          slug: trimmedSlug,
          ownerEmail: ownerEmail.trim(),
          ownerName: ownerName.trim() || undefined,
          modules: beautyModules,
        });
        createBeauty.reset();
        setBeautyResult({
          invite: res.inviteUrl && res.inviteExpiresAt ? { url: res.inviteUrl, expiresAt: res.inviteExpiresAt } : null,
        });
      } catch (e: unknown) {
        setError(t("partialFailure", { error: tBeauty(`errors.${beautyErrorKey(e)}`) }));
      }
    } finally {
      submitting.current = false;
    }
  }

  const canGoStep2 = name.trim().length >= 2 && slug.trim().length >= 2;
  const isSupplier = businessType === "supplier";
  const isBeauty = businessType === "beauty";
  const canGoStep3 =
    businessType !== null && (!isSupplier || supplierCategory !== "");
  const isBusy = createTenant.isPending || createBeauty.isPending;

  if (beautyResult && createdTenantId) {
    if (beautyResult.invite) {
      return <BeautyInviteDialog invite={beautyResult.invite} onClose={() => finish(createdTenantId)} />;
    }
    return (
      <BeautyDialog title={t("title")} onClose={() => finish(createdTenantId)} closeLabel={tBeauty("close")} width={480}>
        <div style={{ display: "flex", flexDirection: "column", gap: 14 }}>
          <div role="note" style={{ color: "#FCD34D", fontSize: 13, background: "#451A03", border: "1px solid #92400E", borderRadius: 8, padding: "10px 14px" }}>
            {t("noInviteNotice")}
          </div>
          <Btn type="button" onClick={() => finish(createdTenantId)} style={{ justifyContent: "center" }}>
            {t("doneButton")}
          </Btn>
        </div>
      </BeautyDialog>
    );
  }

  return (
    <Modal title={t("title")} onClose={handleClose} width={640}>
      <div style={{ color: "#9CA3AF", fontSize: 12, marginTop: -14, marginBottom: 16 }}>
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
      <div>
        {/* Step indicators */}
        <div style={{
          display: "flex", gap: 0,
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
        <div style={{ paddingTop: 20 }}>

          {/* ── Step 1: Company info ── */}
          {step === 1 && (
            <div style={{ display: "flex", flexDirection: "column", gap: 18 }}>
              <Field label={t("companyNameLabel")}>
                {(a) => (
                  <input
                    {...a}
                    value={name}
                    onChange={(e) => handleNameChange(e.target.value)}
                    placeholder={t("companyNamePlaceholder")}
                    style={inputStyle}
                  />
                )}
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

              {isSupplier && (
                <div style={{ marginTop: 20 }}>
                  <Field label={t("supplierCategoryLabel")} hint={t("supplierCategoryHint")}>
                    <div style={{ display: "flex", flexDirection: "column", gap: 8 }}>
                      {itemCategories.map((cat) => {
                        const selected = supplierCategory === cat.key;
                        return (
                          <button
                            key={cat.key}
                            type="button"
                            onClick={() => setSupplierCategory(cat.key)}
                            style={{
                              background: selected ? "#0f1f3d" : "#111827",
                              border: `1px solid ${selected ? "#3B82F6" : "#1F2937"}`,
                              borderRadius: 10,
                              padding: "12px 16px",
                              cursor: "pointer",
                              textAlign: "left",
                              display: "flex",
                              alignItems: "center",
                              gap: 12,
                              transition: "border-color 0.15s, background 0.15s",
                            }}
                          >
                            <div style={{
                              width: 18, height: 18, borderRadius: "50%", flexShrink: 0,
                              background: selected ? "#3B82F6" : "transparent",
                              border: `2px solid ${selected ? "#3B82F6" : "#374151"}`,
                              display: "flex", alignItems: "center", justifyContent: "center",
                            }}>
                              {selected && (
                                <div style={{ width: 7, height: 7, borderRadius: "50%", background: "#fff" }} />
                              )}
                            </div>
                            <span style={{ color: selected ? "#93C5FD" : "#D1D5DB", fontSize: 13, fontWeight: 600 }}>
                              {cat.labelUa}
                            </span>
                          </button>
                        );
                      })}
                    </div>
                  </Field>
                </div>
              )}
            </div>
          )}

          {/* ── Step 3: Modules ── */}
          {step === 3 && isBeauty && (
            <div style={{ display: "flex", flexDirection: "column", gap: 16 }}>
              <p style={{ color: "#9CA3AF", fontSize: 13, margin: 0 }}>{t("beautyHint")}</p>
              <Field label={t("ownerEmailLabel")} error={emailError ?? undefined}>
                {(a) => (
                  <input
                    {...a}
                    ref={emailRef}
                    type="email"
                    aria-required="true"
                    value={ownerEmail}
                    onChange={(e) => { setOwnerEmail(e.target.value); setEmailError(null); }}
                    autoComplete="off"
                    disabled={isBusy}
                    style={inputStyle}
                  />
                )}
              </Field>
              <Field label={t("ownerNameLabel")}>
                {(a) => (
                  <input
                    {...a}
                    value={ownerName}
                    onChange={(e) => setOwnerName(e.target.value)}
                    autoComplete="off"
                    disabled={isBusy}
                    style={inputStyle}
                  />
                )}
              </Field>
              <fieldset style={{ border: "none", padding: 0, margin: 0 }}>
                <legend style={labelStyle}>{t("beautyModulesLabel")}</legend>
                <ModuleChecklist
                  selected={beautyModules}
                  onToggle={(m) =>
                    setBeautyModules((p) => (p.includes(m) ? p.filter((x) => x !== m) : [...p, m]))
                  }
                  disabled={isBusy}
                />
              </fieldset>
            </div>
          )}

          {step === 3 && businessType && !isBeauty && (
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
            <div role="alert" style={{
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
          marginTop: 20, paddingTop: 16,
          borderTop: "1px solid #1F2937",
          display: "flex", justifyContent: "space-between", alignItems: "center",
        }}>
          <Btn
            variant="ghost"
            icon={<ChevronLeft size={14} />}
            disabled={isBusy || (createdTenantId !== null && step > 1)}
            onClick={() => step > 1 ? setStep((s) => (s - 1) as 1 | 2 | 3) : handleClose()}
          >
            {step === 1 ? t("cancelButton") : t("backButton")}
          </Btn>

          {step < 3 ? (
            <Btn
              onClick={() => setStep((s) => (s + 1) as 2 | 3)}
              disabled={step === 1 ? !canGoStep2 : !canGoStep3}
            >
              {t("nextButton")}
              <ChevronRight size={14} />
            </Btn>
          ) : (
            <Btn
              icon={<Check size={14} />}
              onClick={handleSubmit}
              disabled={
                isBusy || !canGoStep3 ||
                (isBeauty ? beautyModules.length === 0 : modules.length === 0)
              }
            >
              {isBusy
                ? t("creating")
                : createdTenantId
                  ? t("retryBeautyButton")
                  : t("createButton")}
            </Btn>
          )}
        </div>
      </div>
    </Modal>
  );
}

// ── Internal helpers ──────────────────────────────────────────────────────────

interface FieldControlProps {
  id: string;
  "aria-invalid"?: true;
  "aria-describedby"?: string;
}

/**
 * Labelled field. Pass a render function to get the id / aria props to spread on a single input
 * (label is linked via htmlFor); plain children (button groups) are wrapped in role="group".
 */
function Field({
  label, hint, error, children,
}: {
  label: string;
  hint?: string;
  error?: string;
  children: ReactNode | ((a: FieldControlProps) => ReactNode);
}) {
  const id = useId();
  const labelId = `${id}-label`;
  const errId = `${id}-err`;
  const hintId = `${id}-hint`;
  const describedBy = [error ? errId : null, hint ? hintId : null].filter(Boolean).join(" ") || undefined;
  return (
    <div role={typeof children === "function" ? undefined : "group"} aria-labelledby={typeof children === "function" ? undefined : labelId}>
      <label id={labelId} htmlFor={typeof children === "function" ? id : undefined} style={labelStyle}>
        {label}
      </label>
      {typeof children === "function"
        ? children({ id, "aria-invalid": error ? true : undefined, "aria-describedby": describedBy })
        : children}
      {error && (
        <div id={errId} role="alert" style={{ color: "#F87171", fontSize: 12, marginTop: 4 }}>{error}</div>
      )}
      {hint && <p id={hintId} style={{ color: "#9CA3AF", fontSize: 11, marginTop: 4 }}>{hint}</p>}
    </div>
  );
}
