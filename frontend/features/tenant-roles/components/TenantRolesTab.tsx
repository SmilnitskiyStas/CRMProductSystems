"use client";

import { useMemo, useState } from "react";
import { useTranslations } from "next-intl";
import { Plus, Pencil, Archive, Shield } from "lucide-react";
import {
  useTenantRoles,
  useTenantRoleCapabilities,
  useTenantRoleTabs,
  useCreateTenantRole,
  useUpdateTenantRole,
  useArchiveTenantRole,
} from "../hooks/useTenantRoles";
import { useUsers } from "@/features/users/hooks/useUsers";
import type { TenantRoleDto } from "../types";
import { Btn } from "@/components/ui/Btn";

/**
 * Management UI for TenantRole capability templates (ADR-020, TASK-345/346).
 * Rendered as the "Шаблони ролей" tab on /users — caller (page.tsx) gates visibility to
 * enterprise_admin+, mirroring backend's AtLeastEnterpriseAdmin-only TenantRolesController.
 */
export function TenantRolesTab() {
  const t = useTranslations("Dashboard.tenantRoles.tab");
  const [showArchived, setShowArchived] = useState(false);
  const { data: roles, isLoading, isError } = useTenantRoles(showArchived);
  const { data: users } = useUsers();
  const [editRole, setEditRole] = useState<TenantRoleDto | null>(null);
  const [creating, setCreating] = useState(false);
  const archiveRole = useArchiveTenantRole();

  // Assigned-user count per template — cheap to derive client-side from the already-fetched
  // users list rather than adding a backend count endpoint.
  const assignedCounts = useMemo(() => {
    const counts = new Map<string, number>();
    for (const u of users ?? []) {
      if (!u.tenantRoleId) continue;
      counts.set(u.tenantRoleId, (counts.get(u.tenantRoleId) ?? 0) + 1);
    }
    return counts;
  }, [users]);

  async function handleArchive(role: TenantRoleDto) {
    if (!confirm(t("archiveConfirm", { name: role.name }))) return;
    try {
      await archiveRole.mutateAsync(role.id);
    } catch (e) {
      alert((e as Error)?.message ?? t("archiveError"));
    }
  }

  return (
    <div
      style={{
        background: "#111827", border: "1px solid #1F2937",
        borderRadius: 12, padding: "20px 24px",
      }}
    >
      <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginBottom: 20, gap: 12, flexWrap: "wrap" }}>
        <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
          <Shield size={18} color="#60A5FA" />
          <h2 style={{ color: "#E8EDF5", fontSize: 16, fontWeight: 600, margin: 0 }}>
            {t("title")}
          </h2>
          {roles && (
            <span style={{
              padding: "2px 8px", borderRadius: 10,
              background: "#1F2937", color: "#9CA3AF", fontSize: 11,
            }}>
              {roles.length}
            </span>
          )}
        </div>
        <div style={{ display: "flex", alignItems: "center", gap: 14 }}>
          <label style={{ display: "flex", alignItems: "center", gap: 6, color: "#6B7280", fontSize: 12, cursor: "pointer" }}>
            <input
              type="checkbox"
              checked={showArchived}
              onChange={(e) => setShowArchived(e.target.checked)}
              style={{ width: 13, height: 13, cursor: "pointer", accentColor: "#3B82F6" }}
            />
            {t("showArchived")}
          </label>
          <Btn icon={<Plus size={14} />} onClick={() => setCreating(true)}>
            {t("newTemplate")}
          </Btn>
        </div>
      </div>

      <p style={{ color: "#4B5563", fontSize: 13, marginTop: -10, marginBottom: 18 }}>
        {t("description")}
      </p>

      {isLoading && (
        <div style={{ color: "#4B5563", fontSize: 13 }}>{t("loading")}</div>
      )}

      {isError && (
        <div style={{ color: "#F87171", fontSize: 13 }}>
          {t("loadError")}
        </div>
      )}

      {!isLoading && !isError && (
        <div style={{ display: "flex", flexDirection: "column", gap: 10 }}>
          {roles?.map((role) => (
            <TenantRoleCard
              key={role.id}
              role={role}
              assignedCount={assignedCounts.get(role.id) ?? 0}
              onEdit={() => setEditRole(role)}
              onArchive={() => handleArchive(role)}
              archivePending={archiveRole.isPending}
            />
          ))}

          {(!roles || roles.length === 0) && (
            <div style={{ color: "#4B5563", fontSize: 13 }}>
              {showArchived ? t("emptyArchived") : t("emptyActive")}
            </div>
          )}
        </div>
      )}

      {creating && <TenantRoleFormModal onClose={() => setCreating(false)} />}
      {editRole && <TenantRoleFormModal role={editRole} onClose={() => setEditRole(null)} />}
    </div>
  );
}

