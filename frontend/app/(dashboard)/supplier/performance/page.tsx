"use client";

import { useTranslations } from "next-intl";
import { SupplierPerformanceView } from "@/features/supplier-cabinet/components/SupplierPerformanceView";
import { useMe } from "@/features/auth/hooks/useAuth";
import { SUPPLIER_ONLY, hasRole } from "@/lib/roles";

export default function SupplierPerformancePage() {
  const t = useTranslations("Dashboard.supplierCabinet.pages");
  const { data: me } = useMe();

  if (me && !hasRole(me.role, SUPPLIER_ONLY)) {
    return (
      <div style={{ padding: "28px 32px", color: "#F87171", fontSize: 14 }}>
        {t("supplierOnlyAccess")}
      </div>
    );
  }

  // null permissions = full/owner access; a restricted staff role without client_reviews
  // should not reach this page directly by URL either (the backend also action-gates it).
  if (me?.permissions && !me.permissions.client_reviews) {
    return (
      <div style={{ padding: "28px 32px", color: "#F87171", fontSize: 14 }}>
        {t("performance.noAccess")}
      </div>
    );
  }

  return (
    <div style={{ padding: "28px 32px" }}>
      <div style={{ marginBottom: 24 }}>
        <h1 style={{ color: "#E8EDF5", fontSize: 22, fontWeight: 700, margin: 0 }}>
          {t("performance.title")}
        </h1>
        <p style={{ color: "#4B5563", fontSize: 14, marginTop: 6 }}>
          {t("performance.subtitle")}
        </p>
      </div>
      <SupplierPerformanceView />
    </div>
  );
}
