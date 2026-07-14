"use client";

import { useState } from "react";
import { useUpdateUser, useDeactivateUser } from "../hooks/useUsers";
import { UserActivityLog } from "./UserActivityLog";
import { UserPermissionsEditor } from "./UserPermissionsEditor";
import { ROLE_LABELS } from "@/features/profile/types";
import { ROLE_RANK } from "../types";
import { useMe } from "@/features/auth/hooks/useAuth";
import { useLegalEntities } from "@/features/legal-entities/hooks/useLegalEntities";
import type { UserDto, UpdateUserRequest } from "../types";
import { Btn } from "@/components/ui/Btn";
import { hasRole, AT_LEAST_ENTERPRISE_ADMIN } from "@/lib/roles";
import { TenantRoleBadge } from "@/features/tenant-roles/components/TenantRoleBadge";
import { TenantRoleSelector } from "@/features/tenant-roles/components/TenantRoleSelector";

const EDITABLE_ROLES = [
  "store_manager",
  "merchandiser",
  "storekeeper",
  "cashier",
];

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
  color: "#6B7280",
  fontSize: 11,
  fontWeight: 500,
  marginBottom: 5,
  textTransform: "uppercase",
  letterSpacing: "0.05em",
};

type PanelTab = "info" | "access" | "activity";

interface Props {
  user: UserDto;
  onClose: () => void;
}

