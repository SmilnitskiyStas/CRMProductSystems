"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import { useTranslations } from "next-intl";
import { Plus, Pencil, Archive, Shield, ChevronDown, ChevronRight } from "lucide-react";
import {
  useTenantRoles,
  useTenantRoleCapabilities,
  useTenantRoleTabs,
  useCreateTenantRole,
  useUpdateTenantRole,
  useArchiveTenantRole,
} from "../hooks/useTenantRoles";
import { useUsers } from "@/features/users/hooks/useUsers";
import type { TenantRoleDto, TenantTabDto, TenantTabGroupDto } from "../types";
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

  /**
   * Toggles a single item-level tab (TASK-399 — per-item granularity, was per-group-only).
   * An item reads as "checked" either because its own key is in `allowedTabs` OR because the
   * whole group is bulk-granted via `group.groupKey` (backward compat with pre-TASK-398
   * "select all" templates — see TenantRoleTabs.cs). Toggling has to account for both:
   *
   * - Unchecking an item that's only checked via the group key must EXPAND that coarse grant
   *   into individual item keys for every sibling except the one being unchecked — "select all"
   *   = whole group visible; unchecking one item must leave the rest visible, not the whole group.
   * - Checking an item that completes the group (every sibling now individually checked)
   *   COLLAPSES back into the single coarse `groupKey` — keeps `allowedTabs` tidy and matches
   *   how a "select all" template is actually stored (same shape a pre-TASK-398 template has).
   * - `group.groupKey === null` (the standalone Dashboard pseudo-group) skips both group-key
   *   branches entirely — Dashboard's single item is just added/removed directly, matching how
   *   its bare "dashboard" key already served both roles before TASK-398.
   */
  function toggleTabItem(group: TenantTabGroupDto, item: TenantTabDto) {
    setAllowedTabs((prev) => {
      const next = new Set(prev);
      const { groupKey } = group;
      const viaGroup = groupKey != null && next.has(groupKey);
      const currentlyChecked = next.has(item.key) || viaGroup;

      if (currentlyChecked) {
        if (viaGroup && groupKey != null) {
          next.delete(groupKey);
          for (const it of group.items) {
            if (it.key !== item.key) next.add(it.key);
          }
        } else {
          next.delete(item.key);
        }
      } else {
        next.add(item.key);
        if (groupKey != null && group.items.every((it) => next.has(it.key))) {
          for (const it of group.items) next.delete(it.key);
          next.add(groupKey);
        }
      }
      return next;
    });
  }

  /** "Select all" checkbox for a group header — grants/revokes every item in the group at
   *  once. Collapses to the single coarse `groupKey` when checking (same rationale as
   *  `toggleTabItem`'s collapse branch); clears both the group key and any partial item-level
   *  entries when unchecking. */
  function toggleTabGroupAll(group: TenantTabGroupDto) {
    setAllowedTabs((prev) => {
      const next = new Set(prev);
      const { groupKey } = group;
      const checkedCount = group.items.filter(
        (item) => next.has(item.key) || (groupKey != null && next.has(groupKey)),
      ).length;
      const allChecked = checkedCount === group.items.length;

      for (const it of group.items) next.delete(it.key);
      if (groupKey != null) next.delete(groupKey);

      if (!allChecked) {
        if (groupKey != null) next.add(groupKey);
        else for (const it of group.items) next.add(it.key);
      }
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
            {tabs && (
              <TabsTree
                groups={tabs}
                allowedTabs={allowedTabs}
                onToggleItem={toggleTabItem}
                onToggleGroupAll={toggleTabGroupAll}
              />
            )}
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

/* ─────────────────────────── Visible-tabs tree (TASK-399) ─────────────────────────── */

interface TabsTreeProps {
  groups: TenantTabGroupDto[];
  allowedTabs: Set<string>;
  onToggleItem: (group: TenantTabGroupDto, item: TenantTabDto) => void;
  onToggleGroupAll: (group: TenantTabGroupDto) => void;
}

/**
 * Two-level tab tree replacing the old flat 10-checkbox list (TASK-398 made the backend
 * catalog hierarchical; this wires the editor up to it). The standalone Dashboard section
 * (`groupKey: null`, always exactly one item) renders as a single bare checkbox with no
 * group header/"select all" affordance — mirrors Sidebar.tsx treating Dashboard as a
 * top-level NavItem rather than a NavGroup. Every other section is a collapsible group
 * (pattern mirrors Sidebar.tsx's own NavGroupSection) with a "select all" checkbox in its
 * header plus one checkbox per page underneath.
 */
function TabsTree({ groups, allowedTabs, onToggleItem, onToggleGroupAll }: TabsTreeProps) {
  const dashboardGroup = groups.find((g) => g.groupKey === null);
  const otherGroups = groups.filter((g) => g.groupKey !== null);

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 8 }}>
      {dashboardGroup && dashboardGroup.items.map((item) => (
        <label
          key={item.key}
          style={{
            display: "flex", alignItems: "center", gap: 8, cursor: "pointer",
            background: "#0D1117", border: "1px solid #1F2937",
            borderRadius: 8, padding: "10px 14px",
          }}
        >
          <input
            type="checkbox"
            checked={allowedTabs.has(item.key)}
            onChange={() => onToggleItem(dashboardGroup, item)}
            style={{ width: 14, height: 14, cursor: "pointer", accentColor: "#3B82F6" }}
          />
          <span style={{ color: allowedTabs.has(item.key) ? "#E8EDF5" : "#4B5563", fontSize: 13, fontWeight: 600 }}>
            {item.labelUa}
          </span>
        </label>
      ))}

      {otherGroups.map((group) => (
        <TabGroupSection
          key={group.groupKey}
          group={group}
          allowedTabs={allowedTabs}
          onToggleItem={onToggleItem}
          onToggleGroupAll={onToggleGroupAll}
        />
      ))}
    </div>
  );
}

