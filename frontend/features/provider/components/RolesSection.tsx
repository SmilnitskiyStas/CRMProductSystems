"use client";

import { useState } from "react";
import { Shield, Plus, Pencil, Trash2, X, ChevronDown, ChevronUp } from "lucide-react";
import { useProviderRoles, useCreateRole, useUpdateRole, useDeleteRole } from "../hooks/useProviderRoles";
import { PROVIDER_PERMISSIONS, ALL_PERMISSIONS } from "@/lib/providerPermissions";
import type { ProviderRoleDto } from "../api/providerRolesApi";

const BASE_ROLE_LABELS: Record<string, string> = {
  provider_admin: "Адмін провайдера",
  provider_agent: "Агент підтримки",
};

export function RolesSection() {
  const { data: roles } = useProviderRoles();
  const [expanded, setExpanded] = useState(false);
  const [editRole, setEditRole] = useState<ProviderRoleDto | null>(null);
  const [creating, setCreating] = useState(false);
  const deleteRole = useDeleteRole();

  if (!expanded) {
    return (
      <div style={{ marginTop: 16 }}>
        <button
          onClick={() => setExpanded(true)}
          style={{
            display: "flex", alignItems: "center", gap: 6,
            padding: "8px 14px", borderRadius: 8,
            background: "transparent", border: "1px solid #1F2937",
            color: "#6B7280", fontSize: 12, cursor: "pointer",
          }}
        >
          <Shield size={13} />
          Управління ролями ({roles?.length ?? 0})
          <ChevronDown size={13} />
        </button>
      </div>
    );
  }

  return (
    <div style={{ marginTop: 20, border: "1px solid #1F2937", borderRadius: 10, overflow: "hidden" }}>
      <div
        style={{
          display: "flex", alignItems: "center", justifyContent: "space-between",
          padding: "12px 16px", background: "#0D1117", cursor: "pointer",
        }}
        onClick={() => setExpanded(false)}
      >
        <div style={{ display: "flex", alignItems: "center", gap: 6 }}>
          <Shield size={14} color="#60A5FA" />
          <span style={{ color: "#E8EDF5", fontSize: 14, fontWeight: 600 }}>Управління ролями</span>
        </div>
        <ChevronUp size={14} color="#6B7280" />
      </div>

      <div style={{ padding: "12px 16px", display: "flex", flexDirection: "column", gap: 8 }}>
        {roles?.map((role) => (
          <div
            key={role.id}
            style={{
              display: "flex", alignItems: "flex-start", justifyContent: "space-between",
              background: "#111827", border: "1px solid #1F2937", borderRadius: 8,
              padding: "10px 14px", gap: 12,
            }}
          >
            <div style={{ flex: 1, minWidth: 0 }}>
              <div style={{ display: "flex", alignItems: "center", gap: 8, marginBottom: 4 }}>
                <span style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 500 }}>{role.displayName}</span>
                {role.isSystem && (
                  <span style={{
                    fontSize: 10, padding: "1px 6px", borderRadius: 4,
                    background: "#1F2937", color: "#4B5563",
                  }}>Системна</span>
                )}
                <span style={{ color: "#4B5563", fontSize: 11 }}>
                  ({BASE_ROLE_LABELS[role.baseRole] ?? role.baseRole})
                </span>
              </div>
              <div style={{ display: "flex", flexWrap: "wrap", gap: 4 }}>
                {role.permissions.map((p) => (
                  <span
                    key={p}
                    style={{
                      fontSize: 11, padding: "2px 6px", borderRadius: 4,
                      background: "#1E3A5F", color: "#93C5FD",
                      border: "1px solid #1D4ED8",
                    }}
                  >
                    {PROVIDER_PERMISSIONS[p] ?? p}
                  </span>
                ))}
                {role.permissions.length === 0 && (
                  <span style={{ color: "#4B5563", fontSize: 11 }}>Немає прав</span>
                )}
              </div>
            </div>
            {!role.isSystem && (
              <div style={{ display: "flex", gap: 4, flexShrink: 0 }}>
                <button
                  onClick={() => setEditRole(role)}
                  style={{ background: "none", border: "none", color: "#4B5563", cursor: "pointer", padding: 4 }}
                  onMouseEnter={(e) => { (e.currentTarget as HTMLElement).style.color = "#60A5FA"; }}
                  onMouseLeave={(e) => { (e.currentTarget as HTMLElement).style.color = "#4B5563"; }}
                >
                  <Pencil size={13} />
                </button>
                <button
                  onClick={() => {
                    if (confirm(`Видалити роль "${role.displayName}"?`)) {
                      deleteRole.mutate(role.id);
                    }
                  }}
                  style={{ background: "none", border: "none", color: "#4B5563", cursor: "pointer", padding: 4 }}
                  onMouseEnter={(e) => { (e.currentTarget as HTMLElement).style.color = "#F87171"; }}
                  onMouseLeave={(e) => { (e.currentTarget as HTMLElement).style.color = "#4B5563"; }}
                >
                  <Trash2 size={13} />
                </button>
              </div>
            )}
          </div>
        ))}

        <button
          onClick={() => setCreating(true)}
          style={{
            display: "flex", alignItems: "center", gap: 6,
            padding: "8px 14px", borderRadius: 8,
            background: "#1D3461", border: "1px solid #3B82F6",
            color: "#93C5FD", fontSize: 12, fontWeight: 600, cursor: "pointer",
            marginTop: 4, alignSelf: "flex-start",
          }}
        >
          <Plus size={13} />
          Нова роль
        </button>
      </div>

      {creating && (
        <RoleFormModal
          onClose={() => setCreating(false)}
        />
      )}
      {editRole && (
        <RoleFormModal
          role={editRole}
          onClose={() => setEditRole(null)}
        />
      )}
    </div>
  );
}

