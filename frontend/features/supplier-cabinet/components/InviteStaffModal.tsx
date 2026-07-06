"use client";

import { useState } from "react";
import { Btn } from "@/components/ui/Btn";
import { useInviteCabinetStaff, useSupplierRoles } from "../hooks/useSupplierCabinet";

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

interface Props {
  onClose: () => void;
}

export function InviteStaffModal({ onClose }: Props) {
  const invite = useInviteCabinetStaff();
  const { data: roles } = useSupplierRoles();

  const [email, setEmail] = useState("");
  const [fullName, setFullName] = useState("");
  const [password, setPassword] = useState("");
  const [supplierRoleId, setSupplierRoleId] = useState("");
  const [errors, setErrors] = useState<Record<string, string>>({});

  function validate() {
    const e: Record<string, string> = {};
    if (!email.trim() || !email.includes("@")) e.email = "Введіть коректний email";
    if (!fullName.trim()) e.fullName = "Введіть ім'я";
    if (password.length < 8) e.password = "Мінімум 8 символів";
    setErrors(e);
    return Object.keys(e).length === 0;
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!validate()) return;

    await invite.mutateAsync({
      email: email.trim().toLowerCase(),
      fullName: fullName.trim(),
      password,
      supplierRoleId: supplierRoleId || undefined,
    });
    onClose();
  }

  return (
    <>
      {/* Backdrop */}
      <div
        onClick={onClose}
        style={{
          position: "fixed", inset: 0,
          background: "rgba(0,0,0,0.6)",
          zIndex: 300,
          backdropFilter: "blur(2px)",
        }}
      />

      {/* Modal */}
      <div
        style={{
          position: "fixed",
          top: "50%", left: "50%",
          transform: "translate(-50%, -50%)",
          width: "min(480px, 95vw)",
          background: "#0D1117",
          border: "1px solid #1F2937",
          borderRadius: 14,
          zIndex: 301,
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
            padding: "18px 22px",
            borderBottom: "1px solid #1F2937",
          }}
        >
          <div>
            <h2 style={{ color: "#E8EDF5", fontSize: 15, fontWeight: 700, margin: 0 }}>
              Запросити співробітника
            </h2>
            <p style={{ color: "#4B5563", fontSize: 12, margin: "3px 0 0" }}>
              Новий акаунт буде створено у вашому кабінеті постачальника
            </p>
          </div>
          <button
            onClick={onClose}
            style={{
              background: "transparent", border: "1px solid #1F2937",
              borderRadius: 8, padding: "5px 9px",
              color: "#4B5563", fontSize: 16, cursor: "pointer",
            }}
          >
            ✕
          </button>
        </div>

        {/* Form */}
        <form onSubmit={handleSubmit} style={{ padding: 22 }}>
          <div style={{ display: "flex", flexDirection: "column", gap: 14 }}>
            {/* Full name */}
            <div>
              <label style={labelStyle}>Повне ім&apos;я *</label>
              <input
                value={fullName}
                onChange={(e) => setFullName(e.target.value)}
                placeholder="Іван Петренко"
                style={{ ...inputStyle, borderColor: errors.fullName ? "#EF4444" : "#374151" }}
              />
              {errors.fullName && (
                <p style={{ color: "#EF4444", fontSize: 11, marginTop: 4 }}>{errors.fullName}</p>
              )}
            </div>

            {/* Email */}
            <div>
              <label style={labelStyle}>Email *</label>
              <input
                type="email"
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                placeholder="ivan@example.com"
                style={{ ...inputStyle, borderColor: errors.email ? "#EF4444" : "#374151" }}
              />
              {errors.email && (
                <p style={{ color: "#EF4444", fontSize: 11, marginTop: 4 }}>{errors.email}</p>
              )}
            </div>

            {/* Temporary password */}
            <div>
              <label style={labelStyle}>Тимчасовий пароль *</label>
              <input
                type="password"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                placeholder="Мінімум 8 символів"
                autoComplete="new-password"
                style={{ ...inputStyle, borderColor: errors.password ? "#EF4444" : "#374151" }}
              />
              {errors.password && (
                <p style={{ color: "#EF4444", fontSize: 11, marginTop: 4 }}>{errors.password}</p>
              )}
            </div>

            {/* Role */}
            <div>
              <label style={labelStyle}>Роль</label>
              <select
                value={supplierRoleId}
                onChange={(e) => setSupplierRoleId(e.target.value)}
                style={{ ...inputStyle, appearance: "none" }}
              >
                <option value="">Повний доступ (без ролі)</option>
                {roles?.map((role) => (
                  <option key={role.id} value={role.id}>
                    {role.displayName}
                  </option>
                ))}
              </select>
            </div>
          </div>

          {/* Actions */}
          <div style={{ display: "flex", gap: 10, marginTop: 22 }}>
            <Btn type="submit" disabled={invite.isPending} style={{ flex: 1, justifyContent: "center" }}>
              {invite.isPending ? "Створення…" : "Запросити"}
            </Btn>
            <Btn type="button" variant="ghost" onClick={onClose}>
              Скасувати
            </Btn>
          </div>

          {invite.isError && (
            <p style={{ color: "#F87171", fontSize: 12, marginTop: 10 }}>
              {(invite.error as Error)?.message ?? "Помилка створення"}
            </p>
          )}
        </form>
      </div>
    </>
  );
}
