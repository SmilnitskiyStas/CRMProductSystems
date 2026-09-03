"use client";

import { useState } from "react";
import { useTranslations } from "next-intl";
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
      <div style={{ padding: "28px 32px", color: "#F87171", fontSize: 14 }}>
        {t("supplierOnlyAccess")}
      </div>
    );
  }

  // null permissions = full/owner access; a restricted staff role without
  // warehouse_management should not reach this page directly by URL either.
  if (me?.permissions && !me.permissions.warehouse_management) {
    return (
      <div style={{ padding: "28px 32px", color: "#F87171", fontSize: 14 }}>
        {t("inventory.noAccess")}
      </div>
    );
  }

  const tabStyle = (tab: ActiveTab): React.CSSProperties => ({
    padding: "10px 20px",
    background: "transparent",
    border: "none",
    borderBottom: activeTab === tab ? "2px solid #3B82F6" : "2px solid transparent",
    color: activeTab === tab ? "#3B82F6" : "#6B7280",
    fontSize: 13,
    fontWeight: activeTab === tab ? 600 : 400,
    cursor: "pointer",
    marginBottom: -1,
    transition: "color 0.15s",
  });

  return (
    <div style={{ padding: "28px 32px" }}>
      <div style={{ marginBottom: 24 }}>
        <h1 style={{ color: "#E8EDF5", fontSize: 22, fontWeight: 700, margin: 0 }}>
          {t("inventory.title")}
        </h1>
        <p style={{ color: "#4B5563", fontSize: 14, marginTop: 6 }}>
          {t("inventory.subtitle")}
        </p>
      </div>

      <div style={{ borderBottom: "1px solid #1F2937", marginBottom: 24, display: "flex" }}>
        <button style={tabStyle("stock")} onClick={() => setActiveTab("stock")}>
          {t("inventory.tabStock")}
        </button>
        <button style={tabStyle("receipts")} onClick={() => setActiveTab("receipts")}>
          {t("inventory.tabReceipts")}
        </button>
      </div>

      {activeTab === "stock" && <WarehouseStockTable />}
      {activeTab === "receipts" && <SupplierReceiptsList />}
    </div>
  );
}
