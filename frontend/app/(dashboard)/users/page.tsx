"use client";

import { useState } from "react";
import { useSearchParams } from "next/navigation";
import { useTranslations } from "next-intl";
import { UserPlus } from "lucide-react";
import { UsersList } from "@/features/users/components/UsersList";
import { InviteUserModal } from "@/features/users/components/InviteUserModal";
import { useMe } from "@/features/auth/hooks/useAuth";
import { useUsers } from "@/features/users/hooks/useUsers";
import { TenantRolesTab } from "@/features/tenant-roles/components/TenantRolesTab";
import { Btn } from "@/components/ui/Btn";
import { PageContainer } from "@/components/ui/PageContainer";
import { PageHeader } from "@/components/ui/PageHeader";
import { Tabs } from "@/components/ui/Tabs";
import { hasRole, AT_LEAST_ENTERPRISE_ADMIN, AT_LEAST_STORE_MANAGER } from "@/lib/roles";
import { useRequireTab } from "@/lib/useRequireTab";

type PageTab = "staff" | "role-templates";

export default function UsersPage() {
  const t = useTranslations("Dashboard.users.page");
  const { data: me } = useMe();
  const { data: users } = useUsers();
  const searchParams = useSearchParams();

  const [inviteOpen, setInviteOpen] = useState(false);
  // Deep-link support (e.g. Settings → "Керувати ролями" links to /users?tab=role-templates).
  const [tab, setTab] = useState<PageTab>(() =>
    searchParams.get("tab") === "role-templates" ? "role-templates" : "staff",
  );

  const isAdmin = me?.role === "enterprise_admin" || me?.role === "provider";
  // TenantRole templates (ADR-020) are AtLeastEnterpriseAdmin-only on the backend — mirror
  // that gate here so the tab (and its data) never even renders for lower roles.
  const canManageRoleTemplates = hasRole(me?.role, AT_LEAST_ENTERPRISE_ADMIN);

  // Sidebar tab visibility (TASK-391c; authoritative/exclusive as of TASK-397; per-item
  // granularity as of TASK-399): mirrors Sidebar.tsx's /users NavItem gate (roles:
  // AT_LEAST_STORE_MANAGER). A non-empty tabs claim is now authoritative in BOTH directions —
  // a TenantRole granted either "/users" (item-level) or "workforce" (whole-group, backward
  // compat) reaches this page even without the role, AND a TenantRole with a non-empty tabs
  // claim that EXCLUDES both is blocked even for an AT_LEAST_STORE_MANAGER user — same override
  // semantic as Sidebar's tabsSet, no longer a plain OR.
  useRequireTab("/users", "workforce", hasRole(me?.role, AT_LEAST_STORE_MANAGER));

  const activeCount   = users?.filter((u) => u.isActive).length  ?? 0;
  const totalCount    = users?.length ?? 0;
  const telegramCount = users?.filter((u) => u.hasTelegram).length ?? 0;
  // TASK-396: derived client-side from the already-loaded list, no separate endpoint —
  // surfaces the TASK-395 coverage-gap flag for a non-technical admin to act on before
  // Stage 3 RLS enforcement goes live.
  const needsLocationCount = users?.filter((u) => u.needsLocationAssignment).length ?? 0;

  return (
    <PageContainer>
      <PageHeader
        title={t("title")}
        subtitle={t("subtitle")}
        actions={
          tab === "staff" && isAdmin ? (
            <Btn icon={<UserPlus size={15} />} onClick={() => setInviteOpen(true)}>
              {t("inviteButton")}
            </Btn>
          ) : undefined
        }
      />

      {canManageRoleTemplates && (
        <Tabs<PageTab>
          value={tab}
          onChange={setTab}
          items={[
            { key: "staff", label: t("staffTab") },
            { key: "role-templates", label: t("roleTemplatesTab") },
          ]}
        />
      )}

      {tab === "role-templates" && canManageRoleTemplates ? (
        <TenantRolesTab />
      ) : (
        <>
          {/* Stats row */}
          <div style={{ display: "flex", gap: 12, marginBottom: 20 }}>
            {[
              { label: t("statTotal"),    value: totalCount,    color: "#60A5FA" },
              { label: t("statActive"),   value: activeCount,   color: "#4ADE80" },
              { label: t("statTelegram"), value: telegramCount, color: "#38BDF8" },
              { label: t("statNeedsLocation"), value: needsLocationCount, color: "#FBBF24" },
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
    </PageContainer>
  );
}
