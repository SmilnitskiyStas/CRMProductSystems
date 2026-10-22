"use client";

import { useTranslations } from "next-intl";
import { PageContainer } from "@/components/ui/PageContainer";
import { PageHeader } from "@/components/ui/PageHeader";
import { useMe } from "@/features/auth/hooks/useAuth";
import { AccessDenied } from "@/components/AccessDenied";
import { AT_LEAST_ENTERPRISE_ADMIN, hasRole } from "@/lib/roles";
import { VersionHistorySection } from "@/features/consumer-app/components/VersionHistorySection";

/**
 * TASK-546: replaces TASK-535's placeholder with the real Version History screen — the tenant's
 * publish/rollback history (TASK-545), a rollback action per historical version, and the first
 * in-app "Publish draft" trigger anywhere in Retailer Admin. Same role gate and page-shell shape
 * as every sibling route here (mirrors `/consumer-app/page.tsx`).
 */
export default function ConsumerAppVersionsPage() {
  const t = useTranslations("Dashboard.consumerApp.versionsPage");
  const { data: me } = useMe();
  const roleAccess = me ? hasRole(me.role, AT_LEAST_ENTERPRISE_ADMIN) : null;

  if (roleAccess === false) {
    return <AccessDenied title={t("title")} />;
  }
  if (roleAccess === null) {
    // Still waiting on useMe() — avoid a denied-then-granted flash.
    return null;
  }

  return (
    <PageContainer>
      <PageHeader title={t("title")} subtitle={t("subtitle")} />
      <VersionHistorySection />
    </PageContainer>
  );
}
