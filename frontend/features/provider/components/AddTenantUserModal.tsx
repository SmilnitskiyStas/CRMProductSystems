"use client";

import { useState } from "react";
import { X, CheckCircle2 } from "lucide-react";
import { useCreateTenantUser } from "../hooks/useProvider";
import type { BusinessType, TenantUserDto } from "../types";
import { Btn } from "@/components/ui/Btn";

interface RoleOption {
  value: string;
  label: string;
  description: string;
}

// Role set depends on tenant business type (ADR-016):
// supplier tenants get ONLY supplier_admin (cabinet-only access),
// regular tenants keep the existing enterprise_admin role.
const SUPPLIER_ROLES: RoleOption[] = [
  {
    value: "supplier_admin",
    label: "Адміністратор постачальника",
    description: "Доступ до кабінету постачальника: профіль, товари, відгуки",
  },
];

const REGULAR_ROLES: RoleOption[] = [
  {
    value: "enterprise_admin",
    label: "Адміністратор підприємства",
    description: "Повний доступ до всіх модулів і налаштувань тенанта",
  },
];

interface Props {
  tenantId: string;
  businessType?: BusinessType;
  onClose: () => void;
  onCreated: (user: TenantUserDto) => void;
}

export function AddTenantUserModal({ tenantId, businessType, onClose, onCreated }: Props) {
  const createUser = useCreateTenantUser(tenantId);

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
    if (!fullName.trim())                      e.fullName = "Введіть ПІБ";
    if (!email.trim())                         e.email    = "Введіть email";
    if (!password)                             e.password = "Введіть пароль";
    else if (password.length < 6)             e.password = "Мінімум 6 символів";
    if (password !== confirmPassword)          e.confirmPassword = "Паролі не збігаються";
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
      setServerError((err as Error)?.message ?? "Помилка при створенні користувача");
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
            Додати адміністратора
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
              Адміністратора {createdUser.email} створено.
            </div>
            {isSupplier && (
              <div style={{ color: "#6B7280", fontSize: 12 }}>
                Постачальник входить через звичайну сторінку логіну
              </div>
            )}
            <Btn
              type="button"
              onClick={() => { onCreated(createdUser); onClose(); }}
              style={{ marginTop: 8, justifyContent: "center", width: "100%" }}
            >
              Закрити
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
              ПІБ
            </label>
            <input
              type="text"
              value={fullName}
              onChange={(e) => setFullName(e.target.value)}
              placeholder="Іванов Іван Іванович"
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
              Email
            </label>
            <input
              type="email"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              placeholder="admin@example.com"
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
              Роль
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
                <div style={{ color: "#E8EDF5", fontSize: 13 }}>Роль: {roles[0].label}</div>
                <div style={{ color: "#6B7280", fontSize: 11, marginTop: 2 }}>{roles[0].description}</div>
              </div>
            )}
          </div>

          {/* Пароль */}
          <div>
            <label style={{ display: "block", color: "#9CA3AF", fontSize: 12, fontWeight: 500, marginBottom: 6 }}>
              Пароль
            </label>
            <input
              type="password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              placeholder="Мінімум 6 символів"
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
              Підтвердження пароля
            </label>
            <input
              type="password"
              value={confirmPassword}
              onChange={(e) => setConfirmPassword(e.target.value)}
              placeholder="Повторіть пароль"
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
              {createUser.isPending ? "Створення…" : "Додати адміністратора"}
            </Btn>
            <Btn type="button" variant="ghost" onClick={onClose}>
              Скасувати
            </Btn>
          </div>
        </form>
        )}
      </div>
    </div>
  );
}
