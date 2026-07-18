"use client";

import { useTranslations } from "next-intl";
import { useTenantRoles } from "../hooks/useTenantRoles";
import { useMe } from "@/features/auth/hooks/useAuth";
import { hasRole, AT_LEAST_ENTERPRISE_ADMIN } from "@/lib/roles";

interface Props {
  tenantRoleId?: string | null;
}

/**
 * Small pill showing the assigned TenantRole template name, e.g. "HR".
 * Renders nothing when the user has no template assigned. Resolves the name via the
 * (cached, deduped by React Query) includeInactive=true list so a badge for a
 * since-archived template still shows a name instead of silently disappearing.
 *
 * GET /api/tenant-roles is AtLeastEnterpriseAdmin-only — this is rendered unconditionally
 * inside UsersList/UserDetailPanel for every viewer (store_manager included), so the lookup
 * query is disabled for non-enterprise_admin viewers to avoid a guaranteed 403 per row.
 */
export function TenantRoleBadge({ tenantRoleId }: Props) {
  const t = useTranslations("Dashboard.tenantRoles.badge");
  const { data: me } = useMe();
  const canResolveName = hasRole(me?.role, AT_LEAST_ENTERPRISE_ADMIN);
  const { data: roles } = useTenantRoles(true, canResolveName && Boolean(tenantRoleId));

  if (!tenantRoleId || !canResolveName) return null;
  const role = roles?.find((r) => r.id === tenantRoleId);
  if (!role) return null;

  return (
    <span
      title={!role.isActive ? t("archivedTitle") : undefined}
      style={{
        display: "inline-block",
        background: "#062b29",
        border: "1px solid #0F766E",
        borderRadius: 20,
        padding: "2px 10px",
        color: "#2DD4BF",
        fontSize: 11,
        fontWeight: 600,
        whiteSpace: "nowrap",
      }}
    >
      {role.name}
      {!role.isActive && t("archivedSuffix")}
    </span>
  );
}