interface TabGroupSectionProps {
  group: TenantTabGroupDto;
  allowedTabs: Set<string>;
  onToggleItem: (group: TenantTabGroupDto, item: TenantTabDto) => void;
  onToggleGroupAll: (group: TenantTabGroupDto) => void;
}

/**
 * One collapsible group section — header (expand toggle + group label + "select all") plus
 * its per-page item checkboxes. An item reads as checked when its own key is granted OR the
 * whole group is (backward-compat bulk grant, see `toggleTabItem`'s doc comment on the parent
 * modal for the full checked/collapse semantics). Defaults to expanded — unlike Sidebar.tsx's
 * nav (which auto-collapses inactive groups to save space), this is a config editor where an
 * admin benefits from seeing everything they're about to grant.
 */
function TabGroupSection({ group, allowedTabs, onToggleItem, onToggleGroupAll }: TabGroupSectionProps) {
  const t = useTranslations("Dashboard.tenantRoles.formModal");
  const [expanded, setExpanded] = useState(true);
  const selectAllRef = useRef<HTMLInputElement>(null);

  const { groupKey } = group;
  const checkedCount = group.items.filter(
    (item) => allowedTabs.has(item.key) || (groupKey != null && allowedTabs.has(groupKey)),
  ).length;
  const allChecked = group.items.length > 0 && checkedCount === group.items.length;
  const someChecked = checkedCount > 0 && !allChecked;

  // Indeterminate is a DOM property, not an HTML attribute — must be set imperatively.
  useEffect(() => {
    if (selectAllRef.current) selectAllRef.current.indeterminate = someChecked;
  }, [someChecked]);

  return (
    <div style={{ background: "#0D1117", border: "1px solid #1F2937", borderRadius: 8, overflow: "hidden" }}>
      <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", gap: 8, padding: "8px 14px" }}>
        <button
          type="button"
          onClick={() => setExpanded((v) => !v)}
          style={{
            display: "flex", alignItems: "center", gap: 6,
            background: "none", border: "none", padding: 0, cursor: "pointer",
            color: "#9CA3AF", fontSize: 11, fontWeight: 700,
            textTransform: "uppercase", letterSpacing: "0.05em",
          }}
        >
          {expanded ? <ChevronDown size={13} /> : <ChevronRight size={13} />}
          {group.groupLabelUa}
        </button>
        <label style={{ display: "flex", alignItems: "center", gap: 6, cursor: "pointer", color: "#6B7280", fontSize: 11 }}>
          <input
            ref={selectAllRef}
            type="checkbox"
            checked={allChecked}
            onChange={() => onToggleGroupAll(group)}
            style={{ width: 13, height: 13, cursor: "pointer", accentColor: "#3B82F6" }}
          />
          {t("selectAllGroup")}
        </label>
      </div>

      {expanded && (
        <div style={{ display: "flex", flexDirection: "column", gap: 7, padding: "2px 14px 12px 34px" }}>
          {group.items.map((item) => {
            const checked = allowedTabs.has(item.key) || (groupKey != null && allowedTabs.has(groupKey));
            return (
              <label key={item.key} style={{ display: "flex", alignItems: "center", gap: 8, cursor: "pointer" }}>
                <input
                  type="checkbox"
                  checked={checked}
                  onChange={() => onToggleItem(group, item)}
                  style={{ width: 14, height: 14, cursor: "pointer", accentColor: "#3B82F6" }}
                />
                <span style={{ color: checked ? "#E8EDF5" : "#4B5563", fontSize: 13 }}>
                  {item.labelUa}
                </span>
              </label>
            );
          })}
        </div>
      )}
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
