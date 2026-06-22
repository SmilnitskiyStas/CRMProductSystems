"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import {
  LayoutDashboard,
  Package,
  ShoppingCart,
  ClipboardList,
  ArrowLeftRight,
  CalendarDays,
  Trash2,
  TrendingUp,
  BarChart2,
  BarChart3,
  Users,
  Settings,
  Shield,
  Sparkles,
  Map,
  Cpu,
  CreditCard,
  PanelLeftClose,
  PanelLeftOpen,
  ChevronDown,
  ChevronRight,
  Calculator,
  Truck,
  Store,
  Wrench,
  Car,
  BookOpen,
  FlaskConical,
  ListOrdered,
  Calendar,
  LifeBuoy,
} from "lucide-react";
import { useState, useEffect } from "react";
import { useMe } from "@/features/auth/hooks/useAuth";
import { useModules } from "@/features/modules/hooks/useModules";
import type { ModuleKey } from "@/features/modules/types";
import {
  AT_LEAST_STORE_MANAGER,
  CAN_ACCESS_POS,
  CAN_MANAGE_WAREHOUSE,
  CAN_VIEW_ANALYTICS,
  CAN_VIEW_WAREHOUSE,
  PROVIDER_ONLY,
  PROVIDER_TEAM,
  TENANT_ROLES,
  type AppRole,
} from "@/lib/roles";
import {
  SYSTEM_ROLE_PERMISSIONS,
  resolvePermissions,
} from "@/lib/providerPermissions";

// ── Types ──────────────────────────────────────────────────────────────────────

interface NavItem {
  href: string;
  label: string;
  icon: React.ReactNode;
  roles?: Set<AppRole>;
  /** Permission key required for PROVIDER_TEAM users (from providerPermissions.ts) */
  permission?: string;
  exact?: boolean;
}

interface NavGroup {
  key: string;
  label: string;
  icon: React.ReactNode;
  /** When set, the whole group is hidden unless this module is active for the tenant. */
  moduleKey?: ModuleKey;
  items: NavItem[];
}

// ── Navigation definition (v4 — ADR-014/015 module-gated groups) ───────────────
//
// Marketplace and Service Desk groups from the v4-spec menu structure are NOT
// included here — there are no pages for them yet (Phase 3 Supplier Marketplace;
// Service Desk only exists today as the "Підтримка" tab inside Settings). Add them
// once their pages exist; don't render empty/dead-link groups.

