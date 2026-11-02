import { createHash, randomUUID } from "node:crypto";
import type { Pool, PoolClient } from "pg";
import { erpWeightRoute, readIotErpOptions, type IotErpOptions } from "./iot-erp";
import {
  assessWeightDelta, AUTO_WRITE_DOWN_MIN_CONFIDENCE, isPlausibleHumidity,
  isPlausibleTemperature, isTempAlert, parseDeviceConfig, tempAlertThreshold, TEMP_VIOLATION_HOURS,
} from "./iot-rules";

const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const sha256 = (value: string) => createHash("sha256").update(value).digest("hex");
type SensorPayload = {
  device_id: string;
  event_id: string | null;
  timestamp: string | null;
  weight_before: number | null;
  weight_after: number | null;
  delta: number | null;
  temperature: number | null;
  humidity: number | null;
  battery: number | null;
};
export type SensorMessage = {
  tenantId: string; storeId: string; payload: SensorPayload; messageKey: string; payloadHash: string;
};
type Device = { id: string; tenant_id: string; store_id: string; store_name: string; zone_id: string | null; device_type: string; config: unknown; pos_primary: boolean };
export type IngestionResult = { status: "created" | "duplicate" | "conflict" | "ignored"; deviceId?: string };

/** Explicit producer identity preferred; timestamp fallback never uses broker packet ids or arrival time. */
export function parseSensorMessage(topic: string, raw: Buffer, now = Date.now()): SensorMessage | null {
  const [prefix, tenant, store] = topic.split("/");
  if (prefix !== "shelfguard" || !UUID.test(tenant ?? "") || !UUID.test(store ?? "") || raw.length > 16_384) return null;
  let data: Record<string, unknown>;
  try {
    const value: unknown = JSON.parse(raw.toString("utf8"));
    if (!value || typeof value !== "object" || Array.isArray(value)) return null;
    data = value as Record<string, unknown>;
  } catch { return null; }
  if (typeof data.device_id !== "string" || !data.device_id.trim() || data.device_id.length > 100) return null;
  if (data.event_id != null && (typeof data.event_id !== "string" || !data.event_id.trim() || data.event_id.length > 128)) return null;
  let timestamp: string | null = null;
  if (data.timestamp != null) {
    if (typeof data.timestamp !== "string" || !/^\d{4}-\d{2}-\d{2}T.*(?:Z|[+-]\d{2}:\d{2})$/.test(data.timestamp)) return null;
    const at = Date.parse(data.timestamp);
    if (!Number.isFinite(at) || at > now + 5 * 60_000) return null;
    timestamp = new Date(at).toISOString();
  }
  if (!data.event_id && !timestamp) return null; // no durable identity: fail closed before effects
  for (const field of ["weight_before", "weight_after", "delta", "temperature", "humidity", "battery"])
    if (data[field] != null && (typeof data[field] !== "number" || !Number.isFinite(data[field]))) return null;
  const payload: SensorPayload = {
    device_id: data.device_id.trim(), event_id: (data.event_id as string | undefined)?.trim() ?? null, timestamp,
    weight_before: data.weight_before as number ?? null, weight_after: data.weight_after as number ?? null,
    delta: data.delta as number ?? null, temperature: data.temperature as number ?? null,
    humidity: data.humidity as number ?? null, battery: data.battery as number ?? null,
  };
  if (payload.battery !== null && (!Number.isInteger(payload.battery) || payload.battery < 0 || payload.battery > 100)) return null;
  if (payload.temperature !== null && !isPlausibleTemperature(payload.temperature)) return null;
  if (!isPlausibleHumidity(payload.humidity)) return null;
  for (const weight of [payload.weight_before, payload.weight_after, payload.delta])
    if (weight !== null && Math.abs(weight) > 99_999_999.99) return null;
  if ((payload.weight_before ?? 0) < 0 || (payload.weight_after ?? 0) < 0) return null;
  if (payload.weight_before !== null && payload.weight_after !== null && payload.delta !== null
    && Math.abs(payload.weight_after - payload.weight_before - payload.delta) > 0.02) return null;
  if (payload.delta === null && payload.temperature === null) return null;
  return {
    tenantId: tenant.toLowerCase(), storeId: store.toLowerCase(), payload,
    messageKey: sha256(payload.event_id ? "id:" + payload.event_id : "time:" + timestamp),
    payloadHash: sha256(JSON.stringify(payload)),
  };
}

