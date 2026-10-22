"use client";

import { useTranslations } from "next-intl";
import { PageContainer } from "@/components/ui/PageContainer";
import { PageHeader } from "@/components/ui/PageHeader";
import { useMe } from "@/features/auth/hooks/useAuth";
import { AccessDenied } from "@/components/AccessDenied";
import { AT_LEAST_ENTERPRISE_ADMIN, hasRole } from "@/lib/roles";
import { ThemeEditorSection } from "@/features/consumer-app/components/ThemeEditorSection";

/**
 * TASK-537: replaces TASK-535's placeholder with the real Theme Editor — whitelisted
 * color/radius/spacing controls (TASK-536) with a live, unsaved-state preview. Same role gate
 * and page-shell shape as every sibling route here (mirrors `/consumer-app/page.tsx`).
 */
export default function ConsumerAppDesignPage() {
  const t = useTranslations("Dashboard.consumerApp.designPage");
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
      <ThemeEditorSection />
    </PageContainer>
  );
}
