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
  Building2,
  HeartHandshake,
  ShoppingBag,
  FileText,
  MessageCircle,
  Landmark,
  Megaphone,
  Target,
  LineChart,
} from "lucide-react";
import { useState, useEffect, useRef, useCallback, useMemo } from "react";
import { createPortal } from "react-dom";
import { useTranslations } from "next-intl";
import { useMe } from "@/features/auth/hooks/useAuth";
import { useModules } from "@/features/modules/hooks/useModules";
import { useSupplierChatSessions } from "@/features/supplier-cabinet/hooks/useSupplierCabinet";
import type { ModuleKey } from "@/features/modules/types";
import {
  AppRoles,
  AT_LEAST_ENTERPRISE_ADMIN,
  AT_LEAST_STORE_MANAGER,
  CAN_ACCESS_POS,
  CAN_MANAGE_WAREHOUSE,
  CAN_VIEW_ANALYTICS,
  CAN_VIEW_WAREHOUSE,
  canManageLegalEntities,
  PROVIDER_ONLY,
  PROVIDER_TEAM,
  SUPPLIER_ONLY,
  TENANT_ROLES,
  type AppRole,
} from "@/lib/roles";
import {
  SYSTEM_ROLE_PERMISSIONS,
  resolvePermissions,
} from "@/lib/providerPermissions";
import { ALL_SUPPLIER_PERMISSIONS } from "@/lib/supplierPermissions";

// ── Types ──────────────────────────────────────────────────────────────────────

interface NavItem {
  href: string;
  label: string;
  icon: React.ReactNode;
  roles?: Set<AppRole>;
  /** Permission key(s) required for PROVIDER_TEAM users — user needs at least one (OR logic) */
  permission?: string | string[];
  exact?: boolean;
  /** Optional unread-count badge (currently only set on the supplier "Повідомлення" item). */
  badge?: number;
}

interface NavGroup {
  key: string;
  label: string;
  icon: React.ReactNode;
  /** When set, the whole group is hidden unless this module is active for the tenant. */
  moduleKey?: ModuleKey;
  /** When set, the group renders permanently expanded with no collapse toggle. */
  alwaysExpanded?: boolean;
  items: NavItem[];
}

// ── Navigation definition (v4 — ADR-014/015 module-gated groups) ───────────────
//
// Marketplace and Service Desk groups from the v4-spec menu structure are NOT
// included here — there are no pages for them yet (Phase 3 Supplier Marketplace;
// Service Desk only exists today as the "Підтримка" tab inside Settings). Add them
// once their pages exist; don't render empty/dead-link groups.

// NAV_GROUPS/SUPPLIER_NAV_GROUP are built by functions (not module-level consts)
// because their `label` strings now come from next-intl — `t` is only available
// inside the component, via `useTranslations` (i18n Block 1, TASK-376). Untyped to
// the specific "Dashboard.sidebar.groups" namespace on purpose — this project has no
// global `Messages`/`IntlMessages` augmentation, so the generic-namespace form of
// `useTranslations<...>` isn't wired up; the general translator shape is enough here.
type SidebarGroupsT = ReturnType<typeof useTranslations>;

