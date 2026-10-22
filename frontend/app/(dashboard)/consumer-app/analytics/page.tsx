"use client";

import { useTranslations } from "next-intl";
import { AccessDenied } from "@/components/AccessDenied";
import { useMe } from "@/features/auth/hooks/useAuth";
import { ConsumerAppAnalyticsSection } from "@/features/consumer-app/components/ConsumerAppAnalyticsSection";
import { CAN_VIEW_ANALYTICS, hasRole } from "@/lib/roles";
import { useRequireTab } from "@/lib/useRequireTab";
import { PageContainer } from "@/components/ui/PageContainer";
import { PageHeader } from "@/components/ui/PageHeader";

export default function ConsumerAppAnalyticsPage() {
  const t = useTranslations("Dashboard.sidebar.groups.analytics");
  const { data: me } = useMe();
  const roleAccess = me ? hasRole(me.role, CAN_VIEW_ANALYTICS) : false;
  const effectiveAccess = useRequireTab("/consumer-app/analytics", "analytics", roleAccess);

  if (!me) return null;
  if (!effectiveAccess) return <AccessDenied title={t("consumerAppAnalytics")} />;

  return (
    <PageContainer>
      <PageHeader
        title="Аналітика застосунку"
        subtitle="Взаємодія клієнтів із каталогами, банерами та акціями без дублювання загальної аналітики продажів"
      />
      <ConsumerAppAnalyticsSection />
    </PageContainer>
  );
}
