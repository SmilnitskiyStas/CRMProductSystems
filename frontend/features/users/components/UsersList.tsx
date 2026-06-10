"use client";

import { useState } from "react";
import { useUsers } from "../hooks/useUsers";
import { UserDetailPanel } from "./UserDetailPanel";
import { ROLE_LABELS } from "@/features/profile/types";
import { PAGES } from "../types";
import type { UserDto } from "../types";

// ── Sub-components ────────────────────────────────────────────────────────────

function RoleBadge({ role }: { role: string }) {
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
      {ROLE_LABELS[role] ?? role}
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
  const perms = user.permissions;
  if (!perms || Object.keys(perms).length === 0) {
    return <span style={{ color: "#374151", fontSize: 12 }}>За роллю</span>;
  }
  const grants = Object.values(perms).filter(Boolean).length;
  const denies = Object.values(perms).filter((v) => !v).length;
  return (
    <div style={{ display: "flex", gap: 4, flexWrap: "wrap" }}>
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

function formatDate(iso?: string | null) {
  if (!iso) return "—";
  return new Date(iso).toLocaleDateString("uk-UA", {
    day: "2-digit", month: "2-digit", year: "numeric",
  });
}

function formatDatetime(iso?: string | null) {
  if (!iso) return "—";
  return new Date(iso).toLocaleString("uk-UA", {
    day: "2-digit", month: "2-digit", year: "numeric",
    hour: "2-digit", minute: "2-digit",
  });
}

// ── Grid template ─────────────────────────────────────────────────────────────
// Columns: User | Role | Phone | Access | Last active | Added | Invited by | Status
const GRID = "minmax(200px,1fr) 150px 130px 100px 140px 120px 140px 110px";

const HEADERS = [
  "Користувач",
  "Роль",
  "Телефон",
  "Доступи",
  "Остання активність",
  "Додано",
  "Хто додав",
  "Статус",
];

// ── Main component ────────────────────────────────────────────────────────────

export function UsersList() {
  const { data: users, isLoading, isError } = useUsers();
  const [selected,    setSelected]    = useState<UserDto | null>(null);
  const [search,      setSearch]      = useState("");
  const [roleFilter,  setRoleFilter]  = useState<string>("all");
  const [statusFilter, setStatusFilter] = useState<"all" | "active" | "inactive">("all");

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
    return matchSearch && matchRole && matchStatus;
  });

  if (isLoading) {
    return (
      <div style={{ padding: "40px 0", textAlign: "center", color: "#374151", fontSize: 13 }}>
        Завантаження…
      </div>
    );
  }

  if (isError) {
    return (
      <div style={{ padding: "40px 0", textAlign: "center", color: "#F87171", fontSize: 13 }}>
        Помилка завантаження користувачів
      </div>
    );
  }

  return (
    <>
      {/* Filters bar */}
      <div style={{ display: "flex", gap: 10, marginBottom: 16, flexWrap: "wrap" }}>
        <input
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          placeholder="Пошук за іменем, email або телефоном…"
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
          <option value="all" style={{ background: "#0D1117" }}>Всі ролі</option>
          {Object.entries(ROLE_LABELS).map(([key, label]) => (
            <option key={key} value={key} style={{ background: "#0D1117" }}>{label}</option>
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
          <option value="all"      style={{ background: "#0D1117" }}>Всі статуси</option>
          <option value="active"   style={{ background: "#0D1117" }}>Активні</option>
          <option value="inactive" style={{ background: "#0D1117" }}>Деактивовані</option>
        </select>
      </div>

      {/* Table */}
      <div
        style={{
          background: "#0D1117",
          border: "1px solid #1F2937",
          borderRadius: 12,
          overflow: "auto",          // horizontal scroll on narrow screens
        }}
      >
        {/* Header */}
        <div
          style={{
            display: "grid",
            gridTemplateColumns: GRID,
            padding: "10px 16px",
            borderBottom: "1px solid #1F2937",
            background: "#0A1020",
            minWidth: 900,
          }}
        >
          {HEADERS.map((h) => (
            <div
              key={h}
              style={{
                color: "#4B5563",
                fontSize: 11,
                fontWeight: 600,
                textTransform: "uppercase",
                letterSpacing: "0.05em",
              }}
            >
              {h}
            </div>
          ))}
        </div>

        {/* Rows */}
        {filtered.length === 0 ? (
          <div style={{ padding: "32px 16px", textAlign: "center", color: "#374151", fontSize: 13 }}>
            {search || roleFilter !== "all" || statusFilter !== "all"
              ? "Нічого не знайдено"
              : "Користувачів ще немає"}
          </div>
        ) : (
          filtered.map((user) => {
            const initials = user.fullName
              .split(" ").slice(0, 2).map((n) => n[0]).join("").toUpperCase();

            return (
              <div
                key={user.id}
                onClick={() => setSelected(user)}
                style={{
                  display: "grid",
                  gridTemplateColumns: GRID,
                  padding: "12px 16px",
                  borderBottom: "1px solid #0F1924",
                  cursor: "pointer",
                  alignItems: "center",
                  minWidth: 900,
                  transition: "background 0.1s",
                }}
                onMouseEnter={(e) => { (e.currentTarget as HTMLElement).style.background = "#0A1628"; }}
                onMouseLeave={(e) => { (e.currentTarget as HTMLElement).style.background = "transparent"; }}
              >
                {/* 1. Name + email */}
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

                {/* 2. Role */}
                <div><RoleBadge role={user.role} /></div>

                {/* 3. Phone */}
                <div style={{ color: user.phone ? "#9CA3AF" : "#374151", fontSize: 12 }}>
                  {user.phone ?? "—"}
                </div>

                {/* 4. Access overrides */}
                <div><AccessBadge user={user} /></div>

                {/* 5. Last active */}
                <div style={{ color: "#4B5563", fontSize: 12 }}>
                  {user.lastActiveAt ? formatDatetime(user.lastActiveAt) : "—"}
                </div>

                {/* 6. Added (createdAt) */}
                <div style={{ color: "#4B5563", fontSize: 12 }}>
                  {formatDate(user.createdAt)}
                </div>

                {/* 7. Invited by */}
                <div style={{ color: user.invitedByName ? "#9CA3AF" : "#374151", fontSize: 12, whiteSpace: "nowrap", overflow: "hidden", textOverflow: "ellipsis" }}>
                  {user.invitedByName ?? "—"}
                </div>

                {/* 8. Status */}
                <div style={{ display: "flex", alignItems: "center" }}>
                  <StatusDot isActive={user.isActive} />
                  <span style={{ color: user.isActive ? "#4ADE80" : "#6B7280", fontSize: 12 }}>
                    {user.isActive ? "Активний" : "Деактив."}
                  </span>
                </div>
              </div>
            );
          })
        )}
      </div>

      {/* Footer count */}
      {filtered.length > 0 && (
        <div style={{ color: "#374151", fontSize: 12, marginTop: 10, textAlign: "right" }}>
          {filtered.length} з {users?.length ?? 0} користувачів
        </div>
      )}

      {/* Detail panel */}
      {selected && (
        <UserDetailPanel
          user={selected}
          onClose={() => setSelected(null)}
        />
      )}
    </>
  );
}
