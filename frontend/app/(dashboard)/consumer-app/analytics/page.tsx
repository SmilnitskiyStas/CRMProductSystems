"use client";

import { useTranslations } from "next-intl";
import { AccessDenied } from "@/components/AccessDenied";
import { useMe } from "@/features/auth/hooks/useAuth";
import { ConsumerAppAnalyticsSection } from "@/features/consumer-app/components/ConsumerAppAnalyticsSection";
import { CAN_VIEW_ANALYTICS, hasRole } from "@/lib/roles";
import { useRequireTab } from "@/lib/useRequireTab";

export default function ConsumerAppAnalyticsPage() {
  const t = useTranslations("Dashboard.sidebar.groups.analytics");
  const { data: me } = useMe();
  const roleAccess = me ? hasRole(me.role, CAN_VIEW_ANALYTICS) : false;
  const effectiveAccess = useRequireTab("/consumer-app/analytics", "analytics", roleAccess);

  if (!me) return null;
  if (!effectiveAccess) return <AccessDenied title={t("consumerAppAnalytics")} />;

  return (
    <div style={{ padding: "28px 32px", width: "100%", boxSizing: "border-box", display: "flex", flexDirection: "column", gap: 20 }}>
      <header>
        <h1 style={{ color: "#E8EDF5", fontSize: 22, fontWeight: 700, margin: 0 }}>Аналітика застосунку</h1>
        <p style={{ color: "#64748B", fontSize: 13, margin: "6px 0 0" }}>
          Взаємодія клієнтів із каталогами, банерами та акціями без дублювання загальної аналітики продажів
        </p>
      </header>
      <ConsumerAppAnalyticsSection />
    </div>
  );
}
