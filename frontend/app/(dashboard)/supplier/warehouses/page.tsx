"use client";

import { useTranslations } from "next-intl";
import { PageContainer } from "@/components/ui/PageContainer";
import { PageHeader } from "@/components/ui/PageHeader";
import { WarehousesTab } from "@/features/supplier-cabinet/components/WarehousesTab";
import { useMe } from "@/features/auth/hooks/useAuth";
import { SUPPLIER_ONLY, hasRole } from "@/lib/roles";

export default function SupplierWarehousesPage() {
  const t = useTranslations("Dashboard.supplierCabinet.pages");
  const { data: me } = useMe();

  if (me && !hasRole(me.role, SUPPLIER_ONLY)) {
    return (
      <PageContainer>
        <div style={{ color: "#F87171", fontSize: 14 }}>{t("supplierOnlyAccess")}</div>
      </PageContainer>
    );
  }

  // null permissions = full/owner access; a restricted staff role without
  // warehouse_management should not reach this page directly by URL either.
  if (me?.permissions && !me.permissions.warehouse_management) {
    return (
      <PageContainer>
        <div style={{ color: "#F87171", fontSize: 14 }}>{t("warehouses.noAccess")}</div>
      </PageContainer>
    );
  }

  return (
    <PageContainer>
      <PageHeader title={t("warehouses.title")} subtitle={t("warehouses.subtitle")} />
      <WarehousesTab />
    </PageContainer>
  );
}