const NAV_GROUPS: NavGroup[] = [
  {
    key: "operations",
    label: "Операції",
    icon: <Package size={18} />,
    moduleKey: "inventory",
    items: [
      { href: "/inventory",  label: "Каталог",      icon: <Package size={16} />,      roles: CAN_VIEW_WAREHOUSE },
      { href: "/stock",      label: "Залишки",      icon: <ShoppingCart size={16} />, roles: CAN_VIEW_WAREHOUSE },
      { href: "/receipts",   label: "Прийомка",     icon: <ClipboardList size={16} />, roles: CAN_MANAGE_WAREHOUSE },
      { href: "/transfers",  label: "Переміщення",  icon: <ArrowLeftRight size={16} />, roles: CAN_MANAGE_WAREHOUSE },
      { href: "/write-offs", label: "Списання",     icon: <Trash2 size={16} />,       roles: CAN_VIEW_WAREHOUSE },
      { href: "/locations",  label: "Локації",      icon: <Map size={16} />,          roles: AT_LEAST_STORE_MANAGER },
      { href: "/iot",        label: "IoT пристрої", icon: <Cpu size={16} />,          roles: AT_LEAST_STORE_MANAGER },
    ],
  },
  {
    key: "sales",
    label: "Продажі",
    icon: <TrendingUp size={18} />,
    moduleKey: "pos",
    items: [
      { href: "/pos",       label: "Каса",     icon: <CreditCard size={16} />,   roles: CAN_ACCESS_POS },
      { href: "/sales",     label: "Продажі",  icon: <TrendingUp size={16} />,   roles: AT_LEAST_STORE_MANAGER },
      { href: "/customers", label: "Клієнти",  icon: <Users size={16} />,        roles: AT_LEAST_STORE_MANAGER },
      { href: "/events",    label: "Події",    icon: <CalendarDays size={16} />,  roles: AT_LEAST_STORE_MANAGER },
    ],
  },
  {
    key: "procurement",
    label: "Постачання",
    icon: <Truck size={18} />,
    moduleKey: "procurement",
    items: [
      { href: "/orders",    label: "Замовлення постачання", icon: <Calculator size={16} />, roles: AT_LEAST_STORE_MANAGER },
      { href: "/ai-orders", label: "AI Постачання",          icon: <Sparkles size={16} />,   roles: AT_LEAST_STORE_MANAGER },
    ],
  },
  {
    key: "marketplace",
    label: "Маркетплейс",
    icon: <Store size={18} />,
    moduleKey: "marketplace",
    items: [
      { href: "/marketplace", label: "Постачальники", icon: <Store size={16} />, exact: true, permission: "marketplace" },
    ],
  },
  {
    key: "auto_service",
    label: "Auto Service",
    icon: <Wrench size={18} />,
    moduleKey: "auto_service",
    items: [
      { href: "/auto-service",                  label: "Наряди",          icon: <Wrench size={16} />,   roles: AT_LEAST_STORE_MANAGER, exact: true },
      { href: "/auto-service/customers",         label: "Клієнти",         icon: <Car size={16} />,      roles: AT_LEAST_STORE_MANAGER },
      { href: "/auto-service/service-catalog",   label: "Каталог послуг",  icon: <BookOpen size={16} />, roles: AT_LEAST_STORE_MANAGER },
    ],
  },
  {
    key: "production",
    label: "Виробництво",
    icon: <FlaskConical size={18} />,
    moduleKey: "production",
    items: [
      { href: "/production/recipes", label: "Рецепти",  icon: <FlaskConical size={16} />, roles: AT_LEAST_STORE_MANAGER },
      { href: "/production/orders",  label: "Ордери",   icon: <ListOrdered size={16} />,  roles: AT_LEAST_STORE_MANAGER },
    ],
  },
  {
    key: "analytics",
    label: "Аналітика",
    icon: <BarChart2 size={18} />,
    items: [
      { href: "/analytics",     label: "Аналітика",    icon: <BarChart2 size={16} />, roles: CAN_VIEW_ANALYTICS, exact: true, permission: "analytics" },
      { href: "/analytics/pos", label: "POS Аналітика", icon: <BarChart3 size={16} />, roles: CAN_VIEW_ANALYTICS, permission: "analytics" },
    ],
  },
  {
    key: "workforce",
    label: "Персонал",
    icon: <Users size={18} />,
    items: [
      { href: "/users",          label: "Персонал", icon: <Users size={16} />,    roles: AT_LEAST_STORE_MANAGER },
      { href: "/schedules",      label: "Розклад",  icon: <Calendar size={16} />, permission: "schedule_management" },
      { href: "/provider/team",  label: "Команда",  icon: <Users size={16} />,    roles: PROVIDER_TEAM, permission: "team_management" },
    ],
  },
  {
    key: "support",
    label: "Підтримка",
    icon: <LifeBuoy size={18} />,
    items: [
      { href: "/service-desk", label: "Service Desk", icon: <LifeBuoy size={16} />, permission: "service_desk" },
    ],
  },
  {
    key: "admin",
    label: "Адмін",
    icon: <Shield size={18} />,
    items: [
      { href: "/provider", label: "Провайдер", icon: <Shield size={16} />,   roles: PROVIDER_ONLY, exact: true, permission: "admin_panel" },
      { href: "/admin",    label: "Адмін",     icon: <Settings size={16} />, roles: PROVIDER_ONLY, permission: "admin_panel" },
    ],
  },
];

/**
 * True when the group should be visible given the tenant's active modules.
 * `modulesSet === null` means either "not loaded yet" or "bypass gating" (provider /
 * enterprise_admin) — default to visible in both cases to avoid flash-hide-then-show.
 * Groups with no `moduleKey` are never module-gated.
 */
function isModuleActive(moduleKey: ModuleKey | undefined, modulesSet: Set<string> | null): boolean {
  if (!moduleKey) return true;
  if (modulesSet === null) return true;
  return modulesSet.has(moduleKey);
}

// ── Helpers ────────────────────────────────────────────────────────────────────

function isActive(pathname: string, href: string, exact?: boolean): boolean {
  if (exact) return pathname === href;
  return pathname === href || pathname.startsWith(href + "/");
}

// ── Sub-components ─────────────────────────────────────────────────────────────

interface NavLinkProps {
  item: NavItem;
  pathname: string;
  collapsed: boolean;
  indented?: boolean;
}

function NavLink({ item, pathname, collapsed, indented }: NavLinkProps) {
  const active = isActive(pathname, item.href, item.exact);

  return (
    <Link
      href={item.href}
      title={collapsed ? item.label : undefined}
      style={{
        display: "flex",
        alignItems: "center",
        justifyContent: collapsed ? "center" : "flex-start",
        gap: collapsed ? 0 : 8,
        padding: collapsed ? "8px 0" : indented ? "7px 12px 7px 32px" : "8px 12px",
        borderRadius: 8,
        background: active ? "#1D3461" : "transparent",
        color: active ? "#93C5FD" : "#6B7280",
        fontSize: 13,
        fontWeight: active ? 600 : 400,
        textDecoration: "none",
        transition: "background 0.1s, color 0.1s",
      }}
      onMouseEnter={(e) => {
        if (!active) {
          (e.currentTarget as HTMLElement).style.background = "#111827";
          (e.currentTarget as HTMLElement).style.color = "#9CA3AF";
        }
      }}
      onMouseLeave={(e) => {
        if (!active) {
          (e.currentTarget as HTMLElement).style.background = "transparent";
          (e.currentTarget as HTMLElement).style.color = "#6B7280";
        }
      }}
    >
      <span style={{ opacity: active ? 1 : 0.7, flexShrink: 0 }}>{item.icon}</span>
      {!collapsed && item.label}
    </Link>
  );
}

