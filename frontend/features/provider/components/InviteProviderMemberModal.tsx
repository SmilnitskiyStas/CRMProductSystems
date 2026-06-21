"use client";

import { useState } from "react";
import { X } from "lucide-react";
import { useInviteProviderMember } from "../hooks/useProviderTeam";

interface Props {
  onClose: () => void;
}

const ROLE_PERMISSIONS: Record<string, string[]> = {
  provider_admin: [
    "Управління командою провайдера",
    "Перегляд усіх клієнтів",
    "Service Desk та тікети",
    "Живий чат",
    "Адмін-панель",
  ],
  provider_agent: [
    "Перегляд клієнтів",
    "Service Desk та тікети",
    "Живий чат",
  ],
};

export function InviteProviderMemberModal({ onClose }: Props) {
  const invite = useInviteProviderMember();
  const [form, setForm] = useState({
    email: "",
    fullName: "",
    role: "provider_agent",
    password: "",
    confirmPassword: "",
  });
  const [error, setError] = useState<string | null>(null);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);

    if (form.password.length < 6) {
      setError("Пароль мінімум 6 символів");
      return;
    }
    if (form.password !== form.confirmPassword) {
      setError("Паролі не співпадають");
      return;
    }

    try {
      await invite.mutateAsync({
        email: form.email,
        fullName: form.fullName,
        role: form.role,
        password: form.password,
      });
      onClose();
    } catch (err: unknown) {
      const e = err as { error?: string };
      setError(e?.error ?? "Помилка створення користувача");
    }
  }

  const permissions = ROLE_PERMISSIONS[form.role] ?? [];

  return (
    <div
      style={{
        position: "fixed", inset: 0, zIndex: 999,
        background: "rgba(0,0,0,0.6)",
        display: "flex", alignItems: "center", justifyContent: "center",
      }}
      onClick={onClose}
    >
      <div
        style={{
          background: "#111827", border: "1px solid #1F2937",
          borderRadius: 14, padding: "28px 32px",
          width: "100%", maxWidth: 440,
        }}
        onClick={(e) => e.stopPropagation()}
      >
        <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginBottom: 20 }}>
          <h2 style={{ color: "#E8EDF5", fontSize: 17, fontWeight: 700, margin: 0 }}>Створити користувача</h2>
          <button onClick={onClose} style={{ background: "none", border: "none", color: "#6B7280", cursor: "pointer" }}>
            <X size={18} />
          </button>
        </div>

        <form onSubmit={handleSubmit} style={{ display: "flex", flexDirection: "column", gap: 14 }}>
          <input
            required
            type="text"
            placeholder="Повне ім'я"
            value={form.fullName}
            onChange={(e) => setForm((f) => ({ ...f, fullName: e.target.value }))}
            style={inputStyle}
          />
          <input
            required
            type="email"
            placeholder="Email"
            value={form.email}
            onChange={(e) => setForm((f) => ({ ...f, email: e.target.value }))}
            style={inputStyle}
          />
          <select
            value={form.role}
            onChange={(e) => setForm((f) => ({ ...f, role: e.target.value }))}
            style={{ ...inputStyle, appearance: "none" }}
          >
            <option value="provider_admin">Адмін провайдера</option>
            <option value="provider_agent">Агент підтримки</option>
          </select>

          {permissions.length > 0 && (
            <div
              style={{
                background: "#0D1117",
                border: "1px solid #1F2937",
                borderRadius: 8,
                padding: "10px 14px",
              }}
            >
              <div style={{ color: "#6B7280", fontSize: 12, marginBottom: 6 }}>Права доступу</div>
              {permissions.map((p) => (
                <div key={p} style={{ color: "#6B7280", fontSize: 12, lineHeight: "1.8" }}>
                  ✓ {p}
                </div>
              ))}
            </div>
          )}

          <input
            required
            type="password"
            placeholder="Пароль"
            value={form.password}
            onChange={(e) => setForm((f) => ({ ...f, password: e.target.value }))}
            style={inputStyle}
          />
          <input
            required
            type="password"
            placeholder="Підтвердження паролю"
            value={form.confirmPassword}
            onChange={(e) => setForm((f) => ({ ...f, confirmPassword: e.target.value }))}
            style={inputStyle}
          />

          {error && (
            <div style={{ color: "#F87171", fontSize: 13, padding: "8px 12px", background: "#1F0A0A", borderRadius: 8, border: "1px solid #7F1D1D" }}>
              {error}
            </div>
          )}

          <button
            type="submit"
            disabled={invite.isPending}
            style={{
              padding: "11px 0", borderRadius: 9,
              background: invite.isPending ? "#1D3461" : "linear-gradient(135deg, #3B82F6, #6366F1)",
              border: "none", color: "#fff", fontSize: 14, fontWeight: 600,
              cursor: invite.isPending ? "not-allowed" : "pointer",
              marginTop: 4,
            }}
          >
            {invite.isPending ? "Створення…" : "Створити користувача"}
          </button>
        </form>
      </div>
    </div>
  );
}

const inputStyle: React.CSSProperties = {
  background: "#0D1117",
  border: "1px solid #1F2937",
  borderRadius: 8,
  padding: "10px 14px",
  color: "#E8EDF5",
  fontSize: 13,
  outline: "none",
  width: "100%",
  boxSizing: "border-box",
};
