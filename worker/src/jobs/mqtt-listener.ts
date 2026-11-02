import mqtt from "mqtt";
import { Queue } from "bullmq";
import { redisConnection } from "../redis";
import { db } from "../db";
import { ingestSensorMessage } from "../services/iot-ingestion";
import { OFFLINE_AFTER_MINUTES } from "../services/iot-rules";
import { sensorMessageHandler } from "../services/iot-mqtt-delivery";
import { readIotErpOptions } from "../services/iot-erp";

const MQTT_URL = process.env.MQTT_URL ?? "";
const TOPIC = "shelfguard/#";
const iotErp = readIotErpOptions();
const notificationQueue = new Queue("notifications", { connection: redisConnection });

// Offline-watchdog episode suppression remains in memory; sensor ingestion dedup is durable.
const offlineAlerted = new Set<string>();

async function checkOfflineDevices(): Promise<void> {
  const client = await db.connect();
  let open = false;
  let broken = false;
  try {
    await client.query("BEGIN"); open = true;
    await client.query("SET LOCAL app.role = 'worker'");
    const { rows } = await client.query<{ id: string; tenant_id: string; store_id: string; name: string | null; device_id: string }>(
      `SELECT "Id" AS id, "TenantId" AS tenant_id, "LocationId" AS store_id, "Name" AS name, "DeviceId" AS device_id
       FROM iot_devices WHERE "IsActive" = true AND "LastSeenAt" IS NOT NULL
         AND "LastSeenAt" < NOW() - make_interval(mins => $1)`, [OFFLINE_AFTER_MINUTES]);
    await client.query("COMMIT"); open = false;
    for (const d of rows) {
      if (offlineAlerted.has(d.id)) continue;
      await notificationQueue.add("iot-offline", {
        type: "iot_offline", tenantId: d.tenant_id, storeId: d.store_id, deviceId: d.id, deviceName: d.name ?? d.device_id,
      }, { attempts: 3, backoff: { type: "exponential", delay: 60_000 } });
      offlineAlerted.add(d.id);
    }
  } finally {
    if (open) { try { await client.query("ROLLBACK"); } catch { broken = true; } }
    client.release(broken);
  }
}

export function startMqttListener(): void {
  if (!MQTT_URL) { console.warn("[mqtt] MQTT_URL not set — IoT listener disabled"); return; }
  // Each replica needs its own stable id. A persistent QoS 1 session redelivers unacknowledged messages.
  const mqttClient = mqtt.connect(MQTT_URL, {
    reconnectPeriod: 5_000, clean: false,
    clientId: process.env.MQTT_CLIENT_ID ?? "shelfguard-iot-worker",
  });
  mqttClient.on("connect", () => {
    mqttClient.subscribe(TOPIC, { qos: 1 }, (err) => {
      if (err) console.error("[mqtt] subscribe failed:", err.message);
      else console.log(`[mqtt] connected, subscribed to ${TOPIC}`);
    });
  });
  // mqtt.js sends PUBACK only after this callback succeeds. Do not also process the 'message' event.
  mqttClient.handleMessage = sensorMessageHandler(
    (topic, payload) => ingestSensorMessage(db, topic, payload, iotErp),
    (error) => mqttClient.stream.destroy(error),
    (result) => { if (result.deviceId && result.status === "created") offlineAlerted.delete(result.deviceId); },
  );
  mqttClient.on("error", (err) => console.error("[mqtt] connection error:", err.message));
  setInterval(() => {
    checkOfflineDevices().catch((e) => console.error("[mqtt] offline check failed:", e instanceof Error ? e.message : e));
  }, 15 * 60_000);
}
