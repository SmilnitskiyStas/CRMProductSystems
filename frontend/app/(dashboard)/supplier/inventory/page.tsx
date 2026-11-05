"use client";

import { useState } from "react";
import { useTranslations } from "next-intl";
import { PageContainer } from "@/components/ui/PageContainer";
import { PageHeader } from "@/components/ui/PageHeader";
import { Tabs } from "@/components/ui/Tabs";
import { WarehouseStockTable } from "@/features/supplier-cabinet/components/WarehouseStockTable";
import { SupplierReceiptsList } from "@/features/supplier-cabinet/components/SupplierReceiptsList";
import { useMe } from "@/features/auth/hooks/useAuth";
import { SUPPLIER_ONLY, hasRole } from "@/lib/roles";

type ActiveTab = "stock" | "receipts";

export default function SupplierInventoryPage() {
  const t = useTranslations("Dashboard.supplierCabinet.pages");
  const { data: me } = useMe();
  const [activeTab, setActiveTab] = useState<ActiveTab>("stock");

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
        <div style={{ color: "#F87171", fontSize: 14 }}>{t("inventory.noAccess")}</div>
      </PageContainer>
    );
  }

  return (
    <PageContainer>
      <PageHeader title={t("inventory.title")} subtitle={t("inventory.subtitle")} />
      <Tabs
        items={[
          { key: "stock", label: t("inventory.tabStock") },
          { key: "receipts", label: t("inventory.tabReceipts") },
        ]}
        value={activeTab}
        onChange={setActiveTab}
      />

      {activeTab === "stock" && <WarehouseStockTable />}
      {activeTab === "receipts" && <SupplierReceiptsList />}
    </PageContainer>
  );
}
