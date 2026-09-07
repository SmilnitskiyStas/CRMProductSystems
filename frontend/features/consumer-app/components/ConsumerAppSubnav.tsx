"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { useTranslations } from "next-intl";
import { Activity, Compass, Gift, LayoutTemplate, Megaphone, Package, Palette, Settings, Smartphone, ToggleLeft, History, TrendingUp } from "lucide-react";
import { useMe } from "@/features/auth/hooks/useAuth";
import { AT_LEAST_ENTERPRISE_ADMIN, CAN_VIEW_ANALYTICS, hasRole } from "@/lib/roles";

interface AppTab {
  href: string;
  label: string;
  icon: React.ReactNode;
  exact?: boolean;
}

interface AppTabGroup {
  key: string;
  label: string;
  icon: React.ReactNode;
  tabs: AppTab[];
}

function matchesPath(pathname: string, tab: AppTab): boolean {
  return tab.exact ? pathname === tab.href : pathname === tab.href || pathname.startsWith(`${tab.href}/`);
}

/**
 * Page-level navigation for the consumer-app workspace. Its underline treatment
 * intentionally mirrors the marketing-analytics tabs instead of sidebar groups.
 */
export function ConsumerAppSubnav() {
  const pathname = usePathname();
  const { data: me } = useMe();
  const t = useTranslations("Dashboard.sidebar.groups.consumerApp");
  const tAnalytics = useTranslations("Dashboard.sidebar.groups.analytics");
  const canManage = me ? hasRole(me.role, AT_LEAST_ENTERPRISE_ADMIN) : false;
  const canViewAnalytics = me ? hasRole(me.role, CAN_VIEW_ANALYTICS) : false;

  const groups: AppTabGroup[] = [
    {
      key: "content",
      label: t("sections.content"),
      icon: <Megaphone size={16} />,
      tabs: [
        { href: "/consumer-app/messages", label: t("customerMessages"), icon: <Megaphone size={15} /> },
        { href: "/consumer-app/banners", label: t("banners"), icon: <Megaphone size={15} /> },
        { href: "/consumer-app/promotions", label: t("promotions"), icon: <TrendingUp size={15} /> },
        { href: "/consumer-app/catalog", label: t("catalog"), icon: <Package size={15} /> },
      ],
    },
    {
      key: "configuration",
      label: t("sections.configuration"),
      icon: <Settings size={16} />,
      tabs: [
        { href: "/consumer-app/design", label: t("design"), icon: <Palette size={15} /> },
        { href: "/consumer-app/pages", label: t("pages"), icon: <LayoutTemplate size={15} /> },
        { href: "/consumer-app/navigation", label: t("navigation"), icon: <Compass size={15} /> },
        { href: "/consumer-app/features", label: t("features"), icon: <ToggleLeft size={15} /> },
        { href: "/consumer-app/versions", label: t("versions"), icon: <History size={15} /> },
      ],
    },
    {
      key: "loyalty",
      label: t("sections.loyalty"),
      icon: <Gift size={16} />,
      tabs: [{ href: "/consumer-app", label: t("bonusProgram"), icon: <Smartphone size={15} />, exact: true }],
    },
    {
      key: "analytics",
      label: t("sections.analytics"),
      icon: <Activity size={16} />,
      tabs: [{ href: "/consumer-app/analytics", label: tAnalytics("consumerAppAnalytics"), icon: <Activity size={15} /> }],
    },
  ];

  const visibleGroups = groups.filter((group) => group.key === "analytics" ? canViewAnalytics : canManage);
  const activeGroup = visibleGroups.find((group) => group.tabs.some((tab) => matchesPath(pathname, tab))) ?? visibleGroups[0];

  if (!activeGroup) return null;

  return (
    <nav aria-label="Навігація керування застосунком" style={{ padding: "20px 32px 0" }}>
      <div style={{ display: "flex", flexWrap: "wrap", gap: 4, borderBottom: "1px solid #1F2937" }}>
        {visibleGroups.map((group) => {
          const active = group.key === activeGroup.key;
          return (
            <Link
              key={group.key}
              href={group.tabs[0].href}
              style={{ display: "inline-flex", alignItems: "center", gap: 7, padding: "10px 18px", marginBottom: -1, borderBottom: active ? "2px solid #3B82F6" : "2px solid transparent", color: active ? "#3B82F6" : "#6B7280", fontSize: 13, fontWeight: active ? 600 : 400, textDecoration: "none", transition: "color 0.15s" }}
            >
              {group.icon}
              {group.label}
            </Link>
          );
        })}
      </div>

      {activeGroup.tabs.length > 1 && (
        <div style={{ display: "flex", flexWrap: "wrap", gap: 4, marginTop: 8, borderBottom: "1px solid #1F2937" }}>
          {activeGroup.tabs.map((tab) => {
            const active = matchesPath(pathname, tab);
            return (
              <Link
                key={tab.href}
                href={tab.href}
                style={{ display: "inline-flex", alignItems: "center", gap: 7, padding: "8px 14px", marginBottom: -1, borderBottom: active ? "2px solid #3B82F6" : "2px solid transparent", color: active ? "#3B82F6" : "#6B7280", fontSize: 12, fontWeight: active ? 700 : 500, textDecoration: "none", transition: "color 0.15s" }}
              >
                {tab.icon}
                {tab.label}
              </Link>
            );
          })}
        </div>
      )}
    </nav>
  );
}
