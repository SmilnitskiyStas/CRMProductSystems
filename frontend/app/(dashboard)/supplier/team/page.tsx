"use client";

import { useState } from "react";
import { useTranslations } from "next-intl";
import { PageContainer } from "@/components/ui/PageContainer";
import { PageHeader } from "@/components/ui/PageHeader";
import { Tabs } from "@/components/ui/Tabs";
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
      <PageContainer>
        <div style={{ color: "#F87171", fontSize: 14 }}>{t("supplierOnlyAccess")}</div>
      </PageContainer>
    );
  }

  // null permissions = full/owner access; a restricted staff role without
  // staff_management should not reach this page directly by URL either.
  if (me?.permissions && !me.permissions.staff_management) {
    return (
      <PageContainer>
        <div style={{ color: "#F87171", fontSize: 14 }}>{t("team.noAccess")}</div>
      </PageContainer>
    );
  }

  return (
    <PageContainer>
      <PageHeader title={t("team.title")} subtitle={t("team.subtitle")} />
      <Tabs
        items={[
          { key: "team", label: t("team.tabTeam") },
          { key: "performance", label: t("team.tabPerformance") },
        ]}
        value={activeTab}
        onChange={setActiveTab}
      />

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
    </PageContainer>
  );
}
