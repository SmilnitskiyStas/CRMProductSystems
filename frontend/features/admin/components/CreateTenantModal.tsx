"use client";

import { useState } from "react";
import { useTranslations } from "next-intl";
import { X } from "lucide-react";
import { useCreateTenant } from "../hooks/useAdmin";
import { useItemCategories } from "@/features/marketplace/hooks/useMarketplace";
import { ALL_BUSINESS_TYPES, ALL_PLANS } from "../types";
import type { CreateTenantRequest } from "../types";
import { Btn } from "@/components/ui/Btn";
import { slugify } from "@/lib/slug";

interface Props {
  onClose: () => void;
}

const INPUT_STYLE: React.CSSProperties = {
  width: "100%",
  background: "#0D1117",
  border: "1px solid #1F2937",
  borderRadius: 8,
  padding: "9px 12px",
  color: "#E8EDF5",
  fontSize: 13,
  outline: "none",
  boxSizing: "border-box",
};

const LABEL_STYLE: React.CSSProperties = {
  display: "block",
  color: "#9CA3AF",
  fontSize: 12,
  fontWeight: 500,
  marginBottom: 6,
};

interface FieldProps {
  label: string;
  children: React.ReactNode;
}

function Field({ label, children }: FieldProps) {
  return (
    <div style={{ display: "flex", flexDirection: "column" }}>
      <label style={LABEL_STYLE}>{label}</label>
      {children}
    </div>
  );
}

