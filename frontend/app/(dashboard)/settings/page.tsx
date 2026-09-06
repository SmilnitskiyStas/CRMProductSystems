"use client";

import { useState, useEffect } from "react";
import { useSearchParams } from "next/navigation";
import { useTranslations } from "next-intl";
import { LayoutDashboard, Bell, Link as LinkIcon, Blocks, Store } from "lucide-react";
import { OverviewTab } from "@/features/settings/components/OverviewTab";
import { NotificationsTab } from "@/features/settings/components/NotificationsTab";
import { IntegrationsTab } from "@/features/settings/components/IntegrationsTab";
import { ModulesTab } from "@/features/settings/components/ModulesTab";
import { MarketplaceProfileTab } from "@/features/settings/components/MarketplaceProfileTab";
import { useMe } from "@/features/auth/hooks/useAuth";
import {
  hasRole,
  canViewIntegrations,
  PROVIDER_TEAM,
  ENTERPRISE_ADMIN_ONLY,
  SUPPLIER_ONLY,
} from "@/lib/roles";

type Tab = "overview" | "notifications" | "integrations" | "modules" | "marketplace-profile";

export default function SettingsPage() {
  const t = useTranslations("Dashboard.settings.page");
  const searchParams = useSearchParams();
  const { data: me } = useMe();

  // Tab-level role gating — mirrors the backend authorization for each tab's endpoints:
  //  · integrations → AppPolicies.IntegrationsViewOrCapability (store_manager+ OR capability)
  //  · modules      → GET /api/settings/modules is open to any tenant role, but the tab is
  //                   read-only info: show it to provider team + the tenant's own admin
  //  · marketplace-profile → supplier_admin only (ADR-016)
  const showIntegrations = canViewIntegrations(me?.role, me?.capabilities);
  const showModules = hasRole(me?.role, PROVIDER_TEAM) || hasRole(me?.role, ENTERPRISE_ADMIN_ONLY);
  const isSupplier = hasRole(me?.role, SUPPLIER_ONLY);

  const ALL_TABS: { id: Tab; label: string; icon: React.ReactNode }[] = [
    { id: "overview",            label: t("tabOverview"),            icon: <LayoutDashboard size={15} /> },
    { id: "notifications",       label: t("tabNotifications"),       icon: <Bell size={15} /> },
    { id: "integrations",        label: t("tabIntegrations"),        icon: <LinkIcon size={15} /> },
    { id: "modules",             label: t("tabModules"),             icon: <Blocks size={15} /> },
    { id: "marketplace-profile", label: t("tabMarketplaceProfile"),  icon: <Store size={15} /> },
  ];

  const TABS = ALL_TABS.filter((tab) => {
    if (tab.id === "integrations") return showIntegrations;
    if (tab.id === "modules") return showModules;
    if (tab.id === "marketplace-profile") return isSupplier;
    return true;
  });

  const [activeTab, setActiveTab] = useState<Tab>(() => {
    const tabParam = searchParams.get("tab") as Tab | null;
    return TABS.some((tb) => tb.id === tabParam) ? (tabParam as Tab) : "overview";
  });

  useEffect(() => {
    const tabParam = searchParams.get("tab") as Tab | null;
    if (tabParam && TABS.some((tb) => tb.id === tabParam)) setActiveTab(tabParam);
  }, [searchParams]); // eslint-disable-line react-hooks/exhaustive-deps

  return (
    <div style={{ padding: "28px 32px", maxWidth: 900 }}>
      {/* Header */}
      <div style={{ marginBottom: 28 }}>
        <h1 style={{ color: "#E8EDF5", fontSize: 22, fontWeight: 700, margin: 0 }}>
          {t("title")}
        </h1>
        <p style={{ color: "#4B5563", fontSize: 14, marginTop: 6 }}>
          {t("subtitle")}
        </p>
      </div>

      {/* Tabs */}
      <div
        style={{
          display: "flex",
          gap: 4,
          borderBottom: "1px solid #1F2937",
          marginBottom: 28,
        }}
      >
        {TABS.map((tab) => {
          const active = activeTab === tab.id;
          return (
            <button
              key={tab.id}
              onClick={() => setActiveTab(tab.id)}
              style={{
                display: "flex",
                alignItems: "center",
                gap: 6,
                padding: "10px 18px",
                background: "transparent",
                border: "none",
                borderBottom: active ? "2px solid #3B82F6" : "2px solid transparent",
                color: active ? "#3B82F6" : "#6B7280",
                fontSize: 13,
                fontWeight: active ? 600 : 400,
                cursor: "pointer",
                marginBottom: -1,
                transition: "color 0.15s",
              }}
            >
              {tab.icon}
              {tab.label}
            </button>
          );
        })}
      </div>

      {/* Content */}
      <div
        style={{
          background: "#0D1117",
          border: "1px solid #1F2937",
          borderRadius: 12,
          padding: 24,
        }}
      >
        {activeTab === "overview"                            && <OverviewTab />}
        {activeTab === "notifications"                       && <NotificationsTab />}
        {activeTab === "integrations" && showIntegrations    && <IntegrationsTab />}
        {activeTab === "modules" && showModules              && <ModulesTab />}
        {activeTab === "marketplace-profile" && isSupplier   && <MarketplaceProfileTab />}
      </div>
    </div>
  );
}
