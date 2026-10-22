"use client";

import { useState, useEffect } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import { useTranslations } from "next-intl";
import { LayoutDashboard, Bell, Link as LinkIcon, Blocks, Store } from "lucide-react";
import { PageContainer } from "@/components/ui/PageContainer";
import { PageHeader } from "@/components/ui/PageHeader";
import { Tabs } from "@/components/ui/Tabs";
import { OverviewTab } from "@/features/settings/components/OverviewTab";
import { NotificationsTab } from "@/features/settings/components/NotificationsTab";
import { IntegrationsTab } from "@/features/settings/components/IntegrationsTab";
import { ModulesTab } from "@/features/settings/components/ModulesTab";
import { MarketplaceProfileTab } from "@/features/settings/components/MarketplaceProfileTab";
import { useMe } from "@/features/auth/hooks/useAuth";
import { hasRole, canViewIntegrations, canAccessTenantSettings, PROVIDER_TEAM, SUPPLIER_ONLY } from "@/lib/roles";

type Tab = "overview" | "notifications" | "integrations" | "modules" | "marketplace-profile";

export default function SettingsPage() {
  const t = useTranslations("Dashboard.settings.page");
  const searchParams = useSearchParams();
  const router = useRouter();
  const { data: me } = useMe();

  // Business tenants do not have a Settings page (the platform team configures it for them).
  const allowed = canAccessTenantSettings(me?.role);
  useEffect(() => {
    if (me && !allowed) router.replace("/dashboard");
  }, [me, allowed, router]);

  // Tab-level role gating — mirrors the backend authorization for each tab's endpoints:
  //  · integrations → AppPolicies.IntegrationsViewOrCapability (store_manager+ OR capability)
  //  · modules      → PROVIDER TEAM ONLY. Module activation is a paid, provider-granted
  //                   decision (per-module pricing) — a tenant must never see a modules
  //                   surface that looks like a self-serve on/off list. The Overview tab
  //                   still shows the tenant their *active* modules read-only.
  //  · marketplace-profile → supplier_admin only (ADR-016)
  const showIntegrations = canViewIntegrations(me?.role, me?.capabilities);
  const showModules = hasRole(me?.role, PROVIDER_TEAM);
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

  if (!me || !allowed) return null;

  return (
    <PageContainer style={{ maxWidth: 900 }}>
      <PageHeader title={t("title")} subtitle={t("subtitle")} />

      <Tabs<Tab>
        items={TABS.map((tab) => ({
          key: tab.id,
          label: (
            <>
              {tab.icon}
              {tab.label}
            </>
          ),
        }))}
        value={activeTab}
        onChange={setActiveTab}
      />

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
    </PageContainer>
  );
}