function buildNavGroups(t: SidebarGroupsT): NavGroup[] {
  return [
  {
    key: "operations",
    label: t("operations.label"),
    icon: <Package size={18} />,
    moduleKey: "inventory",
    items: [
      { href: "/inventory",  label: t("operations.catalog"),   icon: <Package size={16} />,      roles: CAN_VIEW_WAREHOUSE },
      { href: "/stock",      label: t("operations.stock"),     icon: <ShoppingCart size={16} />, roles: CAN_VIEW_WAREHOUSE },
      { href: "/receipts",   label: t("operations.receipts"),  icon: <ClipboardList size={16} />, roles: CAN_MANAGE_WAREHOUSE },
      { href: "/transfers",  label: t("operations.transfers"), icon: <ArrowLeftRight size={16} />, roles: CAN_MANAGE_WAREHOUSE },
      { href: "/write-offs", label: t("operations.writeOffs"), icon: <Trash2 size={16} />,       roles: CAN_VIEW_WAREHOUSE },
      { href: "/locations",  label: t("operations.locations"), icon: <Map size={16} />,          roles: AT_LEAST_STORE_MANAGER },
      { href: "/iot",        label: t("operations.iot"),       icon: <Cpu size={16} />,          roles: AT_LEAST_STORE_MANAGER },
    ],
  },
  {
    key: "sales",
    label: t("sales.label"),
    icon: <TrendingUp size={18} />,
    moduleKey: "pos",
    items: [
      { href: "/pos",       label: t("sales.pos"),       icon: <CreditCard size={16} />,   roles: CAN_ACCESS_POS },
      { href: "/sales",     label: t("sales.sales"),     icon: <TrendingUp size={16} />,   roles: AT_LEAST_STORE_MANAGER },
      { href: "/customers", label: t("sales.customers"), icon: <Users size={16} />,        roles: AT_LEAST_STORE_MANAGER },
      { href: "/events",    label: t("sales.events"),    icon: <CalendarDays size={16} />,  roles: AT_LEAST_STORE_MANAGER },
    ],
  },
  {
    key: "procurement",
    label: t("procurement.label"),
    icon: <Truck size={18} />,
    moduleKey: "procurement",
    items: [
      { href: "/orders",    label: t("procurement.orders"),   icon: <Calculator size={16} />, roles: AT_LEAST_STORE_MANAGER },
      { href: "/ai-orders", label: t("procurement.aiOrders"), icon: <Sparkles size={16} />,   roles: AT_LEAST_STORE_MANAGER },
    ],
  },
  {
    key: "marketplace",
    label: t("marketplace.label"),
    icon: <Store size={18} />,
    moduleKey: "marketplace",
    items: [
      { href: "/marketplace", label: t("marketplace.suppliers"), icon: <Store size={16} />, exact: true, permission: "marketplace" },
      // Клієнтські marketplace-замовлення (TASK-318) — тільки tenant-ролі:
      // провайдерська команда і supplier_admin не мають своїх замовлень.
      { href: "/marketplace/orders", label: t("marketplace.myOrders"), icon: <ShoppingBag size={16} />, roles: TENANT_ROLES },
    ],
  },
  {
    key: "auto_service",
    label: t("autoService.label"),
    icon: <Wrench size={18} />,
    moduleKey: "auto_service",
    items: [
      { href: "/auto-service",                  label: t("autoService.workOrders"),     icon: <Wrench size={16} />,   roles: AT_LEAST_STORE_MANAGER, exact: true },
      { href: "/auto-service/customers",         label: t("autoService.customers"),      icon: <Car size={16} />,      roles: AT_LEAST_STORE_MANAGER },
      { href: "/auto-service/service-catalog",   label: t("autoService.serviceCatalog"), icon: <BookOpen size={16} />, roles: AT_LEAST_STORE_MANAGER },
    ],
  },
  {
    key: "production",
    label: t("production.label"),
    icon: <FlaskConical size={18} />,
    moduleKey: "production",
    items: [
      { href: "/production/recipes", label: t("production.recipes"), icon: <FlaskConical size={16} />, roles: AT_LEAST_STORE_MANAGER },
      { href: "/production/orders",  label: t("production.orders"),  icon: <ListOrdered size={16} />,  roles: AT_LEAST_STORE_MANAGER },
    ],
  },
  {
    key: "analytics",
    label: t("analytics.label"),
    icon: <BarChart2 size={18} />,
    items: [
      { href: "/analytics",     label: t("analytics.analytics"),    icon: <BarChart2 size={16} />, roles: CAN_VIEW_ANALYTICS, exact: true, permission: "analytics" },
      { href: "/analytics/pos", label: t("analytics.posAnalytics"), icon: <BarChart3 size={16} />, roles: CAN_VIEW_ANALYTICS, permission: "analytics" },
    ],
  },
  {
    key: "marketing_analytics",
    label: t("marketingAnalytics.label"),
    icon: <Megaphone size={18} />,
    moduleKey: "marketing_analytics",
    items: [
      { href: "/marketing-analytics", label: t("marketingAnalytics.dashboard"), icon: <Megaphone size={16} />, roles: CAN_VIEW_ANALYTICS, exact: true, permission: "analytics" },
      { href: "/marketing-analytics/price-segments", label: t("marketingAnalytics.priceSegments"), icon: <TrendingUp size={16} />, roles: CAN_VIEW_ANALYTICS, permission: "analytics" },
      { href: "/marketing-analytics/audience-builder", label: t("marketingAnalytics.audienceBuilder"), icon: <Target size={16} />, roles: CAN_VIEW_ANALYTICS, permission: "analytics" },
      { href: "/marketing-analytics/post-campaign", label: t("marketingAnalytics.postCampaign"), icon: <LineChart size={16} />, roles: CAN_VIEW_ANALYTICS, permission: "analytics" },
    ],
  },
  {
    key: "workforce",
    label: t("workforce.label"),
    icon: <Users size={18} />,
    items: [
      { href: "/users",          label: t("workforce.users"),     icon: <Users size={16} />,    roles: AT_LEAST_STORE_MANAGER },
      { href: "/schedules",      label: t("workforce.schedules"), icon: <Calendar size={16} />, permission: "schedule_management" },
      { href: "/provider/team",  label: t("workforce.team"),      icon: <Users size={16} />,    roles: PROVIDER_TEAM, permission: "team_management" },
      // Legal Entities (TASK-323): enterprise_admin/provider always see it; other
      // tenant roles only if granted the `legal_entities.manage` override — that
      // check can't be expressed via `roles`/`permission` (mirrors backend role-OR-
      // permission logic), so it's filtered in manually below via canManageLegalEntities.
      { href: "/settings/legal-entities", label: t("workforce.legalEntities"), icon: <Landmark size={16} />, roles: AT_LEAST_ENTERPRISE_ADMIN },
    ],
  },
  {
    key: "support",
    label: t("support.label"),
    icon: <LifeBuoy size={18} />,
    items: [
      { href: "/service-desk", label: t("support.serviceDesk"), icon: <LifeBuoy size={16} />, permission: "service_desk" },
    ],
  },
  {
    key: "admin",
    label: t("admin.label"),
    icon: <Shield size={18} />,
    items: [
      { href: "/provider", label: t("admin.provider"), icon: <Shield size={16} />,   roles: PROVIDER_TEAM, exact: true, permission: ["view_clients", "manage_clients"] },
      { href: "/admin",    label: t("admin.admin"),    icon: <Settings size={16} />, roles: PROVIDER_ONLY, permission: "admin_panel" },
    ],
  },
  ];
}

