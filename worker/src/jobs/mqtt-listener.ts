import mqtt from "mqtt";
import type { PoolClient } from "pg";
import { Queue } from "bullmq";
import { redisConnection } from "../redis";
import { db } from "../db";
import {
  AUTO_WRITE_DOWN_MIN_CONFIDENCE,
  OFFLINE_AFTER_MINUTES,
  TEMP_VIOLATION_HOURS,
  assessWeightDelta,
  isTempAlert,
  parseDeviceConfig,
  tempAlertThreshold,
} from "../services/iot-rules";

const MQTT_URL = process.env.MQTT_URL ?? "";
const TOPIC = "shelfguard/#";

const notificationQueue = new Queue("notifications", { connection: redisConnection });

type DeviceRow = {
  id: string;
  tenant_id: string;
  store_id: string;
  zone_id: string | null;
  device_type: string;
  config: unknown; // jsonb — pg returns it as a parsed object
};

// v3-spec §1/§4 payloads. Sensors send either weight or temperature fields.
type SensorPayload = {
  device_id: string;
  weight_before?: number;
  weight_after?: number;
  delta?: number;
  temperature?: number;
  humidity?: number;
  battery?: number;
  timestamp?: string;
};

// ── Message handling ───────────────────────────────────────────────────────

async function handleMessage(rawTopic: string, raw: Buffer): Promise<void> {
  let payload: SensorPayload;
  try {
    payload = JSON.parse(raw.toString()) as SensorPayload;
  } catch {
    console.warn(`[mqtt] non-JSON message on ${rawTopic} — ignored`);
    return;
  }
  if (!payload.device_id) {
    console.warn(`[mqtt] message without device_id on ${rawTopic} — ignored`);
    return;
  }

  const client = await db.connect();
  try {
    await client.query("SET app.role = 'worker'");

    const deviceRes = await client.query<DeviceRow>(
      `SELECT "Id" AS id, "TenantId" AS tenant_id, "StoreId" AS store_id,
              "ZoneId" AS zone_id, "DeviceType" AS device_type, "Config" AS config
       FROM iot_devices
       WHERE "DeviceId" = $1 AND "IsActive" = true
       LIMIT 1`,
      [payload.device_id]
    );
    if (deviceRes.rows.length === 0) {
      console.warn(`[mqtt] unknown device '${payload.device_id}' — ignored`);
      return;
    }
    const device = deviceRes.rows[0];
    const recordedAt = payload.timestamp ?? new Date().toISOString();

    await client.query(
      `UPDATE iot_devices
       SET "LastSeenAt" = NOW(),
           "BatteryLevel" = COALESCE($2, "BatteryLevel")
       WHERE "Id" = $1`,
      [device.id, payload.battery ?? null]
    );
    offlineAlerted.delete(device.id); // device is talking again

    if (device.device_type === "temp_sensor" && typeof payload.temperature === "number") {
      await handleTemperature(client, device, payload, recordedAt);
    } else if (device.device_type === "weight_sensor" && typeof payload.delta === "number") {
      await handleWeight(client, device, payload, recordedAt);
    }
  } finally {
    client.release();
  }
}

// ── Temperature (v3-spec §4) ───────────────────────────────────────────────

