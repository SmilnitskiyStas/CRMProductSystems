"use client";

import { useState, useMemo } from "react";
import { X } from "lucide-react";
import { useUpdateMember } from "../hooks/useProviderTeam";
import { useProviderRoles } from "../hooks/useProviderRoles";
import type { ProviderTeamMemberDto } from "../api/providerTeamApi";
import {
  PROVIDER_PERMISSIONS,
  ALL_PERMISSIONS,
  SYSTEM_ROLE_PERMISSIONS,
  resolvePermissions,
} from "@/lib/providerPermissions";

interface Props {
  member: ProviderTeamMemberDto;
  onClose: () => void;
}

export function EditMemberModal({ member, onClose }: Props) {
  const update = useUpdateMember();
  const { data: customRoles } = useProviderRoles();
  const isOwner = member.role === "provider";

  // Determine initial selectedRoleId
  const initialRoleId = member.providerRoleId
    ? member.providerRoleId
    : member.role === "provider_admin" ? "__admin" : "__agent";

  const [form, setForm] = useState({ fullName: member.fullName, selectedRoleId: initialRoleId });
  const [permOverride, setPermOverride] = useState<Record<string, boolean>>(
    member.permissionsOverride ?? {}
  );
  const [error, setError] = useState<string | null>(null);

  const isSystemRole = form.selectedRoleId.startsWith("__");

  const basePermissions = useMemo(() => {
    if (form.selectedRoleId === "__admin") return SYSTEM_ROLE_PERMISSIONS["provider_admin"] ?? [];
    if (form.selectedRoleId === "__agent") return SYSTEM_ROLE_PERMISSIONS["provider_agent"] ?? [];
    const custom = customRoles?.find((r) => r.id === form.selectedRoleId);
    return custom?.permissions ?? [];
  }, [form.selectedRoleId, customRoles]);

  const systemRole = useMemo(() => {
    if (form.selectedRoleId === "__admin") return "provider_admin";
    const custom = customRoles?.find((r) => r.id === form.selectedRoleId);
    return custom?.baseRole ?? "provider_agent";
  }, [form.selectedRoleId, customRoles]);

  const effectivePermissions = useMemo(
    () => resolvePermissions(basePermissions, permOverride),
    [basePermissions, permOverride]
  );

  function getPermState(p: string): "role" | "override-add" | "override-remove" | "none" {
    if (permOverride[p] === true)  return "override-add";
    if (permOverride[p] === false) return "override-remove";
    if (basePermissions.includes(p)) return "role";
    return "none";
  }

  function togglePerm(p: string) {
    const inBase  = basePermissions.includes(p);
    const current = permOverride[p];
    setPermOverride((prev) => {
      const next = { ...prev };
      if (inBase) {
        if (current === false) delete next[p];
        else next[p] = false;
      } else {
        if (current === true) delete next[p];
        else next[p] = true;
      }
      return next;
    });
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    const overridePayload = Object.keys(permOverride).length > 0 ? permOverride : null;
    try {
      await update.mutateAsync({
        memberId: member.id,
        req: {
          fullName:            form.fullName,
          role:                isOwner ? member.role : systemRole,
          providerRoleId:      isOwner || isSystemRole ? null : form.selectedRoleId,
          permissionsOverride: overridePayload,
        },
      });
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
        padding: "20px",
      }}
      onClick={onClose}
    >
      <div
        style={{
          background: "#111827", border: "1px solid #1F2937",
          borderRadius: 14, padding: "28px 32px",
          width: "100%", maxWidth: 500,
          maxHeight: "90vh", overflowY: "auto",
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
            required type="text" placeholder="Повне ім'я" value={form.fullName}
            onChange={(e) => setForm((f) => ({ ...f, fullName: e.target.value }))}
            style={inputStyle}
          />

          {/* Role selector */}
          {!isOwner && (
            <div>
              <div style={{ color: "#6B7280", fontSize: 12, marginBottom: 6 }}>Роль</div>
              <select
                value={form.selectedRoleId}
                onChange={(e) => {
                  setForm((f) => ({ ...f, selectedRoleId: e.target.value }));
                  setPermOverride({});
                }}
                style={{ ...inputStyle, appearance: "none" }}
              >
                <optgroup label="Системні ролі">
                  <option value="__admin">Адмін провайдера</option>
                  <option value="__agent">Агент підтримки</option>
                </optgroup>
                {customRoles && customRoles.filter((r) => !r.isSystem).length > 0 && (
                  <optgroup label="Кастомні ролі">
                    {customRoles.filter((r) => !r.isSystem).map((r) => (
                      <option key={r.id} value={r.id}>{r.displayName}</option>
                    ))}
                  </optgroup>
                )}
              </select>
            </div>
          )}
          {isOwner && (
            <div style={{ color: "#6B7280", fontSize: 12 }}>Роль власника не можна змінити</div>
          )}

          {/* Permissions */}
          {!isOwner && (
            <div style={{ background: "#0D1117", border: "1px solid #1F2937", borderRadius: 8, padding: "12px 14px" }}>
              <div style={{ color: "#6B7280", fontSize: 12, marginBottom: 10 }}>
                Права доступу
                <span style={{ color: "#374151", marginLeft: 6 }}>
                  (зняте = заблоковано для цього юзера)
                </span>
              </div>
              {ALL_PERMISSIONS.map((p) => {
                const state = getPermState(p);
                const checked = effectivePermissions.includes(p);
                return (
                  <label key={p} style={{ display: "flex", alignItems: "center", gap: 8, marginBottom: 6, cursor: "pointer" }}>
                    <input
                      type="checkbox"
                      checked={checked}
                      onChange={() => togglePerm(p)}
                      style={{ width: 14, height: 14, cursor: "pointer", accentColor: "#3B82F6" }}
                    />
                    <span style={{ color: checked ? "#E8EDF5" : "#4B5563", fontSize: 13, flex: 1 }}>
                      {PROVIDER_PERMISSIONS[p]}
                    </span>
                    {state === "role" && (
                      <span style={{ fontSize: 10, color: "#374151" }}>від ролі</span>
                    )}
                    {state === "override-add" && (
                      <span style={{ fontSize: 10, color: "#4ADE80" }}>+особисто</span>
                    )}
                    {state === "override-remove" && (
                      <span style={{ fontSize: 10, color: "#F87171" }}>-заблоковано</span>
                    )}
                  </label>
                );
              })}
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
              cursor: update.isPending ? "not-allowed" : "pointer", marginTop: 4,
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