// Supplier cabinet (v4.1, TASK-286, ADR-016; permissions TASK-307) — the ONLY
// group a supplier_admin sees. Rendered instead of NAV_GROUPS for that role;
// not module-gated on the client (the backend already gates /api/supplier-cabinet
// with marketplace_supplier). Items are additionally filtered by permission key
// when the user has a non-null permissions dict (see effectiveSupplierPermissions
// below) — null permissions = full/owner access, so nothing is hidden.
function buildSupplierNavGroup(t: SidebarGroupsT): NavGroup {
  return {
  key: "supplier_cabinet",
  label: t("supplierCabinet.label"),
  icon: <Store size={18} />,
  // supplier_admin only ever renders this one group (see sourceGroups below) —
  // collapsing the sole group hid every cabinet link (items, reviews, etc.)
  // behind an easy-to-miss toggle. Always expanded, no reason to collapse it.
  alwaysExpanded: true,
  items: [
    { href: "/supplier/profile", label: t("supplierCabinet.profile"),  icon: <Store size={16} />,        roles: SUPPLIER_ONLY, permission: "profile_management" },
    { href: "/supplier/items",   label: t("supplierCabinet.myItems"),  icon: <Package size={16} />,      roles: SUPPLIER_ONLY, permission: "catalog_management" },
    { href: "/supplier/reviews", label: t("supplierCabinet.reviews"),  icon: <ClipboardList size={16} />, roles: SUPPLIER_ONLY, permission: "client_reviews" },
    { href: "/supplier/tasks",   label: t("supplierCabinet.tasks"),    icon: <ListOrdered size={16} />,   roles: SUPPLIER_ONLY, permission: "task_board" },
    { href: "/supplier/clients", label: t("supplierCabinet.clients"), icon: <Building2 size={16} />,     roles: SUPPLIER_ONLY, permission: "client_management" },
    { href: "/supplier/team",    label: t("supplierCabinet.team"),    icon: <Users size={16} />,         roles: SUPPLIER_ONLY, permission: "staff_management" },
    // Cooperation flow (TASK-318) — без permission-ключів: у довіднику
    // supplierPermissions поки немає відповідних прав. NOTE (TASK-359 audit):
    // the backend does NOT gate these routes either — any supplier_admin
    // staff member has full access regardless of their assigned SupplierRole.
    // Flagged as an open product decision (which permission key(s), if any,
    // should cover cooperation-requests/orders/contract-settings/support-tickets);
    // not fixed here.
    { href: "/supplier/requests",          label: t("supplierCabinet.requests"),          icon: <HeartHandshake size={16} />, roles: SUPPLIER_ONLY },
    { href: "/supplier/orders",            label: t("supplierCabinet.orders"),            icon: <ShoppingBag size={16} />, roles: SUPPLIER_ONLY },
    { href: "/supplier/contract-settings", label: t("supplierCabinet.contractSettings"),  icon: <FileText size={16} />,    roles: SUPPLIER_ONLY },
    { href: "/supplier/support",           label: t("supplierCabinet.support"),           icon: <LifeBuoy size={16} />,    roles: SUPPLIER_ONLY },
    // Messaging (BUG-019) — moved out from under /supplier/clients (client_management)
    // because staff without that permission still need to reply to client chats;
    // same ungated treatment as the TASK-318 items above.
    { href: "/supplier/messages",          label: t("supplierCabinet.messages"),          icon: <MessageCircle size={16} />, roles: SUPPLIER_ONLY },
  ],
  };
}

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
      {!collapsed && !!item.badge && item.badge > 0 && (
        <span
          style={{
            marginLeft: "auto",
            background: "#EF4444",
            color: "#fff",
            borderRadius: 999,
            padding: "1px 6px",
            fontSize: 10,
            fontWeight: 700,
            minWidth: 16,
            textAlign: "center",
            lineHeight: 1.4,
          }}
        >
          {item.badge > 9 ? "9+" : item.badge}
        </span>
      )}
    </Link>
  );
}

