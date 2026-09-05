"use client";

import { useState } from "react";
import { useTranslations } from "next-intl";
import { CabinetStaffPanel } from "@/features/supplier-cabinet/components/CabinetStaffPanel";
import { RolesTab } from "@/features/supplier-cabinet/components/RolesTab";
import { TeamPerformanceView } from "@/features/supplier-cabinet/components/TeamPerformanceView";
import { useMe } from "@/features/auth/hooks/useAuth";
import { SUPPLIER_ONLY, hasRole } from "@/lib/roles";

type ActiveTab = "team" | "performance";

export default function SupplierTeamPage() {
  const t = useTranslations("Dashboard.supplierCabinet.pages");
  const { data: me } = useMe();
  const [activeTab, setActiveTab] = useState<ActiveTab>("team");

  if (me && !hasRole(me.role, SUPPLIER_ONLY)) {
    return (
      <div style={{ padding: "28px 32px", color: "#F87171", fontSize: 14 }}>
        {t("supplierOnlyAccess")}
      </div>
    );
  }

  // null permissions = full/owner access; a restricted staff role without
  // staff_management should not reach this page directly by URL either.
  if (me?.permissions && !me.permissions.staff_management) {
    return (
      <div style={{ padding: "28px 32px", color: "#F87171", fontSize: 14 }}>
        {t("team.noAccess")}
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
          {t("team.title")}
        </h1>
        <p style={{ color: "#4B5563", fontSize: 14, marginTop: 6 }}>
          {t("team.subtitle")}
        </p>
      </div>

      <div style={{ borderBottom: "1px solid #1F2937", marginBottom: 24, display: "flex" }}>
        <button style={tabStyle("team")} onClick={() => setActiveTab("team")}>
          {t("team.tabTeam")}
        </button>
        <button style={tabStyle("performance")} onClick={() => setActiveTab("performance")}>
          {t("team.tabPerformance")}
        </button>
      </div>

      {activeTab === "team" ? (
        <div
          style={{
            display: "grid",
            gridTemplateColumns: "1fr",
            gap: 24,
          }}
          className="lg:grid-cols-2"
        >
          <CabinetStaffPanel />
          <RolesTab />
        </div>
      ) : (
        <TeamPerformanceView />
      )}
    </div>
  );
}