/** One connection and transaction cover inbox, telemetry, balances, audit movements and alert intent. */
export async function ingestSensorMessage(pool: Pool, topic: string, raw: Buffer, erp: IotErpOptions = readIotErpOptions()): Promise<IngestionResult> {
  const message = parseSensorMessage(topic, raw);
  if (!message) return { status: "ignored" };
  const { payload, tenantId, storeId } = message;
  const client = await pool.connect();
  let open = false;
  let broken = false;
  try {
    await client.query("BEGIN"); open = true;
    await client.query("SET LOCAL app.role = 'worker'");
    await client.query("SELECT set_config('app.tenant_id',$1,true),set_config('app.user_id','',true),set_config('app.erp_posting_id','',true)", [tenantId]);
    await client.query("SET LOCAL lock_timeout='5s'; SET LOCAL statement_timeout='30s'");
    // DeviceId is unique only WITHIN a tenant. Serialize that device's inbox and configuration.
    const deviceResult = await client.query<Device>(
      `SELECT d."Id" AS id, d."TenantId" AS tenant_id, d."LocationId" AS store_id,
              l."Name" AS store_name, d."ZoneId" AS zone_id, d."DeviceType" AS device_type, d."Config" AS config, t."Modules" @> '["pos"]'::jsonb AS pos_primary
       FROM iot_devices d
       JOIN locations l ON l."Id" = d."LocationId" AND l."TenantId" = d."TenantId" AND l."IsActive"
       JOIN tenants t ON t."Id"=d."TenantId" AND t."IsActive"
       WHERE d."DeviceId" = $1 AND d."TenantId" = $2 AND d."LocationId" = $3 AND d."IsActive"
         AND (d."MqttTopic" IS NULL OR d."MqttTopic" = '' OR d."MqttTopic" = $4)
         AND (d."ZoneId" IS NULL OR EXISTS (
           SELECT 1 FROM location_zones z WHERE z."Id" = d."ZoneId" AND z."LocationId" = d."LocationId"))
         AND (d."ZoneId" IS NULL OR lower($5)=d."ZoneId"::text)
       FOR UPDATE OF d`, [payload.device_id, tenantId, storeId, topic, topic.split("/")[3] ?? ""]);
    const device = deviceResult.rows[0];
    if (!device || (device.device_type !== "weight_sensor" && device.device_type !== "temp_sensor")
      || (device.device_type === "weight_sensor" ? payload.delta === null : payload.temperature === null)) {
      await client.query("ROLLBACK"); open = false;
      return { status: "ignored" };
    }
    const id = randomUUID();
    const inserted = await client.query<{ recorded_at: Date }>(
      `INSERT INTO iot_message_receipts ("Id", "TenantId", "DeviceId", "MessageKey", "PayloadHash", "RecordedAt")
       VALUES ($1,$2,$3,$4,$5,COALESCE($6::timestamptz,NOW()))
       ON CONFLICT ("TenantId", "DeviceId", "MessageKey") DO NOTHING RETURNING "RecordedAt" AS recorded_at`,
      [id, tenantId, device.id, message.messageKey, message.payloadHash, payload.timestamp]);
    if (!inserted.rowCount) {
      const original = await client.query<{ hash: string }>(
        `SELECT "PayloadHash" AS hash FROM iot_message_receipts WHERE "TenantId"=$1 AND "DeviceId"=$2 AND "MessageKey"=$3`,
        [tenantId, device.id, message.messageKey]);
      await client.query("ROLLBACK"); open = false;
      return { status: original.rows[0]?.hash === message.payloadHash ? "duplicate" : "conflict", deviceId: device.id };
    }
    const recordedAt = inserted.rows[0].recorded_at.toISOString();
    await client.query(
      `UPDATE iot_devices SET "LastSeenAt"=NOW(), "BatteryLevel"=COALESCE($2,"BatteryLevel") WHERE "Id"=$1`,
      [device.id, payload.battery]);
    if (device.device_type === "weight_sensor") await ingestWeight(client, device, payload, recordedAt, id, erp);
    else await ingestTemperature(client, device, payload, recordedAt);
    await client.query("COMMIT"); open = false;
    return { status: "created", deviceId: device.id };
  } catch (error) {
    if (open) {
      try { await client.query("ROLLBACK"); }
      catch { broken = true; }
    }
    throw error;
  } finally {
    client.release(broken);
  }
}

