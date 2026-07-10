"use client";

import { useState } from "react";
import { toast } from "sonner";
import { useChangePassword } from "../hooks/useProfile";
import { Btn } from "@/components/ui/Btn";

const POLICY_HINT = "Мінімум 12 символів, літери та цифри";

const inputStyle: React.CSSProperties = {
  width: "100%",
  background: "#0D1117",
  border: "1px solid #374151",
  borderRadius: 8,
  padding: "9px 12px",
  color: "#E8EDF5",
  fontSize: 13,
  outline: "none",
  boxSizing: "border-box",
};

const labelStyle: React.CSSProperties = {
  display: "block",
  color: "#9CA3AF",
  fontSize: 12,
  fontWeight: 500,
  marginBottom: 6,
};

interface FieldError {
  current?: string;
  next?: string;
  confirm?: string;
}

export function ChangePasswordForm() {
  const change = useChangePassword();

  const [current, setCurrent]   = useState("");
  const [next, setNext]         = useState("");
  const [confirm, setConfirm]   = useState("");
  const [errors, setErrors]     = useState<FieldError>({});

  function validate(): boolean {
    const e: FieldError = {};
    if (!current.trim()) e.current = "Введіть поточний пароль";
    if (next.length < 12) e.next = "Мінімум 12 символів";
    else if (!/[a-zA-Zа-яА-ЯіІїЇєЄґҐ]/.test(next) || !/\d/.test(next))
      e.next = "Пароль має містити літери та цифри";
    if (next !== confirm)  e.confirm = "Паролі не співпадають";
    setErrors(e);
    return Object.keys(e).length === 0;
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!validate() || change.isPending) return;

    try {
      await change.mutateAsync({ currentPassword: current, newPassword: next });
      setCurrent(""); setNext(""); setConfirm("");
      // Backend revokes all refresh sessions on password change — the current
      // access token stays valid ≤15 min, all other devices get logged out.
      toast.success("Пароль оновлено", {
        description: "Інші пристрої буде розлогінено.",
      });
    } catch {
      // API error is rendered below via change.error
    }
  }

  function field(
    label: string,
    value: string,
    onChange: (v: string) => void,
    error?: string,
    placeholder = "",
  ) {
    return (
      <div>
        <label style={labelStyle}>{label}</label>
        <input
          type="password"
          value={value}
          onChange={(e) => { onChange(e.target.value); setErrors((err) => ({ ...err })); }}
          placeholder={placeholder}
          autoComplete="new-password"
          style={{ ...inputStyle, borderColor: error ? "#EF4444" : "#374151" }}
        />
        {error && <p style={{ color: "#EF4444", fontSize: 11, marginTop: 4 }}>{error}</p>}
      </div>
    );
  }

  return (
    <form onSubmit={handleSubmit}>
      <div style={{ display: "flex", flexDirection: "column", gap: 14, marginBottom: 8 }}>
        {field("Поточний пароль", current, setCurrent, errors.current)}
        {field("Новий пароль",    next,    setNext,    errors.next,    POLICY_HINT)}
        {field("Підтвердження",   confirm, setConfirm, errors.confirm)}
      </div>

      <p style={{ color: "#4B5563", fontSize: 11, margin: "0 0 20px", lineHeight: 1.5 }}>
        {POLICY_HINT}. Після зміни пароля всі інші пристрої буде розлогінено.
      </p>

      <div style={{ display: "flex", alignItems: "center", gap: 12 }}>
        <Btn type="submit" disabled={change.isPending}>
          {change.isPending ? "Оновлення…" : "Змінити пароль"}
        </Btn>
        {change.isError && (
          <span style={{ color: "#F87171", fontSize: 13 }}>
            {change.error.message}
          </span>
        )}
      </div>
    </form>
  );
}
