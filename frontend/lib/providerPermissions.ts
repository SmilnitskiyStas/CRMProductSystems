export const PROVIDER_PERMISSIONS: Record<string, string> = {
  team_management:     "Управління командою",
  view_clients:        "Перегляд клієнтів",
  manage_clients:      "Управління клієнтами",
  service_desk:        "Service Desk та тікети",
  live_chat:           "Живий чат",
  admin_panel:         "Адмін-панель",
  marketplace:         "Маркетплейс",
  analytics:           "Аналітика",
  schedule_management: "Управління розкладом",
};

export const ALL_PERMISSIONS = Object.keys(PROVIDER_PERMISSIONS);

export const SYSTEM_ROLE_PERMISSIONS: Record<string, string[]> = {
  provider:       ALL_PERMISSIONS,
  provider_admin: ALL_PERMISSIONS,
  provider_agent: ["view_clients", "service_desk", "live_chat"],
};

/** Merge role permissions + per-user override ({ key: true = grant, false = deny }) */
export function resolvePermissions(
  basePermissions: string[],
  override?: Record<string, boolean> | null
): string[] {
  if (!override) return basePermissions;
  const set = new Set(basePermissions);
  for (const [key, granted] of Object.entries(override)) {
    if (granted) set.add(key);
    else set.delete(key);
  }
  return ALL_PERMISSIONS.filter((p) => set.has(p));
}