interface RoleFormModalProps {
  role?: ProviderRoleDto;
  onClose: () => void;
}

function RoleFormModal({ role, onClose }: RoleFormModalProps) {
  const create = useCreateRole();
  const update = useUpdateRole();
  const [displayName, setDisplayName] = useState(role?.displayName ?? "");
  const [baseRole, setBaseRole] = useState(role?.baseRole ?? "provider_agent");
  const [permissions, setPermissions] = useState<Set<string>>(new Set(role?.permissions ?? []));
  const [error, setError] = useState<string | null>(null);

  const isEdit = !!role;

  function togglePerm(p: string) {
    setPermissions((prev) => {
      const next = new Set(prev);
      if (next.has(p)) next.delete(p);
      else next.add(p);
      return next;
    });
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    const req = { displayName, baseRole, permissions: [...permissions] };
    try {
      if (isEdit) {
        await update.mutateAsync({ id: role.id, req });
      } else {
        await create.mutateAsync(req);
      }
      onClose();
    } catch (err: unknown) {
      const e = err as { error?: string };
      setError(e?.error ?? "Помилка збереження");
    }
  }

  const isPending = create.isPending || update.isPending;

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
          width: "100%", maxWidth: 480,
        }}
        onClick={(e) => e.stopPropagation()}
      >
        <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginBottom: 20 }}>
          <h2 style={{ color: "#E8EDF5", fontSize: 16, fontWeight: 700, margin: 0 }}>
            {isEdit ? "Редагувати роль" : "Нова роль"}
          </h2>
          <button onClick={onClose} style={{ background: "none", border: "none", color: "#6B7280", cursor: "pointer" }}>
            <X size={18} />
          </button>
        </div>

        <form onSubmit={handleSubmit} style={{ display: "flex", flexDirection: "column", gap: 14 }}>
          <input
            required
            type="text"
            placeholder="Назва ролі"
            value={displayName}
            onChange={(e) => setDisplayName(e.target.value)}
            style={inputStyle}
          />

          <div>
            <div style={{ color: "#6B7280", fontSize: 12, marginBottom: 6 }}>Базова роль (рівень доступу)</div>
            <select
              value={baseRole}
              onChange={(e) => setBaseRole(e.target.value)}
              style={{ ...inputStyle, appearance: "none" }}
            >
              <option value="provider_admin">Адмін провайдера</option>
              <option value="provider_agent">Агент підтримки</option>
            </select>
          </div>

          <div>
            <div style={{ color: "#6B7280", fontSize: 12, marginBottom: 8 }}>Права доступу</div>
            <div style={{ display: "flex", flexDirection: "column", gap: 6 }}>
              {ALL_PERMISSIONS.map((p) => (
                <label
                  key={p}
                  style={{ display: "flex", alignItems: "center", gap: 8, cursor: "pointer" }}
                >
                  <input
                    type="checkbox"
                    checked={permissions.has(p)}
                    onChange={() => togglePerm(p)}
                    style={{ width: 14, height: 14, cursor: "pointer", accentColor: "#3B82F6" }}
                  />
                  <span style={{ color: permissions.has(p) ? "#E8EDF5" : "#4B5563", fontSize: 13 }}>
                    {PROVIDER_PERMISSIONS[p]}
                  </span>
                </label>
              ))}
            </div>
          </div>

          {error && (
            <div style={{ color: "#F87171", fontSize: 13, padding: "8px 12px", background: "#1F0A0A", borderRadius: 8, border: "1px solid #7F1D1D" }}>
              {error}
            </div>
          )}

          <button
            type="submit"
            disabled={isPending}
            style={{
              padding: "11px 0", borderRadius: 9,
              background: isPending ? "#1D3461" : "linear-gradient(135deg, #3B82F6, #6366F1)",
              border: "none", color: "#fff", fontSize: 14, fontWeight: 600,
              cursor: isPending ? "not-allowed" : "pointer", marginTop: 4,
            }}
          >
            {isPending ? "Збереження…" : (isEdit ? "Зберегти" : "Створити роль")}
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