async function ingestWeight(client: PoolClient, device: Device, payload: SensorPayload, at: string, receiptId: string, erp: IotErpOptions): Promise<void> {
  const route = await erpWeightRoute(client, device.tenant_id, device.store_id, erp);
  if (route !== "legacy" && route !== "missing_schema") {
    await client.query("SELECT public.erp_w4_iot_ingest_weight($1,$2,$3)", [receiptId, JSON.stringify(payload), route === "enabled"]);
    return;
  }
  const config = parseDeviceConfig(device.config);
  const unitWeight = typeof config.unit_weight_grams === "number" && Number.isFinite(config.unit_weight_grams)
    ? config.unit_weight_grams : 0;
  const { units, confidence } = assessWeightDelta(payload.delta!, unitWeight);
  const productId = typeof config.product_id === "string" && UUID.test(config.product_id) ? config.product_id : null;
  const product = productId ? await client.query(
    `SELECT "Id" FROM items WHERE "Id"=$1 AND "TenantId"=$2`, [productId, device.tenant_id]) : null;
  const validProduct = !!product?.rowCount;
  const confident = confidence >= AUTO_WRITE_DOWN_MIN_CONFIDENCE && payload.delta! < 0 && validProduct;
  let applied = false;
  if (confident && route === "legacy" && !device.pos_primary) {
    const batches = await client.query<{ id: string; quantity: string; zone: string | null }>(
      `SELECT "Id" AS id, "Quantity" AS quantity, "ZoneId" AS zone FROM product_stock
       WHERE "TenantId"=$1 AND "LocationId"=$2 AND "ProductId"=$3
         AND ($4::uuid IS NULL OR "ZoneId"=$4) AND "Quantity">0
         AND "ExpiryDate">(NOW() AT TIME ZONE 'UTC')::date
         AND "Status" NOT IN ('sold_out','archived','expired','temp_violation')
       ORDER BY "ExpiryDate", "Id" FOR UPDATE`,
      [device.tenant_id, device.store_id, productId, device.zone_id]);
    // Don't claim an entire sensor delta was applied after silently consuming only some stock.
    if (batches.rows.reduce((sum, batch) => sum + Math.round(Number(batch.quantity) * 100), 0) >= units * 100) {
      let remaining = units * 100;
      for (const batch of batches.rows) {
        if (remaining === 0) break;
        const before = Math.round(Number(batch.quantity) * 100);
        const take = Math.min(before, remaining); const after = before - take;
        await client.query(
          `UPDATE product_stock SET "Quantity"=$1::numeric, "Status"=CASE WHEN $1::numeric=0 THEN 'sold_out' ELSE "Status" END
           WHERE "Id"=$2 AND "TenantId"=$3`, [after / 100, batch.id, device.tenant_id]);
        await client.query(
          `INSERT INTO stock_movements ("TenantId","MovementType","ProductStockId","ProductId",
            "FromLocationId","FromZoneId","Quantity","QuantityBefore","QuantityAfter","ReferenceId","ReferenceType","Notes")
           VALUES ($1,'adjustment',$2,$3,$4,$5,$6,$7,$8,$9,'iot_sensor',$10)`,
          [device.tenant_id, batch.id, productId, device.store_id, batch.zone, take / 100, before / 100, after / 100,
            receiptId, "IoT device: " + payload.device_id]);
        remaining -= take;
      }
      applied = true;
    }
  }
  await client.query(
    `INSERT INTO weight_readings ("DeviceId","ZoneId","WeightBefore","WeightAfter","DeltaWeight","Processed","Confidence","RecordedAt")
     VALUES ($1,$2,$3,$4,$5,$6,$7,$8)`,
    [device.id, device.zone_id, payload.weight_before, payload.weight_after, payload.delta, applied, confidence, at]);
  await client.query(
    `INSERT INTO stock_events ("TenantId","EventType","ProductId","StoreId","SourceDeviceId","QuantityDelta","Confidence","Meta")
     VALUES ($1,'sensor',$2,$3,$4,$5,$6,$7::jsonb)`,
    [device.tenant_id, validProduct ? productId : null, device.store_id, payload.device_id,
      applied ? -units : confident ? 0 : null, confidence,
      JSON.stringify({ ingestionId: receiptId, delta: payload.delta, unitWeight, units, applied,
        requiresReview: !applied, payload,
        reason: route === "missing_schema" ? "erp_schema_required" : device.pos_primary ? "pos_observation" : applied ? "applied" : confident ? "insufficient_eligible_stock" : "not_eligible_for_automatic_write_down" })]);
}

