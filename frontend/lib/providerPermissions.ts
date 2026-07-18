// Labels for these permission keys live in i18n (Dashboard.provider.permissions.*,
// `useTranslations`) — see RolesSection.tsx / EditMemberModal.tsx /
// InviteProviderMemberModal.tsx. Kept here only as the canonical key list.
export const ALL_PERMISSIONS: string[] = [
  "team_management",
  "view_clients",
  "manage_clients",
  "service_desk",
  "live_chat",
  "admin_panel",
  "marketplace",
  "analytics",
  "schedule_management",
];

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