/* ─────────────────────────── Template card ─────────────────────────── */

interface CardProps {
  role: TenantRoleDto;
  assignedCount: number;
  onEdit: () => void;
  onArchive: () => void;
  archivePending: boolean;
}

function TenantRoleCard({ role, assignedCount, onEdit, onArchive, archivePending }: CardProps) {
  const t = useTranslations("Dashboard.tenantRoles.card");
  const { data: groups } = useTenantRoleCapabilities();
  const labelFor = useMemo(() => {
    const map = new Map<string, string>();
    for (const g of groups ?? []) {
      for (const c of g.capabilities) map.set(c.key, c.labelUa);
    }
    return (key: string) => map.get(key) ?? key;
  }, [groups]);

  return (
    <div
      style={{
        display: "flex", alignItems: "flex-start", justifyContent: "space-between",
        background: "#0D1117",
        border: "1px solid #1F2937", borderRadius: 10,
        padding: "14px 18px", gap: 12,
        opacity: role.isActive ? 1 : 0.6,
      }}
    >
      <div style={{ flex: 1, minWidth: 0 }}>
        <div style={{ display: "flex", alignItems: "center", gap: 8, marginBottom: 6, flexWrap: "wrap" }}>
          <span style={{ color: "#E8EDF5", fontSize: 14, fontWeight: 600 }}>
            {role.name}
          </span>
          {!role.isActive && (
            <span style={{
              fontSize: 10, padding: "2px 7px", borderRadius: 4,
              background: "#1F2937", color: "#6B7280", border: "1px solid #374151",
            }}>
              {t("archived")}
            </span>
          )}
          <span style={{
            fontSize: 11, padding: "2px 8px", borderRadius: 4,
            background: "#1E3A5F22", color: "#60A5FA", border: "1px solid #1D4ED855",
          }}>
            {t("capabilityCount", { count: role.capabilities.length })}
          </span>
          <span style={{
            fontSize: 11, padding: "2px 8px", borderRadius: 4,
            background: "#062b2922", color: "#2DD4BF", border: "1px solid #0F766E55",
          }}>
            {t("userCount", { count: assignedCount })}
          </span>
        </div>
        <div style={{ display: "flex", flexWrap: "wrap", gap: 5 }}>
          {role.capabilities.length === 0 ? (
            <span style={{ color: "#374151", fontSize: 12 }}>{t("noCapabilities")}</span>
          ) : role.capabilities.map((key) => (
            <span
              key={key}
              style={{
                fontSize: 11, padding: "3px 8px", borderRadius: 5,
                background: "#1E3A5F", color: "#93C5FD",
                border: "1px solid #1D4ED8",
              }}
            >
              {labelFor(key)}
            </span>
          ))}
        </div>
      </div>
      {role.isActive && (
        <div style={{ display: "flex", gap: 4, flexShrink: 0, paddingTop: 2 }}>
          <button
            onClick={onEdit}
            title={t("editTitle")}
            style={{ background: "none", border: "none", color: "#4B5563", cursor: "pointer", padding: 5 }}
            onMouseEnter={(e) => { (e.currentTarget as HTMLElement).style.color = "#60A5FA"; }}
            onMouseLeave={(e) => { (e.currentTarget as HTMLElement).style.color = "#4B5563"; }}
          >
            <Pencil size={14} />
          </button>
          <button
            onClick={onArchive}
            disabled={archivePending}
            title={t("archiveTitle")}
            style={{ background: "none", border: "none", color: "#4B5563", cursor: archivePending ? "not-allowed" : "pointer", padding: 5 }}
            onMouseEnter={(e) => { (e.currentTarget as HTMLElement).style.color = "#F87171"; }}
            onMouseLeave={(e) => { (e.currentTarget as HTMLElement).style.color = "#4B5563"; }}
          >
            <Archive size={14} />
          </button>
        </div>
      )}
    </div>
  );
}

/* ─────────────────────────── Create/edit modal ─────────────────────────── */

interface FormModalProps {
  role?: TenantRoleDto;
  onClose: () => void;
}