interface NavGroupSectionProps {
  group: NavGroup;
  visibleItems: NavItem[];
  pathname: string;
  collapsed: boolean;
}

function NavGroupSection({ group, visibleItems, pathname, collapsed }: NavGroupSectionProps) {
  const hasActive = visibleItems.some((item) => isActive(pathname, item.href, item.exact));
  const [expanded, setExpanded] = useState(hasActive);

  // Auto-expand when navigating to a child route
  useEffect(() => {
    if (hasActive) setExpanded(true);
  }, [hasActive]);

  // In collapsed mode — show only group icon with tooltip (no children)
  if (collapsed) {
    const groupActive = hasActive;
    return (
      <div
        title={group.label}
        style={{
          display: "flex",
          alignItems: "center",
          justifyContent: "center",
          padding: "9px 0",
          borderRadius: 8,
          color: groupActive ? "#93C5FD" : "#6B7280",
          opacity: groupActive ? 1 : 0.7,
          cursor: "default",
        }}
      >
        {group.icon}
      </div>
    );
  }

  return (
    <div>
      {/* Group header — toggle button */}
      <button
        onClick={() => setExpanded((v) => !v)}
        style={{
          width: "100%",
          display: "flex",
          alignItems: "center",
          justifyContent: "space-between",
          gap: 8,
          padding: "8px 12px",
          borderRadius: 8,
          background: "transparent",
          border: "none",
          color: hasActive ? "#CBD5E1" : "#4B5563",
          fontSize: 12,
          fontWeight: 600,
          letterSpacing: "0.04em",
          textTransform: "uppercase",
          cursor: "pointer",
          transition: "background 0.1s, color 0.1s",
        }}
        onMouseEnter={(e) => {
          (e.currentTarget as HTMLElement).style.background = "#111827";
          (e.currentTarget as HTMLElement).style.color = "#9CA3AF";
        }}
        onMouseLeave={(e) => {
          (e.currentTarget as HTMLElement).style.background = "transparent";
          (e.currentTarget as HTMLElement).style.color = hasActive ? "#CBD5E1" : "#4B5563";
        }}
      >
        <span style={{ display: "flex", alignItems: "center", gap: 8 }}>
          <span style={{ opacity: 0.8 }}>{group.icon}</span>
          {group.label}
        </span>
        {expanded
          ? <ChevronDown size={14} style={{ opacity: 0.6, flexShrink: 0 }} />
          : <ChevronRight size={14} style={{ opacity: 0.6, flexShrink: 0 }} />
        }
      </button>

      {/* Children */}
      {expanded && (
        <div style={{ display: "flex", flexDirection: "column", gap: 1, marginTop: 1 }}>
          {visibleItems.map((item) => (
            <NavLink key={item.href + group.key} item={item} pathname={pathname} collapsed={false} indented />
          ))}
        </div>
      )}
    </div>
  );
}

// ── Main Sidebar ───────────────────────────────────────────────────────────────

interface Props {
  collapsed: boolean;
  onToggle: () => void;
}

