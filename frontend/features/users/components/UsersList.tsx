"use client";

import { useState } from "react";
import { useTranslations, useLocale } from "next-intl";
import { useUsers } from "../hooks/useUsers";
import { UserDetailPanel } from "./UserDetailPanel";
import { getRoleLabel, ROLE_KEYS } from "@/features/profile/types";
import type { UserDto } from "../types";
import { TenantRoleBadge } from "@/features/tenant-roles/components/TenantRoleBadge";
import { Table, type TableColumn } from "@/components/ui/Table";

// ── Sub-components ────────────────────────────────────────────────────────────

function RoleBadge({ role }: { role: string }) {
  const tRoles = useTranslations("Dashboard.roles");
  const colors: Record<string, { bg: string; color: string; border: string }> = {
    enterprise_admin: { bg: "#1c1032", color: "#A78BFA", border: "#4C1D95" },
    network_manager:  { bg: "#0a1f2e", color: "#38BDF8", border: "#0C4A6E" },
    store_manager:    { bg: "#0d2318", color: "#4ADE80", border: "#14532D" },
    merchandiser:     { bg: "#1c1a0a", color: "#FCD34D", border: "#78350F" },
    storekeeper:      { bg: "#0a1628", color: "#60A5FA", border: "#1D4ED8" },
    cashier:          { bg: "#1a1220", color: "#C084FC", border: "#6B21A8" },
  };
  const style = colors[role] ?? { bg: "#111827", color: "#6B7280", border: "#374151" };
  return (
    <span
      style={{
        background: style.bg,
        border: `1px solid ${style.border}`,
        borderRadius: 20,
        padding: "2px 10px",
        color: style.color,
        fontSize: 11,
        fontWeight: 600,
        whiteSpace: "nowrap",
      }}
    >
      {getRoleLabel(tRoles, role)}
    </span>
  );
}

/**
 * Warning-toned pill flagging a store-scoped user with zero `user_locations` rows yet
 * (TASK-396). Not an error — amber, not red — this is a reminder for a manual backfill
 * ahead of Stage 3 RLS enforcement, not something currently broken.
 */
function NeedsLocationBadge() {
  const t = useTranslations("Dashboard.users.list");
  return (
    <span
      style={{
        background: "#2D2208",
        border: "1px solid #78350F",
        borderRadius: 20,
        padding: "2px 10px",
        color: "#FBBF24",
        fontSize: 11,
        fontWeight: 600,
        whiteSpace: "nowrap",
      }}
    >
      {t("needsLocationBadge")}
    </span>
  );
}

function StatusDot({ isActive }: { isActive: boolean }) {
  return (
    <span
      style={{
        display: "inline-block",
        width: 8, height: 8,
        borderRadius: "50%",
        background: isActive ? "#4ADE80" : "#374151",
        marginRight: 6,
        flexShrink: 0,
      }}
    />
  );
}

/** Shows count of custom permission overrides for a user */
function AccessBadge({ user }: { user: UserDto }) {
  const t = useTranslations("Dashboard.users.list");
  const perms = user.permissions;
  if (!perms || Object.keys(perms).length === 0) {
    return <span style={{ color: "#374151", fontSize: 12 }}>{t("byRole")}</span>;
  }
  const grants = Object.values(perms).filter(Boolean).length;
  const denies = Object.values(perms).filter((v) => !v).length;
  return (
    <div style={{ display: "inline-flex", gap: 4, flexWrap: "wrap" }}>
      {grants > 0 && (
        <span
          style={{
            background: "#052e16", border: "1px solid #14532D",
            borderRadius: 4, padding: "1px 6px",
            color: "#4ADE80", fontSize: 10, fontWeight: 600,
          }}
        >
          +{grants}
        </span>
      )}
      {denies > 0 && (
        <span
          style={{
            background: "#2d0a0a", border: "1px solid #7F1D1D",
            borderRadius: 4, padding: "1px 6px",
            color: "#F87171", fontSize: 10, fontWeight: 600,
          }}
        >
          −{denies}
        </span>
      )}
    </div>
  );
}

function formatDate(iso: string | null | undefined, locale: string) {
  if (!iso) return "—";
  return new Date(iso).toLocaleDateString(locale, {
    day: "2-digit", month: "2-digit", year: "numeric",
  });
}

