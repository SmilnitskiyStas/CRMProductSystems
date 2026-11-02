import type { PoolClient } from "pg";

/** Fences never expire: keep their audit events and batches available for exact returns. */
export async function inventoryRetentionPredicate(client: PoolClient, column: "LocationId" | "StoreId"): Promise<string> {
  const registry = await client.query("SELECT to_regclass('public.inventory_posting_scopes') IS NOT NULL AS present");
  return registry.rows[0].present
    ? ` AND NOT EXISTS (SELECT 1 FROM public.inventory_posting_scopes f
        WHERE f."TenantId" = retained."TenantId" AND f."LocationId" = retained."${column}")`
    : "";
}