async function handleTemperature(
  client: PoolClient,
  device: DeviceRow,
  payload: SensorPayload,
  recordedAt: string
): Promise<void> {
  const config = parseDeviceConfig(device.config);
  const threshold = tempAlertThreshold(config);
  const alert = isTempAlert(payload.temperature!, threshold);

  await client.query(
    `INSERT INTO temperature_readings
       ("DeviceId", "StoreId", "ZoneId", "Temperature", "Humidity", "IsAlert", "RecordedAt")
     VALUES ($1, $2, $3, $4, $5, $6, $7)`,
    [device.id, device.store_id, device.zone_id, payload.temperature, payload.humidity ?? null, alert, recordedAt]
  );

  if (!alert) return;

  await notificationQueue.add(
    "temp-alert",
    {
      type: "temp_alert",
      tenantId: device.tenant_id,
      storeId: device.store_id,
      zoneId: device.zone_id,
      deviceId: device.id,
      temperature: payload.temperature,
      threshold,
    },
    { attempts: 3, backoff: { type: "exponential", delay: 60_000 } }
  );

  // Sustained violation: every reading in the last 2h window was an alert
  // (and the window actually spans 2h) → flag all batches in the zone.
  const windowRes = await client.query<{ total: string; alerts: string; oldest: string }>(
    `SELECT COUNT(*) AS total,
            COUNT(*) FILTER (WHERE "IsAlert") AS alerts,
            MIN("RecordedAt")::text AS oldest
     FROM temperature_readings
     WHERE "DeviceId" = $1 AND "RecordedAt" >= NOW() - make_interval(hours => $2)`,
    [device.id, TEMP_VIOLATION_HOURS]
  );
  const w = windowRes.rows[0];
  const sustained =
    Number(w.total) >= 2 &&
    Number(w.total) === Number(w.alerts) &&
    Date.now() - new Date(w.oldest).getTime() >= (TEMP_VIOLATION_HOURS - 0.1) * 3_600_000;

  if (sustained && device.zone_id) {
    const flagged = await client.query(
      `UPDATE product_stock
       SET "Status" = 'temp_violation'
       WHERE "ZoneId" = $1 AND "Quantity" > 0
         AND "Status" NOT IN ('temp_violation', 'expired', 'sold_out', 'archived')`,
      [device.zone_id]
    );
    if ((flagged.rowCount ?? 0) > 0) {
      await client.query(
        `INSERT INTO stock_events
           ("TenantId", "EventType", "StoreId", "SourceDeviceId", "Confidence", "Meta")
         VALUES ($1, 'temp_violation', $2, $3, 100, $4::jsonb)`,
        [
          device.tenant_id,
          device.store_id,
          payload.device_id,
          JSON.stringify({ zoneId: device.zone_id, temperature: payload.temperature, threshold, batches: flagged.rowCount }),
        ]
      );
      console.log(`[mqtt] temp violation: zone ${device.zone_id} — ${flagged.rowCount} batches flagged`);
    }
  }
}

// ── Weight (v3-spec §1) ────────────────────────────────────────────────────

async function handleWeight(
  client: PoolClient,
  device: DeviceRow,
  payload: SensorPayload,
  recordedAt: string
): Promise<void> {
  const config = parseDeviceConfig(device.config);
  const unitWeight = config.unit_weight_grams ?? 0;
  const { units, confidence } = assessWeightDelta(payload.delta!, unitWeight);

  const confident =
    confidence >= AUTO_WRITE_DOWN_MIN_CONFIDENCE && payload.delta! < 0 && !!config.product_id;

  await client.query(
    `INSERT INTO weight_readings
       ("DeviceId", "ZoneId", "WeightBefore", "WeightAfter", "DeltaWeight", "Processed", "Confidence", "RecordedAt")
     VALUES ($1, $2, $3, $4, $5, $6, $7, $8)`,
    [
      device.id,
      device.zone_id,
      payload.weight_before ?? null,
      payload.weight_after ?? null,
      payload.delta,
      confident,
      confidence,
      recordedAt,
    ]
  );

  // Every sensor delta is logged for analysis regardless of confidence (v3-spec §1)
  await client.query(
    `INSERT INTO stock_events
       ("TenantId", "EventType", "ProductId", "StoreId", "SourceDeviceId", "QuantityDelta", "Confidence", "Meta")
     VALUES ($1, 'sensor', $2, $3, $4, $5, $6, $7::jsonb)`,
    [
      device.tenant_id,
      config.product_id ?? null,
      device.store_id,
      payload.device_id,
      confident ? -units : null,
      confidence,
      JSON.stringify({ delta: payload.delta, unitWeight, units }),
    ]
  );

  if (!confident) return;

  // FEFO write-down: consume from batches with the nearest expiry first
  let remaining = units;
  const batches = await client.query<{ id: string; quantity: number }>(
    `SELECT "Id" AS id, "Quantity" AS quantity
     FROM product_stock
     WHERE "TenantId" = $1 AND "StoreId" = $2 AND "ProductId" = $3
       AND "Quantity" > 0 AND "Status" NOT IN ('sold_out', 'archived')
     ORDER BY "ExpiryDate" ASC
     FOR UPDATE`,
    [device.tenant_id, device.store_id, config.product_id]
  );

  for (const batch of batches.rows) {
    if (remaining <= 0) break;
    const take = Math.min(Number(batch.quantity), remaining);
    await client.query(
      `UPDATE product_stock
       SET "Quantity" = "Quantity" - $1,
           "Status" = CASE WHEN "Quantity" - $1 <= 0 THEN 'sold_out' ELSE "Status" END
       WHERE "Id" = $2`,
      [take, batch.id]
    );
    remaining -= take;
  }

  console.log(
    `[mqtt] sensor write-down: device ${payload.device_id} — ${units} units (confidence ${confidence})` +
      (remaining > 0 ? `, ${remaining} units not covered by stock` : "")
  );
}