export function Sidebar({ collapsed, onToggle }: Props) {
  const pathname = usePathname();
  const { data: me } = useMe();
  const userRole = (me?.role ?? "") as AppRole;

  // Resolve effective permissions for PROVIDER_TEAM users
  const isProviderTeamMember = PROVIDER_TEAM.has(userRole);
  const effectivePermissions = isProviderTeamMember
    ? new Set(resolvePermissions(SYSTEM_ROLE_PERMISSIONS[userRole] ?? [], me?.permissions))
    : null;

  // Standalone top item: Dashboard
  const dashboardItem: NavItem = {
    href: "/dashboard",
    label: "Дашборд",
    icon: <LayoutDashboard size={18} />,
    roles: TENANT_ROLES,
    exact: true,
  };
  // Standalone bottom item: Settings
  const settingsItem: NavItem = {
    href: "/settings",
    label: "Налаштування",
    icon: <Settings size={18} />,
  };

  const showDashboard = !dashboardItem.roles || dashboardItem.roles.has(userRole);

  // Provider has no tenant_id outside impersonation — /api/settings/modules would 403.
  // enterprise_admin manages modules, so they bypass gating and see all groups.
  const isModuleAdmin = userRole === "provider" || userRole === "enterprise_admin";
  const { data: modulesData } = useModules(!!userRole && !isModuleAdmin);
  // null = not loaded yet (show all) OR user is a module admin (always show all)
  const modulesSet = isModuleAdmin
    ? null
    : modulesData
    ? new Set<string>(modulesData.modules)
    : null;

  // Filter groups by module activation, then by role and permissions
  const visibleGroups = NAV_GROUPS
    .filter((group) => isModuleActive(group.moduleKey, modulesSet))
    .map((group) => ({
      group,
      visibleItems: group.items.filter((item) => {
        // Role check (existing logic)
        if (item.roles && !item.roles.has(userRole)) return false;
        // Permission check: only applied for PROVIDER_TEAM users on permission-gated items
        if (effectivePermissions && item.permission) {
          return effectivePermissions.has(item.permission);
        }
        return true;
      }),
    }))
    .filter(({ visibleItems }) => visibleItems.length > 0);

  const W = collapsed ? 64 : 240;

  return (
    <aside
      style={{
        width: W,
        minHeight: "100vh",
        background: "#0D1117",
        borderRight: "1px solid #1F2937",
        display: "flex",
        flexDirection: "column",
        flexShrink: 0,
        position: "sticky",
        top: 0,
        height: "100vh",
        overflowY: "auto",
        overflowX: "hidden",
        transition: "width 0.2s ease",
      }}
    >
      {/* Logo */}
      <div
        style={{
          padding: collapsed ? "20px 0 16px" : "20px 20px 16px",
          borderBottom: "1px solid #1F2937",
          display: "flex",
          alignItems: "center",
          justifyContent: collapsed ? "center" : "flex-start",
          gap: 10,
        }}
      >
        <div
          style={{
            width: 32,
            height: 32,
            background: "linear-gradient(135deg, #3B82F6, #6366F1)",
            borderRadius: 8,
            display: "flex",
            alignItems: "center",
            justifyContent: "center",
            fontSize: 16,
            flexShrink: 0,
          }}
        >
          🛡️
        </div>
        {!collapsed && (
          <div>
            <div style={{ color: "#E8EDF5", fontSize: 14, fontWeight: 700 }}>ShelfGuard</div>
            <div style={{ color: "#4B5563", fontSize: 11 }}>v1.0</div>
          </div>
        )}
      </div>

      {/* Navigation */}
      <nav style={{ flex: 1, padding: collapsed ? "12px 8px" : "12px 10px" }}>
        <div style={{ display: "flex", flexDirection: "column", gap: 2 }}>

          {/* Standalone: Dashboard */}
          {showDashboard && (
            <NavLink item={dashboardItem} pathname={pathname} collapsed={collapsed} />
          )}

          {/* Grouped nav items */}
          {visibleGroups.map(({ group, visibleItems }) => (
            <NavGroupSection
              key={group.key}
              group={group}
              visibleItems={visibleItems}
              pathname={pathname}
              collapsed={collapsed}
            />
          ))}

        </div>
      </nav>

      {/* Bottom area: Settings + Toggle */}
      <div
        style={{
          padding: collapsed ? "12px 8px" : "12px 10px",
          borderTop: "1px solid #1F2937",
          display: "flex",
          flexDirection: "column",
          gap: 2,
        }}
      >
        {/* Settings */}
        <NavLink item={settingsItem} pathname={pathname} collapsed={collapsed} />

        {/* Collapse / Expand toggle */}
        <button
          onClick={onToggle}
          title={collapsed ? "Розгорнути меню" : "Приховати меню"}
          style={{
            width: "100%",
            display: "flex",
            alignItems: "center",
            justifyContent: collapsed ? "center" : "flex-start",
            gap: collapsed ? 0 : 10,
            padding: collapsed ? "9px 0" : "9px 12px",
            borderRadius: 8,
            background: "transparent",
            border: "none",
            color: "#4B5563",
            fontSize: 13,
            cursor: "pointer",
            textAlign: "left",
            transition: "background 0.1s, color 0.1s",
          }}
          onMouseEnter={(e) => {
            (e.currentTarget as HTMLElement).style.background = "#111827";
            (e.currentTarget as HTMLElement).style.color = "#9CA3AF";
          }}
          onMouseLeave={(e) => {
            (e.currentTarget as HTMLElement).style.background = "transparent";
            (e.currentTarget as HTMLElement).style.color = "#4B5563";
          }}
        >
          {collapsed
            ? <PanelLeftOpen size={18} style={{ opacity: 0.7 }} />
            : <><PanelLeftClose size={18} style={{ opacity: 0.7 }} />Приховати</>
          }
        </button>
      </div>
    </aside>
  );
}
