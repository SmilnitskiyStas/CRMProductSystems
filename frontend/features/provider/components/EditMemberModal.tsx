"use client";

import { useState } from "react";
import { X } from "lucide-react";
import { useUpdateMember } from "../hooks/useProviderTeam";
import type { ProviderTeamMemberDto } from "../api/providerTeamApi";

interface Props {
  member: ProviderTeamMemberDto;
  onClose: () => void;
}

export function EditMemberModal({ member, onClose }: Props) {
  const update = useUpdateMember();
  const [form, setForm] = useState({ fullName: member.fullName, role: member.role });
  const [error, setError] = useState<string | null>(null);

  const isOwner = member.role === "provider";

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    try {
      await update.mutateAsync({ memberId: member.id, req: form });
      onClose();
    } catch (err: unknown) {
      const e = err as { error?: string };
      setError(e?.error ?? "Помилка оновлення");
    }
  }

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
          <h2 style={{ color: "#E8EDF5", fontSize: 17, fontWeight: 700, margin: 0 }}>Редагувати учасника</h2>
          <button onClick={onClose} style={{ background: "none", border: "none", color: "#6B7280", cursor: "pointer" }}>
            <X size={18} />
          </button>
        </div>

        <div style={{ color: "#4B5563", fontSize: 12, marginBottom: 16 }}>{member.email}</div>

        <form onSubmit={handleSubmit} style={{ display: "flex", flexDirection: "column", gap: 14 }}>
          <input
            required
            type="text"
            placeholder="Повне ім'я"
            value={form.fullName}
            onChange={(e) => setForm((f) => ({ ...f, fullName: e.target.value }))}
            style={inputStyle}
          />
          <select
            value={form.role}
            disabled={isOwner}
            onChange={(e) => setForm((f) => ({ ...f, role: e.target.value }))}
            style={{ ...inputStyle, appearance: "none", opacity: isOwner ? 0.5 : 1, cursor: isOwner ? "not-allowed" : "pointer" }}
          >
            <option value="provider">Власник</option>
            <option value="provider_admin">Адмін провайдера</option>
            <option value="provider_agent">Агент підтримки</option>
          </select>
          {isOwner && (
            <div style={{ color: "#6B7280", fontSize: 12, marginTop: -8 }}>
              Роль власника не можна змінити
            </div>
          )}

          {error && (
            <div style={{ color: "#F87171", fontSize: 13, padding: "8px 12px", background: "#1F0A0A", borderRadius: 8, border: "1px solid #7F1D1D" }}>
              {error}
            </div>
          )}

          <button
            type="submit"
            disabled={update.isPending}
            style={{
              padding: "11px 0", borderRadius: 9,
              background: update.isPending ? "#1D3461" : "linear-gradient(135deg, #3B82F6, #6366F1)",
              border: "none", color: "#fff", fontSize: 14, fontWeight: 600,
              cursor: update.isPending ? "not-allowed" : "pointer",
              marginTop: 4,
            }}
          >
            {update.isPending ? "Збереження…" : "Зберегти"}
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
