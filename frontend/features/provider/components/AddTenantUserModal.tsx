"use client";

import { useState } from "react";
import { useTranslations } from "next-intl";
import { X, CheckCircle2 } from "lucide-react";
import { useCreateTenantUser } from "../hooks/useProvider";
import type { BusinessType, TenantUserDto } from "../types";
import { Btn } from "@/components/ui/Btn";

interface RoleOption {
  value: string;
  label: string;
  description: string;
}

interface Props {
  tenantId: string;
  businessType?: BusinessType;
  onClose: () => void;
  onCreated: (user: TenantUserDto) => void;
}

export function AddTenantUserModal({ tenantId, businessType, onClose, onCreated }: Props) {
  const t = useTranslations("Dashboard.provider.addTenantUserModal");
  const createUser = useCreateTenantUser(tenantId);

  // Role set depends on tenant business type (ADR-016): supplier tenants get ONLY
  // supplier_admin (cabinet-only access), regular tenants keep enterprise_admin.
  const SUPPLIER_ROLES: RoleOption[] = [
    {
      value: "supplier_admin",
      label: t("supplierAdminRoleLabel"),
      description: t("supplierAdminRoleDescription"),
    },
  ];
  const REGULAR_ROLES: RoleOption[] = [
    {
      value: "enterprise_admin",
      label: t("enterpriseAdminRoleLabel"),
      description: t("enterpriseAdminRoleDescription"),
    },
  ];

  const isSupplier = businessType === "supplier";
  const roles      = isSupplier ? SUPPLIER_ROLES : REGULAR_ROLES;

  const [role, setRole]                   = useState(roles[0].value);
  const [fullName, setFullName]           = useState("");
  const [email, setEmail]                 = useState("");
  const [password, setPassword]           = useState("");
  const [confirmPassword, setConfirmPassword] = useState("");
  const [errors, setErrors]               = useState<Record<string, string>>({});
  const [serverError, setServerError]     = useState("");
  const [createdUser, setCreatedUser]     = useState<TenantUserDto | null>(null);

  function validate() {
    const e: Record<string, string> = {};
    if (!fullName.trim())                      e.fullName = t("errorFullNameRequired");
    if (!email.trim())                         e.email    = t("errorEmailRequired");
    if (!password)                             e.password = t("errorPasswordRequired");
    else if (password.length < 6)             e.password = t("errorPasswordMinLength");
    if (password !== confirmPassword)          e.confirmPassword = t("errorPasswordMismatch");
    return e;
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setServerError("");
    const errs = validate();
    if (Object.keys(errs).length > 0) {
      setErrors(errs);
      return;
    }
    setErrors({});
    try {
      const user = await createUser.mutateAsync({ fullName: fullName.trim(), email: email.trim(), password, role });
      setCreatedUser(user);
    } catch (err) {
      setServerError((err as Error)?.message ?? t("errorCreateDefault"));
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
        zIndex: 1000,
      }}
      onClick={(e) => { if (e.target === e.currentTarget) onClose(); }}
    >
      <div
        style={{
          background: "#0D1117",
          border: "1px solid #1F2937",
          borderRadius: 12,
          width: 420,
          maxWidth: "calc(100vw - 32px)",
          display: "flex",
          flexDirection: "column",
        }}
      >
        {/* Header */}
        <div
          style={{
            display: "flex",
            alignItems: "center",
            justifyContent: "space-between",
            padding: "18px 20px",
            borderBottom: "1px solid #1F2937",
          }}
        >
          <div style={{ color: "#E8EDF5", fontSize: 15, fontWeight: 600 }}>
            {t("title")}
          </div>
          <button
            onClick={onClose}
            style={{ background: "none", border: "none", color: "#6B7280", cursor: "pointer", padding: 4 }}
          >
            <X size={18} />
          </button>
        </div>

        {createdUser ? (
          <div style={{ padding: "24px 20px", display: "flex", flexDirection: "column", alignItems: "center", gap: 12, textAlign: "center" }}>
            <CheckCircle2 size={40} color="#22C55E" />
            <div style={{ color: "#E8EDF5", fontSize: 14, fontWeight: 600 }}>
              {t("createdMessage", { email: createdUser.email })}
            </div>
            {isSupplier && (
              <div style={{ color: "#6B7280", fontSize: 12 }}>
                {t("supplierLoginHint")}
              </div>
            )}
            <Btn
              type="button"
              onClick={() => { onCreated(createdUser); onClose(); }}
              style={{ marginTop: 8, justifyContent: "center", width: "100%" }}
            >
              {t("closeButton")}
            </Btn>
          </div>
        ) : (
        <form onSubmit={handleSubmit} style={{ padding: "20px", display: "flex", flexDirection: "column", gap: 14 }}>
          {serverError && (
            <div style={{ color: "#F87171", fontSize: 13, background: "#1F1211", border: "1px solid #7F1D1D", borderRadius: 8, padding: "10px 14px" }}>
              {serverError}
            </div>
          )}

          {/* ПІБ */}
          <div>
            <label style={{ display: "block", color: "#9CA3AF", fontSize: 12, fontWeight: 500, marginBottom: 6 }}>
              {t("fullNameLabel")}
            </label>
            <input
              type="text"
              value={fullName}
              onChange={(e) => setFullName(e.target.value)}
              placeholder={t("fullNamePlaceholder")}
              style={{
                width: "100%",
                background: "#111827",
                border: `1px solid ${errors.fullName ? "#7F1D1D" : "#1F2937"}`,
                borderRadius: 8,
                padding: "9px 12px",
                color: "#E8EDF5",
                fontSize: 13,
                outline: "none",
                boxSizing: "border-box",
              }}
            />
            {errors.fullName && (
              <div style={{ color: "#F87171", fontSize: 11, marginTop: 4 }}>{errors.fullName}</div>
            )}
          </div>

          {/* Email */}
          <div>
            <label style={{ display: "block", color: "#9CA3AF", fontSize: 12, fontWeight: 500, marginBottom: 6 }}>
              {t("emailLabel")}
            </label>
            <input
              type="email"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              placeholder={t("emailPlaceholder")}
              style={{
                width: "100%",
                background: "#111827",
                border: `1px solid ${errors.email ? "#7F1D1D" : "#1F2937"}`,
                borderRadius: 8,
                padding: "9px 12px",
                color: "#E8EDF5",
                fontSize: 13,
                outline: "none",
                boxSizing: "border-box",
              }}
            />
            {errors.email && (
              <div style={{ color: "#F87171", fontSize: 11, marginTop: 4 }}>{errors.email}</div>
            )}
          </div>

          {/* Роль */}
          <div>
            <label style={{ display: "block", color: "#9CA3AF", fontSize: 12, fontWeight: 500, marginBottom: 6 }}>
              {t("roleLabel")}
            </label>
            {roles.length > 1 ? (
              <select
                value={role}
                onChange={(e) => setRole(e.target.value)}
                style={{
                  width: "100%",
                  background: "#111827",
                  border: "1px solid #1F2937",
                  borderRadius: 8,
                  padding: "9px 12px",
                  color: "#E8EDF5",
                  fontSize: 13,
                  outline: "none",
                  boxSizing: "border-box",
                }}
              >
                {roles.map((r) => (
                  <option key={r.value} value={r.value}>{r.label}</option>
                ))}
              </select>
            ) : (
              <div
                style={{
                  background: "#111827",
                  border: "1px solid #1F2937",
                  borderRadius: 8,
                  padding: "9px 12px",
                  boxSizing: "border-box",
                }}
              >
                <div style={{ color: "#E8EDF5", fontSize: 13 }}>{t("roleSingleLabel", { role: roles[0].label })}</div>
                <div style={{ color: "#6B7280", fontSize: 11, marginTop: 2 }}>{roles[0].description}</div>
              </div>
            )}
          </div>

          {/* Пароль */}
          <div>
            <label style={{ display: "block", color: "#9CA3AF", fontSize: 12, fontWeight: 500, marginBottom: 6 }}>
              {t("passwordLabel")}
            </label>
            <input
              type="password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              placeholder={t("passwordPlaceholder")}
              style={{
                width: "100%",
                background: "#111827",
                border: `1px solid ${errors.password ? "#7F1D1D" : "#1F2937"}`,
                borderRadius: 8,
                padding: "9px 12px",
                color: "#E8EDF5",
                fontSize: 13,
                outline: "none",
                boxSizing: "border-box",
              }}
            />
            {errors.password && (
              <div style={{ color: "#F87171", fontSize: 11, marginTop: 4 }}>{errors.password}</div>
            )}
          </div>

          {/* Підтвердження пароля */}
          <div>
            <label style={{ display: "block", color: "#9CA3AF", fontSize: 12, fontWeight: 500, marginBottom: 6 }}>
              {t("confirmPasswordLabel")}
            </label>
            <input
              type="password"
              value={confirmPassword}
              onChange={(e) => setConfirmPassword(e.target.value)}
              placeholder={t("confirmPasswordPlaceholder")}
              style={{
                width: "100%",
                background: "#111827",
                border: `1px solid ${errors.confirmPassword ? "#7F1D1D" : "#1F2937"}`,
                borderRadius: 8,
                padding: "9px 12px",
                color: "#E8EDF5",
                fontSize: 13,
                outline: "none",
                boxSizing: "border-box",
              }}
            />
            {errors.confirmPassword && (
              <div style={{ color: "#F87171", fontSize: 11, marginTop: 4 }}>{errors.confirmPassword}</div>
            )}
          </div>

          {/* Actions */}
          <div style={{ display: "flex", gap: 10, marginTop: 4 }}>
            <Btn type="submit" disabled={createUser.isPending} style={{ flex: 1, justifyContent: "center" }}>
              {createUser.isPending ? t("creating") : t("submitButton")}
            </Btn>
            <Btn type="button" variant="ghost" onClick={onClose}>
              {t("cancelButton")}
            </Btn>
          </div>
        </form>
        )}
      </div>
    </div>
  );
}