// ── Offline watchdog (v3-spec §4: silent > 30 min → alert) ────────────────

// In-memory dedup: alert once per offline episode; reset when the device talks again.
const offlineAlerted = new Set<string>();

async function checkOfflineDevices(): Promise<void> {
  const client = await db.connect();
  try {
    await client.query("SET app.role = 'worker'");
    const { rows } = await client.query<{ id: string; tenant_id: string; store_id: string; name: string | null; device_id: string }>(
      `SELECT "Id" AS id, "TenantId" AS tenant_id, "StoreId" AS store_id, "Name" AS name, "DeviceId" AS device_id
       FROM iot_devices
       WHERE "IsActive" = true
         AND "LastSeenAt" IS NOT NULL
         AND "LastSeenAt" < NOW() - make_interval(mins => $1)`,
      [OFFLINE_AFTER_MINUTES]
    );

    for (const d of rows) {
      if (offlineAlerted.has(d.id)) continue;
      offlineAlerted.add(d.id);
      await notificationQueue.add(
        "iot-offline",
        {
          type: "iot_offline",
          tenantId: d.tenant_id,
          storeId: d.store_id,
          deviceId: d.id,
          deviceName: d.name ?? d.device_id,
        },
        { attempts: 3, backoff: { type: "exponential", delay: 60_000 } }
      );
    }
    if (rows.length > 0) console.log(`[mqtt] offline devices: ${rows.length}`);
  } finally {
    client.release();
  }
}

// ── Entry point ────────────────────────────────────────────────────────────

export function startMqttListener(): void {
  if (!MQTT_URL) {
    console.warn("[mqtt] MQTT_URL not set — IoT listener disabled");
    return;
  }

  const mqttClient = mqtt.connect(MQTT_URL, { reconnectPeriod: 5_000 });

  mqttClient.on("connect", () => {
    mqttClient.subscribe(TOPIC, (err) => {
      if (err) console.error("[mqtt] subscribe failed:", err.message);
      else console.log(`[mqtt] connected, subscribed to ${TOPIC}`);
    });
  });

  mqttClient.on("message", (topic, message) => {
    handleMessage(topic, message).catch((e) =>
      console.error(`[mqtt] message handling failed (${topic}):`, e instanceof Error ? e.message : e)
    );
  });

  mqttClient.on("error", (err) => console.error("[mqtt] connection error:", err.message));

  setInterval(() => {
    checkOfflineDevices().catch((e) =>
      console.error("[mqtt] offline check failed:", e instanceof Error ? e.message : e)
    );
  }, 15 * 60_000);
}