interface CollapsedGroupTriggerProps {
  group: NavGroup;
  visibleItems: NavItem[];
  pathname: string;
  hasActive: boolean;
}

interface PopoverPos {
  top: number;
  left: number;
}

/**
 * Collapsed-sidebar rendering of a nav group. Single-item groups navigate
 * straight to that item (no point popping a 1-row popover); multi-item groups
 * open a small popover — positioned like ActionMenu (portal + viewport-aware
 * flip, outside-click + scroll/resize dismissal) — listing the child links so
 * they stay reachable while the sidebar is collapsed.
 */
function CollapsedGroupTrigger({ group, visibleItems, pathname, hasActive }: CollapsedGroupTriggerProps) {
  const [open, setOpen] = useState(false);
  const [pos, setPos] = useState<PopoverPos | null>(null);
  const triggerRef = useRef<HTMLButtonElement>(null);
  const menuRef = useRef<HTMLDivElement>(null);

  const calcPos = useCallback(() => {
    if (!triggerRef.current) return;
    const rect = triggerRef.current.getBoundingClientRect();
    const viewportHeight = window.innerHeight;

    // Popover sits to the right of the collapsed rail, top-aligned with the trigger.
    let top = rect.top + window.scrollY;
    const estimatedHeight = 40 + visibleItems.length * 36;
    if (rect.top + estimatedHeight > viewportHeight) {
      // flip up so it doesn't run off the bottom of the screen
      top = Math.max(8, rect.bottom - estimatedHeight) + window.scrollY;
    }

    setPos({
      top,
      left: rect.right + 8 + window.scrollX,
    });
  }, [visibleItems.length]);

  const toggle = () => {
    if (visibleItems.length <= 1) return;
    if (!open) calcPos();
    setOpen((v) => !v);
  };

  // Close on outside click
  useEffect(() => {
    if (!open) return;
    function handle(e: MouseEvent) {
      const target = e.target as Node;
      if (triggerRef.current?.contains(target) || menuRef.current?.contains(target)) return;
      setOpen(false);
    }
    document.addEventListener("mousedown", handle);
    return () => document.removeEventListener("mousedown", handle);
  }, [open]);

  // Reposition on scroll / resize
  useEffect(() => {
    if (!open) return;
    const reCalc = () => calcPos();
    window.addEventListener("scroll", reCalc, true);
    window.addEventListener("resize", reCalc);
    return () => {
      window.removeEventListener("scroll", reCalc, true);
      window.removeEventListener("resize", reCalc);
    };
  }, [open, calcPos]);

  // Single-item "group" — just navigate directly, no popover needed.
  const singleItem = visibleItems.length === 1 ? visibleItems[0] : null;

  const popover =
    open && pos
      ? createPortal(
          <div
            ref={menuRef}
            style={{
              position: "absolute",
              top: pos.top,
              left: pos.left,
              width: 220,
              background: "#111827",
              border: "1px solid #1F2937",
              borderRadius: 10,
              boxShadow: "0 8px 24px rgba(0,0,0,0.6)",
              zIndex: 9999,
              overflow: "hidden",
              padding: 4,
            }}
          >
            <div
              style={{
                padding: "6px 10px 4px",
                color: "#6B7280",
                fontSize: 11,
                fontWeight: 600,
                letterSpacing: "0.04em",
                textTransform: "uppercase",
              }}
            >
              {group.label}
            </div>
            {visibleItems.map((item) => {
              const active = isActive(pathname, item.href, item.exact);
              return (
                <Link
                  key={item.href + group.key}
                  href={item.href}
                  onClick={() => setOpen(false)}
                  style={{
                    display: "flex",
                    alignItems: "center",
                    gap: 9,
                    padding: "8px 10px",
                    borderRadius: 8,
                    background: active ? "#1D3461" : "transparent",
                    color: active ? "#93C5FD" : "#9CA3AF",
                    fontSize: 13,
                    fontWeight: active ? 600 : 400,
                    textDecoration: "none",
                    transition: "background 0.1s, color 0.1s",
                  }}
                  onMouseEnter={(e) => {
                    if (!active) (e.currentTarget as HTMLElement).style.background = "#1F2937";
                  }}
                  onMouseLeave={(e) => {
                    if (!active) (e.currentTarget as HTMLElement).style.background = "transparent";
                  }}
                >
                  <span style={{ opacity: active ? 1 : 0.8, flexShrink: 0, display: "flex" }}>{item.icon}</span>
                  {item.label}
                  {!!item.badge && item.badge > 0 && (
                    <span
                      style={{
                        marginLeft: "auto",
                        background: "#EF4444",
                        color: "#fff",
                        borderRadius: 999,
                        padding: "1px 6px",
                        fontSize: 10,
                        fontWeight: 700,
                        minWidth: 16,
                        textAlign: "center",
                        lineHeight: 1.4,
                      }}
                    >
                      {item.badge > 9 ? "9+" : item.badge}
                    </span>
                  )}
                </Link>
              );
            })}
          </div>,
          document.body,
        )
      : null;

  if (singleItem) {
    return <NavLink item={singleItem} pathname={pathname} collapsed />;
  }

  return (
    <>
      <button
        ref={triggerRef}
        onClick={toggle}
        title={group.label}
        style={{
          width: "100%",
          display: "flex",
          alignItems: "center",
          justifyContent: "center",
          padding: "9px 0",
          borderRadius: 8,
          background: open ? "#111827" : "transparent",
          border: "none",
          color: hasActive ? "#93C5FD" : "#6B7280",
          opacity: hasActive ? 1 : 0.7,
          cursor: "pointer",
          transition: "background 0.1s, color 0.1s",
        }}
        onMouseEnter={(e) => {
          (e.currentTarget as HTMLElement).style.background = "#111827";
        }}
        onMouseLeave={(e) => {
          if (!open) (e.currentTarget as HTMLElement).style.background = "transparent";
        }}
      >
        {group.icon}
      </button>
      {popover}
    </>
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
  const [expanded, setExpanded] = useState(hasActive || !!group.alwaysExpanded);

  // Auto-expand when navigating to a child route
  useEffect(() => {
    if (hasActive) setExpanded(true);
  }, [hasActive]);

  // In collapsed mode — show only the group icon. Groups with children open a
  // popover on click (children are otherwise unreachable while collapsed —
  // this used to be a static <div> with no click handler at all, see BUG report).
  if (collapsed) {
    return (
      <CollapsedGroupTrigger group={group} visibleItems={visibleItems} pathname={pathname} hasActive={hasActive} />
    );
  }

  return (
    <div>
      {/* Group header — toggle button (no-op when alwaysExpanded, e.g. the
          supplier cabinet's sole group has nothing to collapse in favor of) */}
      <button
        onClick={() => !group.alwaysExpanded && setExpanded((v) => !v)}
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
          cursor: group.alwaysExpanded ? "default" : "pointer",
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
        {!group.alwaysExpanded && (
          expanded
            ? <ChevronDown size={14} style={{ opacity: 0.6, flexShrink: 0 }} />
            : <ChevronRight size={14} style={{ opacity: 0.6, flexShrink: 0 }} />
        )}
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

  const t = useTranslations("Dashboard.sidebar");
  const tGroups = useTranslations("Dashboard.sidebar.groups");
  const navGroups = useMemo(() => buildNavGroups(tGroups), [tGroups]);
  const supplierNavGroup = useMemo(() => buildSupplierNavGroup(tGroups), [tGroups]);

  // Resolve effective permissions for PROVIDER_TEAM users
  const isProviderTeamMember = PROVIDER_TEAM.has(userRole);
  const effectivePermissions = isProviderTeamMember
    ? new Set(resolvePermissions(SYSTEM_ROLE_PERMISSIONS[userRole] ?? [], me?.permissions))
    : null;

  // Resolve effective permissions for supplier cabinet staff (TASK-307).
  // me.permissions === null/undefined means full/owner access (no filtering) —
  // it's only set when the user was invited with a specific supplier role.
  const isSupplierAdminRole = userRole === AppRoles.SupplierAdmin;
  const supplierEffectivePermissions =
    isSupplierAdminRole && me?.permissions
      ? new Set(ALL_SUPPLIER_PERMISSIONS.filter((p) => me.permissions?.[p]))
      : null;

  // Sidebar tab visibility (TASK-391c, ADR-020 follow-up): TenantRole.AllowedTabs is a
  // separate axis from Capabilities — which whole nav sections are visible, independent
  // of per-item role checks. UI-only signal (no Tier 2 backend enforcement yet, see
  // backend/ShelfGuard.Domain/Constants/TenantRoleTabs.cs) — null/empty (no TenantRoleId,
  // most users today) is a no-op, existing role-based filtering below is unaffected.
  const tabsSet = me?.tabs && me.tabs.length > 0 ? new Set(me.tabs) : null;

  // Standalone top item: Dashboard
  const dashboardItem: NavItem = {
    href: "/dashboard",
    label: t("dashboard"),
    icon: <LayoutDashboard size={18} />,
    roles: TENANT_ROLES,
    exact: true,
  };
  // Standalone bottom item: Settings
  const settingsItem: NavItem = {
    href: "/settings",
    label: t("settings"),
    icon: <Settings size={18} />,
  };

  // Sidebar tab visibility (TASK-391c; made authoritative/exclusive in TASK-397): previously
  // this standalone item never consulted tabsSet at all, so (un)checking "Дашборд" in a
  // TenantRole's "Видимі розділи" editor had zero effect on it either direction — a real,
  // separate bug from the additive-OR one fixed below (this one was non-functional, not just
  // additive). Now mirrors the exact same override semantic as the grouped items just below:
  // a non-empty tabsSet is authoritative (grants OR revokes, replacing the role check
  // entirely); tabsSet null (default — no TenantRole tabs configured) preserves the original
  // role-only check unchanged.
  // "dashboard" — matches backend TenantRoleTabs.Dashboard and the tab-key convention every
  // NavGroup.key already follows (no shared frontend constant for these; group keys below are
  // likewise plain string literals, e.g. "workforce", "operations").
  // TASK-399 (per-item granularity for the grouped items below) doesn't change this check:
  // Dashboard is a single top-level NavItem, not a NavGroup, so it never had a separate
  // group-key/item-key pair to begin with — "dashboard" already serves both roles (see
  // TenantRoleTabs.cs's class doc comment, "no second key" rationale).
  const showDashboard = tabsSet
    ? tabsSet.has("dashboard")
    : !dashboardItem.roles || dashboardItem.roles.has(userRole);

  // supplier_admin (v4.1) sees only the supplier cabinet + Settings — none of the
  // regular NAV_GROUPS, and no module fetch (its menu is fixed).
  const isSupplierAdmin = userRole === AppRoles.SupplierAdmin;

  // Aggregate unread badge for the supplier cabinet "Повідомлення" nav item.
  // Only fetched for supplier_admin — other roles don't have this endpoint access.
  const { data: chatSessions } = useSupplierChatSessions(isSupplierAdmin);
  const supplierUnreadTotal = isSupplierAdmin
    ? (chatSessions ?? []).reduce((sum, s) => sum + (s.unreadCount ?? 0), 0)
    : 0;

  // Only bare provider sessions have no tenant_id — /api/settings/modules would 403.
  // enterprise_admin (real clients AND impersonation sessions) must go through module
  // gating so only their tenant's enabled modules are visible.
  const isModuleAdmin = userRole === "provider";
  const { data: modulesData } = useModules(!!userRole && !isModuleAdmin && !isSupplierAdmin);
  // null = user is provider (show all) OR data still loading (show all until resolved)
  const modulesSet = isModuleAdmin
    ? null
    : modulesData
    ? new Set<string>(modulesData.modules)
    : null;

  // Filter groups by module activation, then by role and permissions.
  // supplier_admin bypasses NAV_GROUPS entirely — only the cabinet group.
  const sourceGroups = isSupplierAdmin ? [supplierNavGroup] : navGroups;
  const visibleGroups = sourceGroups
    .filter((group) => isModuleActive(group.moduleKey, modulesSet))
    .map((group) => ({
      group,
      visibleItems: group.items.filter((item) => {
        // Legal Entities (TASK-323): role check OR the `legal_entities.manage`
        // per-user override — same OR-logic as backend LegalEntityAuthorization.CanManage.
        // Independent of tabsSet in both directions (deliberately not part of the
        // TenantRole-tabs override below, TASK-397) — this narrow, security-sensitive gate
        // keeps deciding this one item on its own regardless of what tabs are configured.
        if (item.href === "/settings/legal-entities") {
          return canManageLegalEntities(userRole, me?.permissions);
        }
        // Sidebar tab visibility (TASK-391c; made authoritative/exclusive in TASK-397;
        // per-item granularity in TASK-399): a non-empty tabsSet (the user's effective
        // TenantRole has "Видимі розділи" configured) is the DEFINITIVE, EXCLUSIVE source of
        // visibility for that user — it completely REPLACES the item.roles/permission checks
        // below, rather than just OR-ing into them. Previously `tabsSet?.has(group.key))
        // return true` could only ADD visibility, never remove it (the TASK-397 fix). tabsSet
        // is null whenever the user has no TenantRole tabs configured at all (the default, and
        // the common case for existing capability-only TenantRole users) — that case falls
        // through unchanged to the item.roles/permission checks below, preserving exact prior
        // behavior for everyone not using this feature.
        //
        // TASK-399: AllowedTabs can now carry item-level keys (`item.href`, e.g. "/receipts")
        // alongside the original group-level keys (`group.key`, e.g. "operations") — see
        // backend/ShelfGuard.Domain/Constants/TenantRoleTabs.cs's ItemKeys/GroupKeys. A group
        // key still bulk-grants every item in the group (backward compat with every
        // already-configured TenantRole row); an item key grants just that one page. Checking
        // per item rather than per group is what actually makes the fine-grained grant visible
        // — with the old `tabsSet.has(group.key)`-only check, granting a lone item key had zero
        // effect (backend TASK-398 shipped the catalog/validation for this; this line is the
        // frontend enforcement follow-up it explicitly deferred). If NEITHER key is present for
        // any item in the group, `visibleItems` ends up empty and the whole group section
        // doesn't render — handled by the existing `.filter(({ visibleItems }) => ...)` below,
        // unchanged by this task.
        if (tabsSet) return tabsSet.has(item.href) || tabsSet.has(group.key);
        // Role check (existing logic)
        if (item.roles && !item.roles.has(userRole)) return false;
        // Permission check: only applied for PROVIDER_TEAM users on permission-gated items
        if (effectivePermissions && item.permission) {
          const perms = Array.isArray(item.permission) ? item.permission : [item.permission];
          return perms.some((p) => effectivePermissions.has(p));
        }
        // Permission check for supplier cabinet staff with a restricted role
        if (supplierEffectivePermissions && item.permission) {
          const perms = Array.isArray(item.permission) ? item.permission : [item.permission];
          return perms.some((p) => supplierEffectivePermissions.has(p));
        }
        return true;
      }),
    }))
    .filter(({ visibleItems }) => visibleItems.length > 0)
    .map(({ group, visibleItems }) => ({
      group,
      // Attach the aggregate unread badge to the supplier "Повідомлення" item only.
      visibleItems: visibleItems.map((item) =>
        item.href === "/supplier/messages" && supplierUnreadTotal > 0
          ? { ...item, badge: supplierUnreadTotal }
          : item,
      ),
    }));

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
          title={collapsed ? t("expandTooltip") : t("collapseTooltip")}
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
            : <><PanelLeftClose size={18} style={{ opacity: 0.7 }} />{t("collapseLabel")}</>
          }
        </button>
      </div>
    </aside>
  );
}
