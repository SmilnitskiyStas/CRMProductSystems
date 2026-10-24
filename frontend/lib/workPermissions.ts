type WorkUser = { role: string; permissions?: Record<string, boolean> | null; capabilities?: string[] | null };
/** UI hint only: the server checks live permissions and assignments. */
export function canWorkPermission(user: WorkUser | null | undefined, key: string): boolean {
  if (!user || ["consumer", "provider", "provider_admin", "provider_agent"].includes(user.role)) return false;
  if (typeof user.permissions?.[key] === "boolean") return user.permissions[key];
  return ["enterprise_admin", "network_manager", "store_manager"].includes(user.role) || Boolean(user.capabilities?.includes(key));
}