function TenantRoleFormModal({ role, onClose }: FormModalProps) {
  const t = useTranslations("Dashboard.tenantRoles.formModal");
  const { data: groups, isLoading: groupsLoading } = useTenantRoleCapabilities();
  const { data: tabs, isLoading: tabsLoading } = useTenantRoleTabs();
  const create = useCreateTenantRole();
  const update = useUpdateTenantRole();

  const [name, setName] = useState(role?.name ?? "");
  const [capabilities, setCapabilities] = useState<Set<string>>(new Set(role?.capabilities ?? []));
  const [allowedTabs, setAllowedTabs] = useState<Set<string>>(new Set(role?.allowedTabs ?? []));
  const [error, setError] = useState<string | null>(null);

  const isEdit = !!role;
  const isPending = create.isPending || update.isPending;

  function toggleCap(key: string) {
    setCapabilities((prev) => {
      const next = new Set(prev);
      if (next.has(key)) next.delete(key);
      else next.add(key);
      return next;
    });
  }

  function toggleTab(key: string) {
    setAllowedTabs((prev) => {
      const next = new Set(prev);
      if (next.has(key)) next.delete(key);
      else next.add(key);
      return next;
    });
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    const req = { name: name.trim(), capabilities: [...capabilities], allowedTabs: [...allowedTabs] };
    try {
      if (isEdit) await update.mutateAsync({ id: role.id, data: req });
      else await create.mutateAsync(req);
      onClose();
    } catch (err) {
      setError((err as Error)?.message ?? t("saveError"));
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
          width: "100%", maxWidth: 520,
          maxHeight: "90vh", overflowY: "auto",
        }}
        onClick={(e) => e.stopPropagation()}
      >
        <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginBottom: 20 }}>
          <h2 style={{ color: "#E8EDF5", fontSize: 16, fontWeight: 700, margin: 0 }}>
            {isEdit ? t("editTitle") : t("newTitle")}
          </h2>
          <button onClick={onClose} style={{ background: "none", border: "none", color: "#6B7280", cursor: "pointer" }}>
            ✕
          </button>
        </div>

        <form onSubmit={handleSubmit} style={{ display: "flex", flexDirection: "column", gap: 14 }}>
          <input
            required
            type="text"
            placeholder={t("namePlaceholder")}
            value={name}
            onChange={(e) => setName(e.target.value)}
            style={inputStyle}
          />

          <div>
            <div style={{ color: "#6B7280", fontSize: 12, marginBottom: 8 }}>{t("capabilitiesLabel")}</div>
            {groupsLoading && (
              <div style={{ color: "#4B5563", fontSize: 13 }}>{t("loading")}</div>
            )}
            <div style={{ display: "flex", flexDirection: "column", gap: 12 }}>
              {groups?.map((group) => (
                <div
                  key={group.specialty}
                  style={{
                    background: "#0D1117", border: "1px solid #1F2937",
                    borderRadius: 8, padding: "10px 14px",
                  }}
                >
                  <div style={{ color: "#9CA3AF", fontSize: 11, fontWeight: 700, textTransform: "uppercase", letterSpacing: "0.05em", marginBottom: 8 }}>
                    {group.specialty}
                  </div>
                  <div style={{ display: "flex", flexDirection: "column", gap: 7 }}>
                    {group.capabilities.map((cap) => (
                      <label key={cap.key} style={{ display: "flex", alignItems: "center", gap: 8, cursor: "pointer" }}>
                        <input
                          type="checkbox"
                          checked={capabilities.has(cap.key)}
                          onChange={() => toggleCap(cap.key)}
                          style={{ width: 14, height: 14, cursor: "pointer", accentColor: "#3B82F6" }}
                        />
                        <span style={{ color: capabilities.has(cap.key) ? "#E8EDF5" : "#4B5563", fontSize: 13 }}>
                          {cap.labelUa}
                        </span>
                      </label>
                    ))}
                  </div>
                </div>
              ))}
            </div>
          </div>

          <div>
            <div style={{ color: "#6B7280", fontSize: 12, marginBottom: 8 }}>{t("tabsLabel")}</div>
            {tabsLoading && (
              <div style={{ color: "#4B5563", fontSize: 13 }}>{t("loading")}</div>
            )}
            <div
              style={{
                background: "#0D1117", border: "1px solid #1F2937",
                borderRadius: 8, padding: "10px 14px",
              }}
            >
              <div style={{ display: "flex", flexDirection: "column", gap: 7 }}>
                {tabs?.map((tab) => (
                  <label key={tab.key} style={{ display: "flex", alignItems: "center", gap: 8, cursor: "pointer" }}>
                    <input
                      type="checkbox"
                      checked={allowedTabs.has(tab.key)}
                      onChange={() => toggleTab(tab.key)}
                      style={{ width: 14, height: 14, cursor: "pointer", accentColor: "#3B82F6" }}
                    />
                    <span style={{ color: allowedTabs.has(tab.key) ? "#E8EDF5" : "#4B5563", fontSize: 13 }}>
                      {tab.labelUa}
                    </span>
                  </label>
                ))}
              </div>
            </div>
          </div>

          {error && (
            <div style={{
              color: "#F87171", fontSize: 13, padding: "8px 12px",
              background: "#1F0A0A", borderRadius: 8, border: "1px solid #7F1D1D",
            }}>
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
            {isPending ? t("savingButton") : (isEdit ? t("saveChangesButton") : t("createButton"))}
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
