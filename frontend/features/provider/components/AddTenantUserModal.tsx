"use client";

import { useState } from "react";
import { X } from "lucide-react";
import { useCreateTenantUser } from "../hooks/useProvider";
import type { TenantUserDto } from "../types";

interface Props {
  tenantId: string;
  onClose: () => void;
  onCreated: (user: TenantUserDto) => void;
}

export function AddTenantUserModal({ tenantId, onClose, onCreated }: Props) {
  const createUser = useCreateTenantUser(tenantId);

  const [fullName, setFullName]           = useState("");
  const [email, setEmail]                 = useState("");
  const [password, setPassword]           = useState("");
  const [confirmPassword, setConfirmPassword] = useState("");
  const [errors, setErrors]               = useState<Record<string, string>>({});
  const [serverError, setServerError]     = useState("");

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
      const user = await createUser.mutateAsync({ fullName: fullName.trim(), email: email.trim(), password });
      onCreated(user);
      onClose();
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

        {/* Form */}
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
            <button
              type="submit"
              disabled={createUser.isPending}
              style={{
                flex: 1,
                padding: "9px 18px",
                borderRadius: 8,
                background: createUser.isPending ? "#1A1F2C" : "#1D3461",
                border: `1px solid ${createUser.isPending ? "#374151" : "#3B82F6"}`,
                color: createUser.isPending ? "#4B5563" : "#93C5FD",
                fontSize: 13,
                fontWeight: 600,
                cursor: createUser.isPending ? "default" : "pointer",
              }}
            >
              {createUser.isPending ? "Створення…" : "Додати адміністратора"}
            </button>
            <button
              type="button"
              onClick={onClose}
              style={{
                padding: "9px 16px",
                borderRadius: 8,
                background: "transparent",
                border: "1px solid #374151",
                color: "#6B7280",
                fontSize: 13,
                fontWeight: 600,
                cursor: "pointer",
              }}
            >
              Скасувати
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}
