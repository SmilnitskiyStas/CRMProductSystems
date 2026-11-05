"use client";

import { useTranslations } from "next-intl";
import { PageContainer } from "@/components/ui/PageContainer";
import { PageHeader } from "@/components/ui/PageHeader";
import { useMe } from "@/features/auth/hooks/useAuth";
import { AccessDenied } from "@/components/AccessDenied";
import { AT_LEAST_ENTERPRISE_ADMIN, hasRole } from "@/lib/roles";
import { NavigationBuilderSection } from "@/features/consumer-app/components/NavigationBuilderSection";

/**
 * TASK-542: replaces TASK-535's placeholder with the real Navigation Builder — min 2/max 5
 * whitelisted nav items (type/icon), drag-to-reorder, backed by the same whole-document draft
 * AppBuilderCanvas.tsx (TASK-539/541) and ThemeEditorSection.tsx (TASK-537) already read/write.
 * Same role gate and page-shell shape as every sibling route here (mirrors
 * `/consumer-app/page.tsx`).
 */
export default function ConsumerAppNavigationPage() {
  const t = useTranslations("Dashboard.consumerApp.navigationPage");
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
      <NavigationBuilderSection />
    </PageContainer>
  );
}
