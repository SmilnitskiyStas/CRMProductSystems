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
  Calculator,
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
} from "lucide-react";
import { useMe } from "@/features/auth/hooks/useAuth";
import { AT_LEAST_STORE_MANAGER, CAN_RECEIVE_STOCK, CAN_VIEW_ANALYTICS, PROVIDER_ONLY, TENANT_ROLES, type AppRole } from "@/lib/roles";

interface NavItem {
  href: string;
  label: string;
  icon: React.ReactNode;
  roles?: Set<AppRole>;
  exact?: boolean;
}

const NAV_ITEMS: NavItem[] = [
  // Tenant-scoped pages — hidden from provider (no tenant context → API returns 403/empty)
  { href: "/dashboard",  label: "Дашборд",     icon: <LayoutDashboard size={18} />, roles: TENANT_ROLES },
  { href: "/inventory",  label: "Каталог",      icon: <Package size={18} />,         roles: TENANT_ROLES },
  { href: "/stock",      label: "Залишки",      icon: <ShoppingCart size={18} />,    roles: TENANT_ROLES },
  { href: "/receipts",   label: "Прийомка",     icon: <ClipboardList size={18} />,   roles: CAN_RECEIVE_STOCK },
  { href: "/transfers",  label: "Переміщення",  icon: <ArrowLeftRight size={18} />,  roles: CAN_RECEIVE_STOCK },
  { href: "/pos",        label: "Каса",         icon: <CreditCard size={18} />,      roles: CAN_RECEIVE_STOCK },
  { href: "/write-offs", label: "Списання",     icon: <Trash2 size={18} />,          roles: TENANT_ROLES },
  { href: "/sales",      label: "Продажі",      icon: <TrendingUp size={18} />,      roles: AT_LEAST_STORE_MANAGER },
  { href: "/orders",     label: "Замовлення",   icon: <Calculator size={18} />,      roles: AT_LEAST_STORE_MANAGER },
  { href: "/events",     label: "Події",        icon: <CalendarDays size={18} />,    roles: AT_LEAST_STORE_MANAGER },
  { href: "/ai-orders",  label: "AI Замовлення", icon: <Sparkles size={18} />,       roles: AT_LEAST_STORE_MANAGER },
  { href: "/analytics",     label: "Аналітика",    icon: <BarChart2 size={18} />,  roles: CAN_VIEW_ANALYTICS, exact: true },
  { href: "/analytics/pos", label: "POS Аналітика", icon: <BarChart3 size={18} />, roles: CAN_VIEW_ANALYTICS },
  { href: "/users",      label: "Персонал",     icon: <Users size={18} />,           roles: AT_LEAST_STORE_MANAGER },
  { href: "/floor-plan", label: "План магазину", icon: <Map size={18} />,            roles: AT_LEAST_STORE_MANAGER },
  { href: "/iot",        label: "IoT пристрої", icon: <Cpu size={18} />,             roles: AT_LEAST_STORE_MANAGER },
  // Provider-only pages
  { href: "/provider",   label: "Провайдер",    icon: <Shield size={18} />,          roles: PROVIDER_ONLY },
  // Shared
  { href: "/settings",   label: "Налаштування", icon: <Settings size={18} /> },
];

interface Props {
  collapsed: boolean;
  onToggle: () => void;
}

export function Sidebar({ collapsed, onToggle }: Props) {
  const pathname = usePathname();
  const { data: me } = useMe();
  const userRole = (me?.role ?? "") as AppRole;

  const visibleItems = NAV_ITEMS.filter(
    (item) => !item.roles || item.roles.has(userRole),
  );

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
          {visibleItems.map((item) => {
            const active = item.exact
              ? pathname === item.href
              : pathname === item.href || pathname.startsWith(item.href + "/");
            return (
              <Link
                key={item.href}
                href={item.href}
                title={collapsed ? item.label : undefined}
                style={{
                  display: "flex",
                  alignItems: "center",
                  justifyContent: collapsed ? "center" : "flex-start",
                  gap: collapsed ? 0 : 10,
                  padding: collapsed ? "9px 0" : "9px 12px",
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
          })}
        </div>
      </nav>

      {/* Collapse / Expand toggle */}
      <div
        style={{
          padding: collapsed ? "12px 8px" : "12px 10px",
          borderTop: "1px solid #1F2937",
        }}
      >
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
