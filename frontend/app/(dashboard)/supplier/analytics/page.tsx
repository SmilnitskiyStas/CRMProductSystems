"use client";

import { useTranslations } from "next-intl";
import { SupplierAnalyticsDashboard } from "@/features/supplier-cabinet/components/SupplierAnalyticsDashboard";
import { useMe } from "@/features/auth/hooks/useAuth";
import { SUPPLIER_ONLY, hasRole } from "@/lib/roles";

export default function SupplierAnalyticsPage() {
  const t = useTranslations("Dashboard.supplierCabinet.pages");
  const { data: me } = useMe();

  if (me && !hasRole(me.role, SUPPLIER_ONLY)) {
    return (
      <div style={{ padding: "28px 32px", color: "#F87171", fontSize: 14 }}>
        {t("supplierOnlyAccess")}
      </div>
    );
  }

  // null permissions = full/owner access; a restricted staff role without analytics_view
  // should not reach this page directly by URL either (the backend also action-gates it).
  if (me?.permissions && !me.permissions.analytics_view) {
    return (
      <div style={{ padding: "28px 32px", color: "#F87171", fontSize: 14 }}>
        {t("analytics.noAccess")}
      </div>
    );
  }

  return (
    <div style={{ padding: "28px 32px" }}>
      <div style={{ marginBottom: 24 }}>
        <h1 style={{ color: "#E8EDF5", fontSize: 22, fontWeight: 700, margin: 0 }}>
          {t("analytics.title")}
        </h1>
        <p style={{ color: "#4B5563", fontSize: 14, marginTop: 6 }}>
          {t("analytics.subtitle")}
        </p>
      </div>
      <SupplierAnalyticsDashboard />
    </div>
  );
}
