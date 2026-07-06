export const SUPPLIER_PERMISSIONS: Record<string, string> = {
  catalog_management: "Управління каталогом",
  client_reviews:      "Відгуки клієнтів",
  task_board:          "Дошка завдань",
  staff_management:    "Управління командою",
  profile_management:  "Профіль компанії",
  client_management:   "Клієнти",
};

export const ALL_SUPPLIER_PERMISSIONS = Object.keys(SUPPLIER_PERMISSIONS);

/** Merge role permissions + per-user override ({ key: true = grant, false = deny }) */
export function resolveSupplierPermissions(
  basePermissions: string[],
  override?: Record<string, boolean> | null
): string[] {
  if (!override) return basePermissions;
  const set = new Set(basePermissions);
  for (const [key, granted] of Object.entries(override)) {
    if (granted) set.add(key);
    else set.delete(key);
  }
  return ALL_SUPPLIER_PERMISSIONS.filter((p) => set.has(p));
}
