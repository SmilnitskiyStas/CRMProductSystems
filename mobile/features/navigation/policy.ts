import { AppRoles } from '@/lib/roles';
import type { NavigationContext, NavigationDecision } from './types';

export const MOBILE_ROUTES = {
  dashboard: '/(app)',
  stock: '/(app)/stock',
  inventory: '/(app)/inventory',
  scan: '/(app)/scan',
  receipt: '/(app)/receipt',
  pos: '/(app)/pos',
  writeOffs: '/(app)/write-offs',
  transfers: '/(app)/transfers',
  customers: '/(app)/customers',
  schedules: '/(app)/schedules',
  serviceDesk: '/(app)/service-desk',
  marketplace: '/(app)/marketplace',
  marketplaceOrders: '/(app)/marketplace-orders',
  production: '/(app)/production',
  autoService: '/(app)/auto-service',
  aiAssistant: '/(app)/ai-assistant',
  more: '/(app)/more',
  profile: '/(app)/profile',
  notifications: '/(app)/notifications',
  movements: '/(app)/movements',
} as const;

type RouteRule = {
  prefix: string;
  roles?: readonly string[];
  capability?: string;
  tab?: string;
  module?: string;
  businessTypes?: readonly string[];
};

const TENANT_STAFF = [
  AppRoles.Cashier, AppRoles.Storekeeper, AppRoles.Merchandiser, AppRoles.StoreManager,
  AppRoles.NetworkManager, AppRoles.EnterpriseAdmin, AppRoles.SupplierAdmin,
] as const;
const MANAGERS = [AppRoles.StoreManager, AppRoles.NetworkManager, AppRoles.EnterpriseAdmin] as const;
const SHELL_ROLES = [...TENANT_STAFF, AppRoles.Provider, AppRoles.ProviderAdmin, AppRoles.ProviderAgent] as const;

export const ROUTE_RULES: readonly RouteRule[] = [
  { prefix: MOBILE_ROUTES.profile, roles: SHELL_ROLES },
  { prefix: MOBILE_ROUTES.notifications, roles: SHELL_ROLES },
  { prefix: MOBILE_ROUTES.movements, roles: [AppRoles.Storekeeper, AppRoles.Merchandiser, ...MANAGERS], module: 'inventory', tab: 'inventory' },
  { prefix: MOBILE_ROUTES.more, roles: SHELL_ROLES },
  { prefix: MOBILE_ROUTES.pos, roles: [AppRoles.Cashier, AppRoles.Storekeeper, ...MANAGERS], module: 'pos', tab: 'pos' },
  { prefix: MOBILE_ROUTES.receipt, roles: [AppRoles.Storekeeper, ...MANAGERS], module: 'procurement', tab: 'procurement' },
  { prefix: MOBILE_ROUTES.writeOffs, roles: [AppRoles.Storekeeper, ...MANAGERS], module: 'inventory', tab: 'inventory' },
  { prefix: MOBILE_ROUTES.transfers, roles: [AppRoles.Storekeeper, ...MANAGERS], module: 'inventory', tab: 'inventory' },
  { prefix: MOBILE_ROUTES.stock, roles: [AppRoles.Storekeeper, AppRoles.Merchandiser, ...MANAGERS], module: 'inventory', tab: 'inventory' },
  { prefix: MOBILE_ROUTES.inventory, roles: [AppRoles.Storekeeper, AppRoles.Merchandiser, ...MANAGERS], module: 'inventory', tab: 'inventory' },
  { prefix: MOBILE_ROUTES.scan, roles: [AppRoles.Cashier, AppRoles.Storekeeper, AppRoles.Merchandiser, ...MANAGERS], module: 'inventory' },
  { prefix: MOBILE_ROUTES.customers, roles: MANAGERS, capability: 'customers.manage', tab: 'customers' },
  { prefix: MOBILE_ROUTES.serviceDesk, roles: MANAGERS, tab: 'service_desk' },
  { prefix: MOBILE_ROUTES.schedules, roles: TENANT_STAFF, tab: 'schedules' },
  { prefix: MOBILE_ROUTES.marketplaceOrders, roles: [AppRoles.Storekeeper, ...MANAGERS], module: 'marketplace', tab: 'marketplace' },
  { prefix: MOBILE_ROUTES.marketplace, roles: TENANT_STAFF, module: 'marketplace', tab: 'marketplace' },
  { prefix: MOBILE_ROUTES.production, roles: [AppRoles.Storekeeper, ...MANAGERS], module: 'production', tab: 'production', businessTypes: ['restaurant', 'production'] },
  { prefix: MOBILE_ROUTES.autoService, roles: MANAGERS, module: 'auto_service', tab: 'auto_service', businessTypes: ['auto_service'] },
  { prefix: MOBILE_ROUTES.aiAssistant, roles: MANAGERS, tab: 'analytics', module: 'inventory' },
  { prefix: MOBILE_ROUTES.dashboard, roles: SHELL_ROLES },
] as const;

function matches(value: string, prefix: string) {
  if (prefix === MOBILE_ROUTES.dashboard) return value === prefix;
  return value === prefix || value.startsWith(`${prefix}/`);
}

export function navigationDecision(route: string, context: NavigationContext): NavigationDecision {
  const rule = ROUTE_RULES.find((candidate) => matches(route, candidate.prefix));
  if (!rule || !context.user) return { allowed: false, reason: 'access_denied' };
  const roleAllowed = rule.roles?.includes(context.user.role) ?? false;
  const capabilityAllowed = Boolean(rule.capability && context.user.capabilities.includes(rule.capability));
  const tabAllowed = Boolean(rule.tab && context.user.tabs.includes(rule.tab));
  if (!roleAllowed && !capabilityAllowed && !tabAllowed) {
    return { allowed: false, reason: 'access_denied' };
  }
  if (rule.tab && context.user.tabs.length > 0 && !tabAllowed && !capabilityAllowed) {
    return { allowed: false, reason: 'access_denied' };
  }
  if (rule.module || rule.businessTypes) {
    if (!context.settings) return { allowed: false, reason: 'context_unavailable' };
    if (rule.module && !context.settings.modules.includes(rule.module)) {
      return { allowed: false, reason: 'module_disabled' };
    }
    if (rule.businessTypes && !rule.businessTypes.includes(context.settings.businessType)) {
      return { allowed: false, reason: 'access_denied' };
    }
  }
  return { allowed: true };
}

export function visibleRoutes<T extends { href: string }>(items: readonly T[], context: NavigationContext): T[] {
  return items.filter((item) => navigationDecision(item.href, context).allowed);
}