export function CreateTenantModal({ onClose }: Props) {
  const t = useTranslations("Dashboard.admin.createTenantModal");
  const tPlans = useTranslations("Dashboard.admin.plans");
  const tBusinessTypes = useTranslations("Dashboard.admin.businessTypes");
  const createTenant = useCreateTenant();
  const { data: itemCategories = [] } = useItemCategories();

  const [form, setForm] = useState<CreateTenantRequest>({
    name: "",
    slug: "",
    plan: "trial",
    businessType: "retail",
    adminEmail: "",
    adminFullName: "",
    adminPassword: "",
    supplierCategory: "",
  });
  const [slugManual, setSlugManual] = useState(false);
  const [error, setError] = useState("");

  function setField<K extends keyof CreateTenantRequest>(key: K, value: CreateTenantRequest[K]) {
    setForm((prev) => ({ ...prev, [key]: value }));
  }

  function handleNameChange(name: string) {
    setForm((prev) => ({
      ...prev,
      name,
      slug: slugManual ? prev.slug : slugify(name),
    }));
  }

  function handleSlugChange(slug: string) {
    setSlugManual(true);
    setField("slug", slug);
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError("");

    if (!form.name.trim() || !form.slug.trim() || !form.adminEmail.trim() || !form.adminPassword.trim()) {
      setError(t("errorRequiredFields"));
      return;
    }

    const isSupplier = form.businessType === "supplier";
    if (isSupplier && !form.supplierCategory) {
      setError(t("errorRequiredFields"));
      return;
    }

    try {
      await createTenant.mutateAsync({
        ...form,
        supplierCategory: isSupplier ? form.supplierCategory : undefined,
      });
      onClose();
    } catch (err) {
      setError((err as Error)?.message ?? t("errorCreateDefault"));
    }
  }

  return (
    <div
      style={{
        position: "fixed",
        inset: 0,
        background: "rgba(0,0,0,0.6)",
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        zIndex: 50,
      }}
      onClick={(e) => { if (e.target === e.currentTarget) onClose(); }}
    >
      <div
        style={{
          background: "#161B26",
          border: "1px solid #1F2937",
          borderRadius: 12,
          width: "100%",
          maxWidth: 500,
          maxHeight: "90vh",
          overflowY: "auto",
          boxShadow: "0 24px 64px rgba(0,0,0,0.6)",
        }}
      >
        {/* Header */}
        <div
          style={{
            display: "flex",
            alignItems: "center",
            justifyContent: "space-between",
            padding: "20px 24px",
            borderBottom: "1px solid #1F2937",
          }}
        >
          <div style={{ color: "#E8EDF5", fontSize: 16, fontWeight: 600 }}>
            {t("title")}
          </div>
          <button
            onClick={onClose}
            style={{ background: "none", border: "none", color: "#6B7280", cursor: "pointer", padding: 4 }}
          >
            <X size={18} />
          </button>
        </div>

        {/* Form */}
        <form onSubmit={handleSubmit}>
          <div style={{ padding: "20px 24px", display: "flex", flexDirection: "column", gap: 16 }}>
            <Field label={t("nameLabel")}>
              <input
                style={INPUT_STYLE}
                value={form.name}
                onChange={(e) => handleNameChange(e.target.value)}
                placeholder={t("namePlaceholder")}
              />
            </Field>

            <Field label={t("slugLabel")}>
              <input
                style={INPUT_STYLE}
                value={form.slug}
                onChange={(e) => handleSlugChange(e.target.value)}
                placeholder={t("slugPlaceholder")}
              />
            </Field>

            <Field label={t("planLabel")}>
              <select
                style={{ ...INPUT_STYLE, cursor: "pointer" }}
                value={form.plan}
                onChange={(e) => setField("plan", e.target.value)}
              >
                {ALL_PLANS.map((p) => (
                  <option key={p} value={p}>{tPlans(p)}</option>
                ))}
              </select>
            </Field>

            <Field label={t("businessTypeLabel")}>
              <select
                style={{ ...INPUT_STYLE, cursor: "pointer" }}
                value={form.businessType}
                onChange={(e) => setField("businessType", e.target.value)}
              >
                {ALL_BUSINESS_TYPES.map((bt) => (
                  <option key={bt} value={bt}>{tBusinessTypes(bt)}</option>
                ))}
              </select>
              {form.businessType === "supplier" && (
                <div style={{ color: "#6B7280", fontSize: 11, marginTop: 6, lineHeight: 1.5 }}>
                  {t("supplierHint")}
                </div>
              )}
            </Field>

            {form.businessType === "supplier" && (
              <Field label={t("supplierCategoryLabel")}>
                <select
                  style={{ ...INPUT_STYLE, cursor: "pointer" }}
                  value={form.supplierCategory ?? ""}
                  onChange={(e) => setField("supplierCategory", e.target.value)}
                >
                  <option value="">{t("supplierCategoryPlaceholder")}</option>
                  {itemCategories.map((cat) => (
                    <option key={cat.key} value={cat.key}>{cat.labelUa}</option>
                  ))}
                </select>
                <div style={{ color: "#6B7280", fontSize: 11, marginTop: 6, lineHeight: 1.5 }}>
                  {t("supplierCategoryHint")}
                </div>
              </Field>
            )}

            <div style={{ borderTop: "1px solid #1F2937", paddingTop: 16 }}>
              <div style={{ color: "#6B7280", fontSize: 12, marginBottom: 14 }}>
                {t("adminSectionTitle")}
              </div>
              <div style={{ display: "flex", flexDirection: "column", gap: 14 }}>
                <Field label={t("emailLabel")}>
                  <input
                    style={INPUT_STYLE}
                    type="email"
                    value={form.adminEmail}
                    onChange={(e) => setField("adminEmail", e.target.value)}
                    placeholder={t("emailPlaceholder")}
                  />
                </Field>
                <Field label={t("fullNameLabel")}>
                  <input
                    style={INPUT_STYLE}
                    value={form.adminFullName}
                    onChange={(e) => setField("adminFullName", e.target.value)}
                    placeholder={t("fullNamePlaceholder")}
                  />
                </Field>
                <Field label={t("passwordLabel")}>
                  <input
                    style={INPUT_STYLE}
                    type="password"
                    value={form.adminPassword}
                    onChange={(e) => setField("adminPassword", e.target.value)}
                    placeholder={t("passwordPlaceholder")}
                  />
                </Field>
              </div>
            </div>

            {error && (
              <div style={{ color: "#F87171", fontSize: 13 }}>{error}</div>
            )}
          </div>

          {/* Footer */}
          <div
            style={{
              display: "flex",
              justifyContent: "flex-end",
              gap: 10,
              padding: "16px 24px",
              borderTop: "1px solid #1F2937",
            }}
          >
            <Btn type="button" variant="ghost" onClick={onClose}>
              {t("cancelButton")}
            </Btn>
            <Btn type="submit" disabled={createTenant.isPending}>
              {createTenant.isPending ? t("creating") : t("createButton")}
            </Btn>
          </div>
        </form>
      </div>
    </div>
  );
}