export function UserDetailPanel({ user, onClose }: Props) {
  const { data: me } = useMe();
  const update     = useUpdateUser(user.id);
  const deactivate = useDeactivateUser();
  const { data: legalEntities } = useLegalEntities();
  const activeLegalEntities = (legalEntities ?? []).filter((e) => e.isActive);

  const [tab, setTab] = useState<PanelTab>("info");

  // Edit form state
  const [editing,  setEditing]  = useState(false);
  const [fullName, setFullName] = useState(user.fullName);
  const [phone,    setPhone]    = useState(user.phone ?? "");
  const [role,     setRole]     = useState(user.role);
  const [legalEntityId, setLegalEntityId] = useState(user.legalEntityId ?? "");
  const [saved,    setSaved]    = useState(false);

  const isSelf      = me?.id === user.id;
  const isAdmin     = me?.role === "enterprise_admin" || me?.role === "provider";
  const canEdit     = isAdmin && !isSelf;
  const canDeactivate = isAdmin && !isSelf && user.isActive;

  // Permissions tab: visible if editor outranks target
  const editorRank  = ROLE_RANK[me?.role ?? ""] ?? 0;
  const targetRank  = ROLE_RANK[user.role] ?? 0;
  const canSeeAccessTab = !isSelf && editorRank > targetRank;

  // TenantRole template assignment (ADR-020): enterprise_admin+ only, independent of the
  // rank check above — backend's POST /api/users/:id/tenant-role has no rank comparison
  // (AtLeastEnterpriseAdmin-only), so an admin can assign a template to a peer admin too.
  const canManageTenantRole = hasRole(me?.role, AT_LEAST_ENTERPRISE_ADMIN) && !isSelf;
  const showAccessTab = canSeeAccessTab || canManageTenantRole;

  const initials = user.fullName
    .split(" ").slice(0, 2).map((n) => n[0]).join("").toUpperCase();

  async function handleSave() {
    const data: UpdateUserRequest = {
      fullName: fullName.trim() || user.fullName,
      phone: phone.trim() || null,
      role,
      storeId: user.storeId,
      legalEntityId: legalEntityId || null,
    };
    await update.mutateAsync(data);
    setSaved(true);
    setEditing(false);
    setTimeout(() => setSaved(false), 3000);
  }

  async function handleDeactivate() {
    if (!confirm(`Деактивувати ${user.fullName}? Вони не зможуть увійти в систему.`)) return;
    await deactivate.mutateAsync(user.id);
    onClose();
  }

  const tabBtnStyle = (active: boolean): React.CSSProperties => ({
    padding: "8px 16px",
    background: "transparent",
    border: "none",
    borderBottom: active ? "2px solid #3B82F6" : "2px solid transparent",
    color: active ? "#3B82F6" : "#4B5563",
    fontSize: 13,
    fontWeight: active ? 600 : 400,
    cursor: "pointer",
    marginBottom: -1,
  });

  return (
    <>
      {/* Backdrop */}
      <div
        onClick={onClose}
        style={{
          position: "fixed", inset: 0,
          background: "rgba(0,0,0,0.5)",
          zIndex: 200,
          backdropFilter: "blur(2px)",
        }}
      />

      {/* Panel */}
      <div
        style={{
          position: "fixed",
          top: 0, right: 0, bottom: 0,
          width: "min(480px, 100vw)",
          background: "#0D1117",
          borderLeft: "1px solid #1F2937",
          zIndex: 201,
          display: "flex",
          flexDirection: "column",
          overflowY: "auto",
        }}
      >
        {/* Header */}
        <div
          style={{
            display: "flex",
            alignItems: "center",
            gap: 14,
            padding: "20px 24px",
            borderBottom: "1px solid #1F2937",
            flexShrink: 0,
          }}
        >
          {/* Avatar */}
          <div
            style={{
              width: 44, height: 44, borderRadius: "50%",
              background: user.isActive
                ? "linear-gradient(135deg, #3B82F6, #6366F1)"
                : "#1F2937",
              display: "flex", alignItems: "center", justifyContent: "center",
              color: "#fff", fontSize: 16, fontWeight: 700, flexShrink: 0,
            }}
          >
            {initials}
          </div>
          <div style={{ flex: 1, minWidth: 0 }}>
            <div style={{ color: "#E8EDF5", fontSize: 15, fontWeight: 700 }}>
              {user.fullName}
            </div>
            <div style={{ display: "flex", alignItems: "center", gap: 8, marginTop: 4 }}>
              <span
                style={{
                  background: "#1A2235", border: "1px solid #1F2937",
                  borderRadius: 20, padding: "2px 10px",
                  color: "#3B82F6", fontSize: 11, fontWeight: 600,
                }}
              >
                {ROLE_LABELS[user.role] ?? user.role}
              </span>
              <TenantRoleBadge tenantRoleId={user.tenantRoleId} />
              {!user.isActive && (
                <span
                  style={{
                    background: "#2d0a0a", border: "1px solid #7f1d1d",
                    borderRadius: 20, padding: "2px 8px",
                    color: "#F87171", fontSize: 11,
                  }}
                >
                  Деактивовано
                </span>
              )}
            </div>
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

        {/* Tabs */}
        <div
          style={{
            display: "flex",
            borderBottom: "1px solid #1F2937",
            padding: "0 24px",
            flexShrink: 0,
          }}
        >
          <button style={tabBtnStyle(tab === "info")}     onClick={() => setTab("info")}>Інформація</button>
          {showAccessTab && (
            <button style={tabBtnStyle(tab === "access")} onClick={() => setTab("access")}>Доступ</button>
          )}
          <button style={tabBtnStyle(tab === "activity")} onClick={() => setTab("activity")}>Активність</button>
        </div>

        {/* Content */}
        <div style={{ padding: 24, flex: 1 }}>
          {tab === "info" && (
            <div>
              {/* Read-only fields */}
              <div style={{ marginBottom: 20 }}>
                <div style={{ padding: "10px 0", borderBottom: "1px solid #1A2235" }}>
                  <span style={labelStyle}>Email</span>
                  <span style={{ color: "#E8EDF5", fontSize: 13 }}>{user.email}</span>
                </div>
                <div style={{ padding: "10px 0", borderBottom: "1px solid #1A2235" }}>
                  <span style={labelStyle}>Telegram</span>
                  <span style={{ color: user.hasTelegram ? "#4ADE80" : "#374151", fontSize: 13 }}>
                    {user.hasTelegram ? "✓ Підключено" : "Не підключено"}
                  </span>
                </div>
                <div style={{ padding: "10px 0" }}>
                  <span style={labelStyle}>Зареєстровано</span>
                  <span style={{ color: "#6B7280", fontSize: 13 }}>
                    {new Date(user.createdAt).toLocaleDateString("uk-UA")}
                  </span>
                </div>
              </div>

              {/* Edit form — only for admins, not self */}
              {canEdit && (
                editing ? (
                  <div>
                    <div style={{ display: "flex", flexDirection: "column", gap: 14, marginBottom: 16 }}>
                      <div>
                        <label style={labelStyle}>Повне ім'я</label>
                        <input
                          value={fullName}
                          onChange={(e) => setFullName(e.target.value)}
                          style={inputStyle}
                        />
                      </div>
                      <div>
                        <label style={labelStyle}>Телефон</label>
                        <input
                          value={phone}
                          onChange={(e) => setPhone(e.target.value)}
                          placeholder="+380 99 123 45 67"
                          type="tel"
                          style={inputStyle}
                        />
                      </div>
                      <div>
                        <label style={labelStyle}>Роль</label>
                        <select
                          value={role}
                          onChange={(e) => setRole(e.target.value)}
                          style={{ ...inputStyle, appearance: "none", cursor: "pointer" }}
                        >
                          {EDITABLE_ROLES.map((r) => (
                            <option key={r} value={r} style={{ background: "#0D1117" }}>
                              {ROLE_LABELS[r] ?? r}
                            </option>
                          ))}
                        </select>
                      </div>
                      <div>
                        <label style={labelStyle}>Юридична особа</label>
                        <select
                          value={legalEntityId}
                          onChange={(e) => setLegalEntityId(e.target.value)}
                          style={{ ...inputStyle, appearance: "none", cursor: "pointer" }}
                        >
                          <option value="" style={{ background: "#0D1117" }}>— Не вказано —</option>
                          {activeLegalEntities.map((entity) => (
                            <option key={entity.id} value={entity.id} style={{ background: "#0D1117" }}>
                              {entity.legalName}
                            </option>
                          ))}
                        </select>
                      </div>
                    </div>
                    <div style={{ display: "flex", gap: 10 }}>
                      <Btn onClick={handleSave} disabled={update.isPending} style={{ flex: 1, justifyContent: "center" }}>
                        {update.isPending ? "Збереження…" : "Зберегти"}
                      </Btn>
                      <Btn variant="ghost" onClick={() => setEditing(false)}>
                        Скасувати
                      </Btn>
                    </div>
                  </div>
                ) : (
                  <div style={{ display: "flex", flexDirection: "column", gap: 10 }}>
                    {saved && (
                      <div style={{ color: "#4ADE80", fontSize: 13 }}>✓ Збережено</div>
                    )}
                    <button
                      onClick={() => setEditing(true)}
                      style={{
                        width: "100%", padding: "9px 0", borderRadius: 8,
                        background: "transparent", border: "1px solid #374151",
                        color: "#6B7280", fontSize: 13, cursor: "pointer",
                      }}
                    >
                      Редагувати
                    </button>
                    {canDeactivate && (
                      <button
                        onClick={handleDeactivate}
                        disabled={deactivate.isPending}
                        style={{
                          width: "100%", padding: "9px 0", borderRadius: 8,
                          background: "transparent", border: "1px solid #7f1d1d",
                          color: "#F87171", fontSize: 13, cursor: "pointer",
                        }}
                      >
                        {deactivate.isPending ? "Деактивація…" : "Деактивувати"}
                      </button>
                    )}
                  </div>
                )
              )}
            </div>
          )}

          {tab === "access" && (
            <div>
              {canManageTenantRole && <TenantRoleSelector user={user} />}
              {canSeeAccessTab && <UserPermissionsEditor user={user} editorRole={me?.role ?? ""} />}
            </div>
          )}

          {tab === "activity" && (
            <UserActivityLog userId={user.id} />
          )}
        </div>
      </div>
    </>
  );
}
