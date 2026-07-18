"use client";

import { useState, useEffect } from "react";
import { useSearchParams } from "next/navigation";
import { useTranslations } from "next-intl";
import { Settings, Bell, Link as LinkIcon, Blocks, Store } from "lucide-react";
import { NotificationsTab } from "@/features/settings/components/NotificationsTab";
import { IntegrationsTab } from "@/features/settings/components/IntegrationsTab";
import { ModulesTab } from "@/features/settings/components/ModulesTab";
import { MarketplaceProfileTab } from "@/features/settings/components/MarketplaceProfileTab";
import { useMe } from "@/features/auth/hooks/useAuth";
import { hasRole, PROVIDER_TEAM, SUPPLIER_ONLY } from "@/lib/roles";

type Tab = "general" | "notifications" | "integrations" | "modules" | "marketplace-profile";

export default function SettingsPage() {
  const t = useTranslations("Dashboard.settings.page");
  const searchParams = useSearchParams();
  const { data: me } = useMe();
  const canViewModules = hasRole(me?.role, PROVIDER_TEAM);
  const isSupplier = hasRole(me?.role, SUPPLIER_ONLY);

  const ALL_TABS: { id: Tab; label: string; icon: React.ReactNode }[] = [
    { id: "general",             label: t("tabGeneral"),             icon: <Settings size={15} /> },
    { id: "notifications",       label: t("tabNotifications"),       icon: <Bell size={15} /> },
    { id: "integrations",        label: t("tabIntegrations"),        icon: <LinkIcon size={15} /> },
    { id: "modules",             label: t("tabModules"),             icon: <Blocks size={15} /> },
    { id: "marketplace-profile", label: t("tabMarketplaceProfile"),  icon: <Store size={15} /> },
  ];

  const TABS = ALL_TABS.filter((tab) => {
    if (tab.id === "modules") return canViewModules;
    if (tab.id === "marketplace-profile") return isSupplier;
    return true;
  });

  const [activeTab, setActiveTab] = useState<Tab>(() => {
    const tabParam = searchParams.get("tab") as Tab | null;
    return TABS.some((tb) => tb.id === tabParam) ? (tabParam as Tab) : "general";
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
        {activeTab === "general"             && <GeneralTab />}
        {activeTab === "notifications"       && <NotificationsTab />}
        {activeTab === "integrations"        && <IntegrationsTab />}
        {activeTab === "modules" && canViewModules && <ModulesTab />}
        {activeTab === "marketplace-profile" && isSupplier && <MarketplaceProfileTab />}
      </div>
    </div>
  );
}

function GeneralTab() {
  const t = useTranslations("Dashboard.settings.generalTab");

  const infoRowStyle: React.CSSProperties = {
    display: "flex",
    alignItems: "flex-start",
    gap: 14,
    padding: "14px 0",
    borderBottom: "1px solid #1A2235",
  };

  const items = [
    {
      icon: "🔔",
      title: t("notificationsTitle"),
      desc: t("notificationsDesc"),
      tab: "notifications",
    },
    {
      icon: "🔗",
      title: t("integrationsTitle"),
      desc: t("integrationsDesc"),
      tab: "integrations",
    },
  ];

  return (
    <div>
      <p style={{ color: "#4B5563", fontSize: 13, margin: "0 0 20px", lineHeight: 1.6 }}>
        {t("description")}
      </p>

      <div>
        {items.map((item) => (
          <div key={item.tab} style={infoRowStyle}>
            <span style={{ fontSize: 22, lineHeight: 1, marginTop: 1 }}>{item.icon}</span>
            <div style={{ flex: 1 }}>
              <div style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 600, marginBottom: 3 }}>
                {item.title}
              </div>
              <div style={{ color: "#4B5563", fontSize: 12, lineHeight: 1.5 }}>
                {item.desc}
              </div>
            </div>
            <button
              onClick={() => {
                const url = new URL(window.location.href);
                url.searchParams.set("tab", item.tab);
                window.history.pushState({}, "", url.toString());
                window.dispatchEvent(new Event("popstate"));
              }}
              style={{
                padding: "6px 14px",
                background: "transparent",
                border: "1px solid #1F2937",
                borderRadius: 7,
                color: "#6B7280",
                fontSize: 12,
                cursor: "pointer",
                whiteSpace: "nowrap",
                flexShrink: 0,
              }}
              onMouseEnter={(e) => {
                (e.currentTarget as HTMLElement).style.borderColor = "#3B82F6";
                (e.currentTarget as HTMLElement).style.color = "#3B82F6";
              }}
              onMouseLeave={(e) => {
                (e.currentTarget as HTMLElement).style.borderColor = "#1F2937";
                (e.currentTarget as HTMLElement).style.color = "#6B7280";
              }}
            >
              {t("openButton")}
            </button>
          </div>
        ))}
      </div>

      {/* User settings hint */}
      <div
        style={{
          marginTop: 24,
          padding: "14px 16px",
          background: "#0A1020",
          border: "1px solid #1F2937",
          borderRadius: 9,
          display: "flex",
          alignItems: "center",
          gap: 12,
        }}
      >
        <span style={{ fontSize: 18 }}>👤</span>
        <div style={{ flex: 1 }}>
          <div style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 500 }}>
            {t("personalSettingsTitle")}
          </div>
          <div style={{ color: "#4B5563", fontSize: 12, marginTop: 2 }}>
            {t("personalSettingsDesc")}
          </div>
        </div>
        <a
          href="/settings-user"
          style={{
            padding: "6px 14px",
            background: "#1D3461",
            border: "1px solid #3B82F6",
            borderRadius: 7,
            color: "#93C5FD",
            fontSize: 12,
            fontWeight: 600,
            textDecoration: "none",
            whiteSpace: "nowrap",
            flexShrink: 0,
          }}
        >
          {t("myProfileLink")}
        </a>
      </div>
    </div>
  );
}