async function ingestTemperature(client: PoolClient, device: Device, payload: SensorPayload, at: string): Promise<void> {
  const threshold = tempAlertThreshold(parseDeviceConfig(device.config));
  const alert = isTempAlert(payload.temperature!, threshold);
  await client.query(
    `INSERT INTO temperature_readings ("DeviceId","LocationId","ZoneId","Temperature","Humidity","IsAlert","RecordedAt")
     VALUES ($1,$2,$3,$4,$5,$6,$7)`, [device.id, device.store_id, device.zone_id, payload.temperature, payload.humidity, alert, at]);
  if (!alert) return;
  const title = `Температурний алерт: ${payload.temperature}°C (поріг ${threshold}°C) — ${device.store_name}`.slice(0, 255);
  await client.query(
    `INSERT INTO notification_queue ("TenantId","LocationId","Channel","EventType","Title","Payload","Status","RetryCount","IsRead")
     VALUES ($1,$2,'system','iot.temp_alert',$3,$4::jsonb,'pending',0,false)`,
    [device.tenant_id, device.store_id, title, JSON.stringify({ deviceId: device.id, zoneId: device.zone_id,
      temperature: payload.temperature, threshold })]);
  const result = await client.query<{ total: string; alerts: string; span: number }>(
    `SELECT COUNT(*) AS total, COUNT(*) FILTER (WHERE "IsAlert") AS alerts,
       EXTRACT(EPOCH FROM (NOW()-MIN("RecordedAt"))) AS span
     FROM temperature_readings WHERE "DeviceId"=$1 AND "RecordedAt">=NOW()-make_interval(hours=>$2)
       AND "RecordedAt"<=NOW()`, [device.id, TEMP_VIOLATION_HOURS]);
  const window = result.rows[0];
  if (device.zone_id && Number(window.total) >= 2 && window.total === window.alerts
    && Number(window.span) >= (TEMP_VIOLATION_HOURS - 0.1) * 3600) {
    const flagged = await client.query(
      `UPDATE product_stock SET "Status"='temp_violation'
       WHERE "TenantId"=$1 AND "LocationId"=$2 AND "ZoneId"=$3 AND "Quantity">0
         AND "ExpiryDate">(NOW() AT TIME ZONE 'UTC')::date
         AND "Status" NOT IN ('temp_violation','expired','sold_out','archived')`,
      [device.tenant_id, device.store_id, device.zone_id]);
    if (flagged.rowCount) await client.query(
      `INSERT INTO stock_events ("TenantId","EventType","StoreId","SourceDeviceId","Confidence","Meta")
       VALUES ($1,'temp_violation',$2,$3,100,$4::jsonb)`,
      [device.tenant_id, device.store_id, payload.device_id,
        JSON.stringify({ zoneId: device.zone_id, temperature: payload.temperature, threshold, batches: flagged.rowCount })]);
  }
}
