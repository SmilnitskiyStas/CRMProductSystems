import type { PoolClient } from "pg";

export type IotErpOptions = { enabled: boolean; scopes: ReadonlyArray<{ tenantId: string; locationId: string }> };
const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
/** Explicit opt-in only. A malformed deployment setting must never select the legacy writer. */
export function readIotErpOptions(env: NodeJS.ProcessEnv = process.env): IotErpOptions {
  const enabled = env.ERP_IOT_ENABLED ?? "false";
  if (!["true", "false"].includes(enabled)) throw new Error("ERP_IOT_ENABLED must be true or false");
  const scopes: unknown = JSON.parse(env.ERP_IOT_SCOPES ?? "[]");
  if (!Array.isArray(scopes) || scopes.some(s => !s || typeof s !== "object"
    || typeof s.tenantId !== "string" || !uuid.test(s.tenantId)
    || typeof s.locationId !== "string" || !uuid.test(s.locationId))) throw new Error("Invalid ERP_IOT_SCOPES");
  return { enabled: enabled === "true", scopes: scopes.map(s => ({ tenantId: s.tenantId.toLowerCase(), locationId: s.locationId.toLowerCase() })) };
}

/** Runs on the ingestion connection, never an HTTP call with an independent commit. */
export async function erpWeightRoute(client: PoolClient, tenant: string, location: string, options: IotErpOptions): Promise<"legacy" | "disabled" | "enabled" | "missing_schema"> {
  const declared = options.scopes.some(s => s.tenantId === tenant && s.locationId === location);
  const registry = (await client.query<{ present: boolean }>("SELECT to_regclass('public.inventory_posting_scopes') IS NOT NULL AS present")).rows[0].present;
  const fenced = registry && (await client.query<{ present: boolean }>(
    `SELECT EXISTS(SELECT 1 FROM public.inventory_posting_scopes WHERE "TenantId"=$1 AND "LocationId"=$2) AS present`, [tenant, location])).rows[0].present;
  if (!fenced && !declared) return "legacy";
  const schema = (await client.query<{ present: boolean }>("SELECT to_regprocedure('public.erp_w4_integrated_ready(uuid,uuid)') IS NOT NULL AND to_regprocedure('public.erp_w4_iot_ingest_weight(uuid,text,boolean)') IS NOT NULL AS present")).rows[0].present;
  if (!schema) return "missing_schema";
  return options.enabled && declared ? "enabled" : "disabled";
}
