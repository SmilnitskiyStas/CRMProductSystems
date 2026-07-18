"use client";

import { useState } from "react";
import type { CSSProperties } from "react";
import { useTranslations } from "next-intl";
import { UserPlus } from "lucide-react";
import { UsersList } from "@/features/users/components/UsersList";
import { InviteUserModal } from "@/features/users/components/InviteUserModal";
import { useMe } from "@/features/auth/hooks/useAuth";
import { useUsers } from "@/features/users/hooks/useUsers";
import { TenantRolesTab } from "@/features/tenant-roles/components/TenantRolesTab";
import { Btn } from "@/components/ui/Btn";
import { hasRole, AT_LEAST_ENTERPRISE_ADMIN } from "@/lib/roles";

type PageTab = "staff" | "role-templates";

export default function UsersPage() {
  const t = useTranslations("Dashboard.users.page");
  const { data: me } = useMe();
  const { data: users } = useUsers();

  const [inviteOpen, setInviteOpen] = useState(false);
  const [tab, setTab] = useState<PageTab>("staff");

  const isAdmin = me?.role === "enterprise_admin" || me?.role === "provider";
  // TenantRole templates (ADR-020) are AtLeastEnterpriseAdmin-only on the backend — mirror
  // that gate here so the tab (and its data) never even renders for lower roles.
  const canManageRoleTemplates = hasRole(me?.role, AT_LEAST_ENTERPRISE_ADMIN);

  const activeCount   = users?.filter((u) => u.isActive).length  ?? 0;
  const totalCount    = users?.length ?? 0;
  const telegramCount = users?.filter((u) => u.hasTelegram).length ?? 0;

  const tabBtnStyle = (active: boolean): CSSProperties => ({
    padding: "8px 2px",
    background: "transparent",
    border: "none",
    borderBottom: active ? "2px solid #3B82F6" : "2px solid transparent",
    color: active ? "#3B82F6" : "#4B5563",
    fontSize: 14,
    fontWeight: active ? 600 : 500,
    cursor: "pointer",
    marginBottom: -1,
  });

  return (
    <div style={{ padding: "28px 32px" }}>
      {/* Header */}
      <div
        style={{
          display: "flex",
          alignItems: "flex-start",
          justifyContent: "space-between",
          marginBottom: 20,
        }}
      >
        <div>
          <h1 style={{ color: "#E8EDF5", fontSize: 22, fontWeight: 700, margin: 0 }}>
            {t("title")}
          </h1>
          <p style={{ color: "#4B5563", fontSize: 14, marginTop: 6 }}>
            {t("subtitle")}
          </p>
        </div>

        {tab === "staff" && isAdmin && (
          <Btn icon={<UserPlus size={15} />} onClick={() => setInviteOpen(true)}>
            {t("inviteButton")}
          </Btn>
        )}
      </div>

      {/* Tabs — "Шаблони ролей" (role templates) is enterprise_admin+ only (ADR-020) */}
      {canManageRoleTemplates && (
        <div style={{ display: "flex", gap: 22, borderBottom: "1px solid #1F2937", marginBottom: 24 }}>
          <button style={tabBtnStyle(tab === "staff")} onClick={() => setTab("staff")}>
            {t("staffTab")}
          </button>
          <button style={tabBtnStyle(tab === "role-templates")} onClick={() => setTab("role-templates")}>
            {t("roleTemplatesTab")}
          </button>
        </div>
      )}

      {tab === "role-templates" && canManageRoleTemplates ? (
        <TenantRolesTab />
      ) : (
        <>
          {/* Stats row */}
          <div style={{ display: "flex", gap: 12, marginBottom: 24 }}>
            {[
              { label: t("statTotal"),    value: totalCount,    color: "#60A5FA" },
              { label: t("statActive"),   value: activeCount,   color: "#4ADE80" },
              { label: t("statTelegram"), value: telegramCount, color: "#38BDF8" },
            ].map((stat) => (
              <div
                key={stat.label}
                style={{
                  flex: 1,
                  background: "#0D1117",
                  border: "1px solid #1F2937",
                  borderRadius: 10,
                  padding: "14px 18px",
                }}
              >
                <div style={{ color: stat.color, fontSize: 22, fontWeight: 700 }}>
                  {stat.value}
                </div>
                <div style={{ color: "#4B5563", fontSize: 12, marginTop: 2 }}>
                  {stat.label}
                </div>
              </div>
            ))}
          </div>

          {/* Users table */}
          <UsersList />
        </>
      )}

      {/* Invite modal */}
      {inviteOpen && <InviteUserModal onClose={() => setInviteOpen(false)} />}
    </div>
  );
}
