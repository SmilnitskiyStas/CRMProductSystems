"use client";

import { useTranslations } from "next-intl";
import { PageContainer } from "@/components/ui/PageContainer";
import { PageHeader } from "@/components/ui/PageHeader";
import { SupplierPerformanceView } from "@/features/supplier-cabinet/components/SupplierPerformanceView";
import { useMe } from "@/features/auth/hooks/useAuth";
import { SUPPLIER_ONLY, hasRole } from "@/lib/roles";

export default function SupplierPerformancePage() {
  const t = useTranslations("Dashboard.supplierCabinet.pages");
  const { data: me } = useMe();

  if (me && !hasRole(me.role, SUPPLIER_ONLY)) {
    return (
      <PageContainer>
        <div style={{ color: "#F87171", fontSize: 14 }}>{t("supplierOnlyAccess")}</div>
      </PageContainer>
    );
  }

  // null permissions = full/owner access; a restricted staff role without client_reviews
  // should not reach this page directly by URL either (the backend also action-gates it).
  if (me?.permissions && !me.permissions.client_reviews) {
    return (
      <PageContainer>
        <div style={{ color: "#F87171", fontSize: 14 }}>{t("performance.noAccess")}</div>
      </PageContainer>
    );
  }

  return (
    <PageContainer>
      <PageHeader title={t("performance.title")} subtitle={t("performance.subtitle")} />
      <SupplierPerformanceView />
    </PageContainer>
  );
}