function formatDatetime(iso: string | null | undefined, locale: string) {
  if (!iso) return "—";
  return new Date(iso).toLocaleString(locale, {
    day: "2-digit", month: "2-digit", year: "numeric",
    hour: "2-digit", minute: "2-digit",
  });
}

// ── Main component ────────────────────────────────────────────────────────────

export function UsersList() {
  const t = useTranslations("Dashboard.users.list");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";
  const tRoles = useTranslations("Dashboard.roles");
  const { data: users, isLoading, isError } = useUsers();
  // Holds only the id — the panel's `user` is derived live from `users` below, so an
  // already-open panel picks up cache patches (e.g. a TenantRole assignment) without
  // needing to be closed and reopened.
  const [selectedId,  setSelectedId] = useState<string | null>(null);
  const [search,      setSearch]      = useState("");
  const [roleFilter,  setRoleFilter]  = useState<string>("all");
  const [statusFilter, setStatusFilter] = useState<"all" | "active" | "inactive">("all");
  // TASK-396: client-side only — filters the already-loaded list, no separate API request.
  const [onlyMissingLocation, setOnlyMissingLocation] = useState(false);

  const filtered = (users ?? []).filter((u) => {
    const matchSearch =
      !search ||
      u.fullName.toLowerCase().includes(search.toLowerCase()) ||
      u.email.toLowerCase().includes(search.toLowerCase()) ||
      (u.phone ?? "").includes(search);
    const matchRole   = roleFilter === "all" || u.role === roleFilter;
    const matchStatus =
      statusFilter === "all" ||
      (statusFilter === "active"   && u.isActive) ||
      (statusFilter === "inactive" && !u.isActive);
    const matchLocation = !onlyMissingLocation || u.needsLocationAssignment;
    return matchSearch && matchRole && matchStatus && matchLocation;
  });

  const selected = selectedId ? (users ?? []).find((u) => u.id === selectedId) ?? null : null;

  if (isLoading) {
    return (
      <div style={{ padding: "40px 0", textAlign: "center", color: "#374151", fontSize: 13 }}>
        {t("loading")}
      </div>
    );
  }

  if (isError) {
    return (
      <div style={{ padding: "40px 0", textAlign: "center", color: "#F87171", fontSize: 13 }}>
        {t("loadError")}
      </div>
    );
  }

  // Columns: User | Role | Phone | Access | Last active | Added | Invited by | Status
  const columns: TableColumn<UserDto>[] = [
    {
      key: "user",
      header: t("headerUser"),
      render: (user) => {
        const initials = user.fullName
          .split(" ").slice(0, 2).map((n) => n[0]).join("").toUpperCase();
        return (
          <div style={{ display: "flex", alignItems: "center", gap: 10, minWidth: 0 }}>
            <div
              style={{
                width: 34, height: 34, borderRadius: "50%", flexShrink: 0,
                background: user.isActive
                  ? "linear-gradient(135deg, #3B82F6, #6366F1)"
                  : "#1F2937",
                display: "flex", alignItems: "center", justifyContent: "center",
                color: "#fff", fontSize: 12, fontWeight: 700,
              }}
            >
              {initials}
            </div>
            <div style={{ minWidth: 0 }}>
              <div style={{
                color: "#E8EDF5", fontSize: 13, fontWeight: 500,
                whiteSpace: "nowrap", overflow: "hidden", textOverflow: "ellipsis",
              }}>
                {user.fullName}
              </div>
              <div style={{
                color: "#4B5563", fontSize: 11,
                whiteSpace: "nowrap", overflow: "hidden", textOverflow: "ellipsis",
              }}>
                {user.email}
              </div>
            </div>
          </div>
        );
      },
    },
    {
      key: "role",
      header: t("headerRole"),
      render: (user) => (
        <div style={{ display: "inline-flex", flexDirection: "column", gap: 4, alignItems: "center" }}>
          <RoleBadge role={user.role} />
          <TenantRoleBadge tenantRoleId={user.tenantRoleId} />
          {user.needsLocationAssignment && <NeedsLocationBadge />}
        </div>
      ),
    },
    {
      key: "phone",
      header: t("headerPhone"),
      render: (user) => (
        <span style={{ color: user.phone ? "#9CA3AF" : "#374151", fontSize: 12 }}>
          {user.phone ?? "—"}
        </span>
      ),
    },
    {
      key: "access",
      header: t("headerAccess"),
      render: (user) => <AccessBadge user={user} />,
    },
    {
      key: "lastActive",
      header: t("headerLastActive"),
      render: (user) => (
        <span style={{ color: "#4B5563", fontSize: 12 }}>
          {user.lastActiveAt ? formatDatetime(user.lastActiveAt, intlLocale) : "—"}
        </span>
      ),
    },
    {
      key: "added",
      header: t("headerAdded"),
      render: (user) => (
        <span style={{ color: "#4B5563", fontSize: 12 }}>
          {formatDate(user.createdAt, intlLocale)}
        </span>
      ),
    },
    {
      key: "invitedBy",
      header: t("headerInvitedBy"),
      render: (user) => (
        <span style={{ color: user.invitedByName ? "#9CA3AF" : "#374151", fontSize: 12, whiteSpace: "nowrap" }}>
          {user.invitedByName ?? "—"}
        </span>
      ),
    },
    {
      key: "status",
      header: t("headerStatus"),
      render: (user) => (
        <div style={{ display: "inline-flex", alignItems: "center" }}>
          <StatusDot isActive={user.isActive} />
          <span style={{ color: user.isActive ? "#4ADE80" : "#6B7280", fontSize: 12 }}>
            {user.isActive ? t("statusActive") : t("statusInactive")}
          </span>
        </div>
      ),
    },
  ];

  return (
    <>
      {/* Filters bar */}
      <div style={{ display: "flex", gap: 10, marginBottom: 16, flexWrap: "wrap" }}>
        <input
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          placeholder={t("searchPlaceholder")}
          style={{
            flex: 1,
            minWidth: 220,
            background: "#0D1117",
            border: "1px solid #1F2937",
            borderRadius: 8,
            padding: "8px 12px",
            color: "#E8EDF5",
            fontSize: 13,
            outline: "none",
          }}
        />
        <select
          value={roleFilter}
          onChange={(e) => setRoleFilter(e.target.value)}
          style={{
            background: "#0D1117",
            border: "1px solid #1F2937",
            borderRadius: 8,
            padding: "8px 12px",
            color: "#E8EDF5",
            fontSize: 13,
            outline: "none",
            cursor: "pointer",
          }}
        >
          <option value="all" style={{ background: "#0D1117" }}>{t("allRoles")}</option>
          {ROLE_KEYS.map((key) => (
            <option key={key} value={key} style={{ background: "#0D1117" }}>{getRoleLabel(tRoles, key)}</option>
          ))}
        </select>
        <select
          value={statusFilter}
          onChange={(e) => setStatusFilter(e.target.value as typeof statusFilter)}
          style={{
            background: "#0D1117",
            border: "1px solid #1F2937",
            borderRadius: 8,
            padding: "8px 12px",
            color: "#E8EDF5",
            fontSize: 13,
            outline: "none",
            cursor: "pointer",
          }}
        >
          <option value="all"      style={{ background: "#0D1117" }}>{t("allStatuses")}</option>
          <option value="active"   style={{ background: "#0D1117" }}>{t("activeOption")}</option>
          <option value="inactive" style={{ background: "#0D1117" }}>{t("inactiveOption")}</option>
        </select>
        <label
          style={{
            display: "flex",
            alignItems: "center",
            gap: 8,
            background: "#0D1117",
            border: "1px solid #1F2937",
            borderRadius: 8,
            padding: "8px 12px",
            color: "#E8EDF5",
            fontSize: 13,
            cursor: "pointer",
            whiteSpace: "nowrap",
          }}
        >
          <input
            type="checkbox"
            checked={onlyMissingLocation}
            onChange={(e) => setOnlyMissingLocation(e.target.checked)}
          />
          {t("onlyMissingLocationLabel")}
        </label>
      </div>

      {/* Table */}
      <Table
        columns={columns}
        rows={filtered}
        rowKey={(user) => user.id}
        onRowClick={(user) => setSelectedId(user.id)}
        minWidth={900}
        emptyMessage={
          search || roleFilter !== "all" || statusFilter !== "all" || onlyMissingLocation
            ? t("notFound")
            : t("noUsersYet")
        }
      />

      {/* Footer count */}
      {filtered.length > 0 && (
        <div style={{ color: "#374151", fontSize: 12, marginTop: 10, textAlign: "right" }}>
          {t("footerCount", { shown: filtered.length, total: users?.length ?? 0 })}
        </div>
      )}

      {/* Detail panel */}
      {selected && (
        <UserDetailPanel
          user={selected}
          onClose={() => setSelectedId(null)}
        />
      )}
    </>
  );
}
