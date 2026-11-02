import assert from "node:assert/strict";
import { randomUUID } from "node:crypto";
import { after, test } from "node:test";
import { Pool } from "pg";
import mqtt, { type MqttClient } from "mqtt";
import { ingestSensorMessage } from "./iot-ingestion";
import { sensorMessageHandler } from "./iot-mqtt-delivery";
import type { IotErpOptions } from "./iot-erp";
import { simulatedReading, publishSimulation } from "../tools/iot-simulator";
import { updateBatchExpirySql } from "./batch-expiry";

// Provision with the backend posting fixture (the actual EF model), never a running application DB.
const connection = process.env.SHELFGUARD_IOT_TEST_DB;
const broker = process.env.SHELFGUARD_IOT_TEST_MQTT;
if (broker && (!connection || !["localhost", "127.0.0.1"].includes(new URL(broker).hostname)))
  throw new Error("MQTT acceptance requires a local disposable broker and explicit disposable database.");
if (connection && !new URL(connection).pathname.startsWith("/erm_posting_test_"))
  throw new Error("IoT tests require an explicit disposable erm_posting_test_* database.");
const pool = connection ? new Pool({ connectionString: connection, max: 8, connectionTimeoutMillis: 5000 }) : null;
after(async () => { await pool?.end(); });
const pgTest = (name: string, run: () => Promise<void>) => test(name, { skip: !connection }, run);
const query = (sql: string, values?: unknown[]) => pool!.query(sql, values);
function connected(client: MqttClient): Promise<void> {
  return new Promise((resolve, reject) => {
    client.once("error", reject);
    client.once("connect", () => { client.off("error", reject); resolve(); });
  });
}
type Seed = { tenant: string; store: string; zone: string; product: string; device: string; physical: string; early: string; late: string };
async function seed(type = "weight_sensor", physical = "scale-" + randomUUID()): Promise<Seed> {
  const t = { tenant: randomUUID(), store: randomUUID(), zone: randomUUID(), product: randomUUID(),
    device: randomUUID(), physical, early: randomUUID(), late: randomUUID() };
  await query(`INSERT INTO tenants ("Id","Name","Slug") VALUES ($1,'Synthetic IoT',$2)`, [t.tenant, "iot-" + t.tenant]);
  await query(`INSERT INTO locations ("Id","TenantId","Name","Type") VALUES ($1,$2,'Synthetic IoT store','shop')`, [t.store, t.tenant]);
  await query(`INSERT INTO location_zones ("Id","LocationId","Name","Type") VALUES ($1,$2,'Synthetic zone','shelf')`, [t.zone, t.store]);
  await query(`INSERT INTO items ("Id","TenantId","Name","Barcodes","MinStock","MaxStock","SafetyBuffer")
    VALUES ($1,$2,'Synthetic item','[]',0,0,0)`, [t.product, t.tenant]);
  await query(`INSERT INTO iot_devices ("Id","TenantId","LocationId","ZoneId","DeviceType","DeviceId","Config")
    VALUES ($1,$2,$3,$4,$5,$6,$7::jsonb)`, [t.device, t.tenant, t.store, t.zone, type, physical,
    JSON.stringify(type === "weight_sensor" ? { product_id: t.product, unit_weight_grams: 245 } : { profile: "fridge" })]);
  for (const [id, days, quantity] of [[t.early, 5, 2], [t.late, 10, 3]] as const)
    await query(`INSERT INTO product_stock ("Id","TenantId","ProductId","LocationId","ZoneId","Quantity","QuantityInitial","ExpiryDate")
      VALUES ($1,$2,$3,$4,$5,$6,$6,CURRENT_DATE+$7::int)`, [id, t.tenant, t.product, t.store, t.zone, quantity, days]);
  return t;
}
function topic(t: Seed): string { return `shelfguard/${t.tenant}/${t.store}/${t.zone}/shelf`; }
function raw(t: Seed, extra: Record<string, unknown> = {}): Buffer {
  return Buffer.from(JSON.stringify({ device_id: t.physical, event_id: "boot:" + randomUUID(), timestamp:"options" in t?new Date().toISOString():undefined, delta: -245, ...extra }));
}
async function balances(t: Seed): Promise<number[]> {
  return (await query(`SELECT "Quantity" FROM product_stock WHERE "Id"=ANY($1::uuid[]) ORDER BY "ExpiryDate"`, [[t.early, t.late]]))
    .rows.map(row => Number(row.Quantity));
}
async function count(table: string, column: string, id: string): Promise<number> {
  // Table/column identifiers are fixed test literals; id is always parameterized.
  return Number((await query(`SELECT COUNT(*) AS n FROM ${table} WHERE "${column}"=$1`, [id])).rows[0].n);
}

pgTest("FEFO consumes multiple batches with exact durable before/after movements", async () => {
  const t = await seed();
  assert.equal((await ingestSensorMessage(pool!, topic(t), raw(t, { delta: -735 }))).status, "created");
  assert.deepEqual(await balances(t), [0, 2]);
  const movement = (await query(`SELECT * FROM stock_movements WHERE "TenantId"=$1 ORDER BY "QuantityBefore"`, [t.tenant])).rows;
  assert.equal(movement.length, 2);
  assert.deepEqual(movement.map(m => [Number(m.Quantity), Number(m.QuantityBefore), Number(m.QuantityAfter)]), [[2, 2, 0], [1, 3, 2]]);
  assert.ok(movement.every(m => m.MovementType === "adjustment" && m.ReferenceType === "iot_sensor"
    && m.FromLocationId === t.store && m.FromZoneId === t.zone));
  assert.equal(new Set(movement.map(m => m.ReferenceId)).size, 1);
  assert.equal((await query(`SELECT "QuantityDelta" FROM stock_events WHERE "TenantId"=$1`, [t.tenant])).rows[0].QuantityDelta, "-3.00");
  assert.equal((await query(`SELECT "Processed" FROM weight_readings WHERE "DeviceId"=$1`, [t.device])).rows[0].Processed, true);
});
pgTest("concurrent copies of one event commit exactly once and survive pool restart", async () => {
  const t = await seed(); const bytes = raw(t);
  const results = await Promise.all(Array.from({ length: 6 }, () => ingestSensorMessage(pool!, topic(t), bytes)));
  assert.equal(results.filter(r => r.status === "created").length, 1);
  assert.equal(results.filter(r => r.status === "duplicate").length, 5);
  const restarted = new Pool({ connectionString: connection, max: 1 });
  try { assert.equal((await ingestSensorMessage(restarted, topic(t), bytes)).status, "duplicate"); }
  finally { await restarted.end(); }
  assert.deepEqual(await balances(t), [1, 3]);
  assert.equal(await count("iot_message_receipts", "DeviceId", t.device), 1);
  assert.equal(await count("weight_readings", "DeviceId", t.device), 1);
  assert.equal(await count("stock_movements", "TenantId", t.tenant), 1);
});
pgTest("legacy timestamp dedup persists after telemetry and stock-event retention cleanup", async () => {
  const t = await seed(); const message = raw(t, { event_id: undefined, timestamp: new Date().toISOString() });
  await ingestSensorMessage(pool!, topic(t), message);
  await query(`DELETE FROM weight_readings WHERE "DeviceId"=$1`, [t.device]);
  await query(`DELETE FROM stock_events WHERE "TenantId"=$1`, [t.tenant]);
  assert.equal((await ingestSensorMessage(pool!, topic(t), message)).status, "duplicate");
  assert.deepEqual(await balances(t), [1, 3]);
  assert.equal(await count("stock_movements", "TenantId", t.tenant), 1);
});
pgTest("reusing an event id with changed content is a conflict without another deduction", async () => {
  const t = await seed(); const message = raw(t, { event_id: "stable-id" });
  await ingestSensorMessage(pool!, topic(t), message);
  assert.equal((await ingestSensorMessage(pool!, topic(t), raw(t, { event_id: "stable-id", delta: -490 }))).status, "conflict");
  assert.deepEqual(await balances(t), [1, 3]);
  assert.equal(await count("iot_message_receipts", "DeviceId", t.device), 1);
});
pgTest("failure after stock writes rolls back quantities, audit, heartbeat, telemetry and inbox; retry succeeds", async () => {
  const t = await seed(); const message = raw(t, { delta: -735 });
  await query(`CREATE FUNCTION iot_test_fail_reading() RETURNS trigger LANGUAGE plpgsql AS $$
    BEGIN IF NEW."DeviceId"='${t.device}'::uuid THEN RAISE EXCEPTION 'synthetic telemetry failure'; END IF; RETURN NEW; END $$;
    CREATE TRIGGER iot_test_fail_reading BEFORE INSERT ON weight_readings FOR EACH ROW EXECUTE FUNCTION iot_test_fail_reading()`);
  try { await assert.rejects(ingestSensorMessage(pool!, topic(t), message), /synthetic telemetry failure/); }
  finally { await query("DROP TRIGGER iot_test_fail_reading ON weight_readings; DROP FUNCTION iot_test_fail_reading()"); }
  assert.deepEqual(await balances(t), [2, 3]);
  assert.equal(await count("iot_message_receipts", "DeviceId", t.device), 0);
  assert.equal(await count("weight_readings", "DeviceId", t.device), 0);
  assert.equal(await count("stock_movements", "TenantId", t.tenant), 0);
  assert.equal(await count("stock_events", "TenantId", t.tenant), 0);
  assert.equal((await query(`SELECT "LastSeenAt" FROM iot_devices WHERE "Id"=$1`, [t.device])).rows[0].LastSeenAt, null);
  assert.equal((await ingestSensorMessage(pool!, topic(t), message)).status, "created");
  assert.deepEqual(await balances(t), [0, 2]);
});
pgTest("two devices competing for the same stock serialize; insufficient delta never applies partially", async () => {
  const t = await seed(); const second = randomUUID();
  await query(`INSERT INTO iot_devices ("Id","TenantId","LocationId","ZoneId","DeviceType","DeviceId","Config")
    SELECT $1,"TenantId","LocationId","ZoneId","DeviceType",$2,"Config" FROM iot_devices WHERE "Id"=$3`, [second, "second", t.device]);
  const results = await Promise.all([
    ingestSensorMessage(pool!, topic(t), raw(t, { delta: -980 })),
    ingestSensorMessage(pool!, topic(t), raw(t, { device_id: "second", delta: -980 })),
  ]);
  assert.ok(results.every(r => r.status === "created"));
  assert.deepEqual(await balances(t), [0, 1]);
  assert.deepEqual((await query(`SELECT "QuantityDelta" FROM stock_events WHERE "TenantId"=$1 ORDER BY "QuantityDelta"`, [t.tenant]))
    .rows.map(r => Number(r.QuantityDelta)), [-4, 0]);
  assert.equal(await count("stock_movements", "TenantId", t.tenant), 2);
});
pgTest("same physical device id in two tenants is resolved from the topic namespace", async () => {
  const physical = "shared-physical-id"; const a = await seed("weight_sensor", physical); const b = await seed("weight_sensor", physical);
  await ingestSensorMessage(pool!, topic(a), raw(a));
  assert.deepEqual(await balances(a), [1, 3]); assert.deepEqual(await balances(b), [2, 3]);
  assert.equal((await ingestSensorMessage(pool!, topic(a).replace(a.store, b.store), raw(a))).status, "ignored");
  assert.equal(await count("iot_message_receipts", "DeviceId", b.device), 0);
});
pgTest("foreign product configuration and mismatched configured topic cannot mutate stock", async () => {
  const a = await seed(); const b = await seed();
  await query(`UPDATE iot_devices SET "Config"=$1::jsonb WHERE "Id"=$2`, [JSON.stringify({ product_id: b.product, unit_weight_grams: 245 }), a.device]);
  await ingestSensorMessage(pool!, topic(a), raw(a));
  assert.deepEqual(await balances(a), [2, 3]); assert.deepEqual(await balances(b), [2, 3]);
  assert.equal(await count("stock_movements", "TenantId", a.tenant), 0);
  await query(`UPDATE iot_devices SET "MqttTopic"=$1 WHERE "Id"=$2`, [topic(a) + "/different", a.device]);
  assert.equal((await ingestSensorMessage(pool!, topic(a), raw(a))).status, "ignored");
});
pgTest("FEFO excludes other zones, expired stock and temperature-quarantined batches", async () => {
  const t = await seed();
  await query(`UPDATE product_stock SET "ExpiryDate"=CURRENT_DATE-1 WHERE "Id"=$1`, [t.early]);
  await query(`UPDATE product_stock SET "Status"='temp_violation' WHERE "Id"=$1`, [t.late]);
  const otherZone = randomUUID();
  await query(`INSERT INTO location_zones ("Id","LocationId","Name","Type") VALUES ($1,$2,'Other zone','shelf')`, [otherZone, t.store]);
  await query(`INSERT INTO product_stock ("TenantId","ProductId","LocationId","ZoneId","Quantity","QuantityInitial","ExpiryDate")
    VALUES ($1,$2,$3,$4,10,10,CURRENT_DATE+2)`, [t.tenant, t.product, t.store, otherZone]);
  await ingestSensorMessage(pool!, topic(t), raw(t));
  assert.deepEqual(await balances(t), [2, 3]);
  assert.equal(await count("stock_movements", "TenantId", t.tenant), 0);
  assert.equal((await query(`SELECT "Processed" FROM weight_readings WHERE "DeviceId"=$1`, [t.device])).rows[0].Processed, false);
});
pgTest("low-confidence, positive and missing-identity events never deduct stock", async () => {
  const t = await seed();
  for (const delta of [-100, 245]) await ingestSensorMessage(pool!, topic(t), raw(t, { delta }));
  assert.equal((await ingestSensorMessage(pool!, topic(t), raw(t, { event_id: undefined }))).status, "ignored");
  assert.deepEqual(await balances(t), [2, 3]);
  assert.equal(await count("iot_message_receipts", "DeviceId", t.device), 2);
  assert.equal(await count("stock_movements", "TenantId", t.tenant), 0);
});
pgTest("temperature alert and sustained violation commit with a durable notification intent and dedup", async () => {
  const t = await seed("temp_sensor"); const foreign = await seed();
  // Deliberately inconsistent foreign row: raw worker bypass must still enforce tenant+location.
  await query(`UPDATE product_stock SET "ZoneId"=$1 WHERE "Id"=$2`, [t.zone, foreign.early]);
  await query(`INSERT INTO temperature_readings ("DeviceId","LocationId","ZoneId","Temperature","IsAlert","RecordedAt")
    VALUES ($1,$2,$3,10,true,NOW()-INTERVAL '119 minutes')`, [t.device, t.store, t.zone]);
  const message = raw(t, { delta: undefined, temperature: 10 });
  assert.equal((await ingestSensorMessage(pool!, topic(t), message)).status, "created");
  assert.equal((await ingestSensorMessage(pool!, topic(t), message)).status, "duplicate");
  assert.equal(await count("notification_queue", "TenantId", t.tenant), 1);
  assert.ok((await query(`SELECT "Title" FROM notification_queue WHERE "TenantId"=$1`, [t.tenant]))
    .rows[0].Title.includes("Synthetic IoT store"));
  assert.equal(await count("temperature_readings", "DeviceId", t.device), 2);
  const flags = (await query(`SELECT "Status" FROM product_stock WHERE "Id"=ANY($1::uuid[])`, [[t.early, t.late]])).rows;
  assert.ok(flags.every(r => r.Status === "temp_violation"));
  assert.equal((await query(`SELECT "Status" FROM product_stock WHERE "Id"=$1`, [foreign.early])).rows[0].Status, "safe");
  assert.equal(await count("stock_events", "TenantId", t.tenant), 1);
});
pgTest("notification insert failure rolls back the temperature reading and ingestion key", async () => {
  const t = await seed("temp_sensor"); const message = raw(t, { delta: undefined, temperature: 10 });
  await query(`CREATE FUNCTION iot_test_fail_alert() RETURNS trigger LANGUAGE plpgsql AS $$
    BEGIN IF NEW."TenantId"='${t.tenant}'::uuid THEN RAISE EXCEPTION 'synthetic alert failure'; END IF; RETURN NEW; END $$;
    CREATE TRIGGER iot_test_fail_alert BEFORE INSERT ON notification_queue FOR EACH ROW EXECUTE FUNCTION iot_test_fail_alert()`);
  try { await assert.rejects(ingestSensorMessage(pool!, topic(t), message), /synthetic alert failure/); }
  finally { await query("DROP TRIGGER iot_test_fail_alert ON notification_queue; DROP FUNCTION iot_test_fail_alert()"); }
  assert.equal(await count("temperature_readings", "DeviceId", t.device), 0);
  assert.equal(await count("iot_message_receipts", "DeviceId", t.device), 0);
  assert.equal((await ingestSensorMessage(pool!, topic(t), message)).status, "created");
  assert.equal(await count("notification_queue", "TenantId", t.tenant), 1);
});
pgTest("worker write bumps xmin so a stale API update cannot overwrite its quantity", async () => {
  const t = await seed();
  const old = (await query(`SELECT xmin::text AS version FROM product_stock WHERE "Id"=$1`, [t.early])).rows[0].version;
  await ingestSensorMessage(pool!, topic(t), raw(t));
  assert.equal((await query(`UPDATE product_stock SET "Quantity"=100 WHERE "Id"=$1 AND xmin=$2::xid`, [t.early, old])).rowCount, 0);
  assert.deepEqual(await balances(t), [1, 3]);
});
pgTest("transaction-local worker role is cleared on pooled connection reuse", async () => {
  const isolated = new Pool({ connectionString: connection, max: 1 });
  try {
    const t = await seed(); await ingestSensorMessage(isolated, topic(t), raw(t));
    const role = (await isolated.query("SELECT current_setting('app.role', true) AS role")).rows[0].role;
    assert.ok(role == null || role === "");
  } finally { await isolated.end(); }
});

test("real MQTT QoS 1 sends subscriber PUBACK only after the database commit", { skip: !broker, timeout: 15_000 }, async () => {
  const t = await seed(); const message = raw(t);
  let release!: () => void; let received!: () => void; let ack!: () => void;
  const gate = new Promise<void>(resolve => { release = resolve; });
  const incoming = new Promise<void>(resolve => { received = resolve; });
  const acknowledged = new Promise<void>(resolve => { ack = resolve; });
  const events: string[] = [];
  const subscriber = mqtt.connect(broker!, { clientId: "iot-test-" + randomUUID(), clean: false, reconnectPeriod: 0 });
  subscriber.on("error", () => {});
  subscriber.handleMessage = sensorMessageHandler(async (route, bytes) => {
    received(); await gate;
    const result = await ingestSensorMessage(pool!, route, bytes); events.push("commit"); return result;
  }, error => subscriber.stream.destroy(error), () => {});
  subscriber.on("packetsend", packet => { if (packet.cmd === "puback") { events.push("puback"); ack(); } });
  const publisher = mqtt.connect(broker!, { reconnectPeriod: 0 });
  try {
    await Promise.all([connected(subscriber), connected(publisher)]);
    await subscriber.subscribeAsync(topic(t), { qos: 1 });
    await publisher.publishAsync(topic(t), message, { qos: 1 });
    await incoming;
    assert.deepEqual(events, []);
    assert.equal(await count("iot_message_receipts", "DeviceId", t.device), 0);
    release(); await acknowledged;
    assert.deepEqual(events, ["commit", "puback"]);
    assert.deepEqual(await balances(t), [1, 3]);
  } finally {
    release(); await Promise.all([subscriber.endAsync(true), publisher.endAsync(true)]);
  }
});

test("real MQTT reconnect after a lost commit acknowledgment redelivers as duplicate", { skip: !broker, timeout: 15_000 }, async () => {
  const t = await seed(); const message = raw(t); const clientId = "iot-test-" + randomUUID();
  const first = mqtt.connect(broker!, { clientId, clean: false, reconnectPeriod: 0 });
  first.on("error", () => {});
  first.handleMessage = sensorMessageHandler(async (route, bytes) => {
    await ingestSensorMessage(pool!, route, bytes);
    throw new Error("synthetic disconnect after commit before PUBACK");
  }, error => first.stream.destroy(error), () => {});
  const publisher = mqtt.connect(broker!, { reconnectPeriod: 0 });
  try {
    await Promise.all([connected(first), connected(publisher)]);
    await first.subscribeAsync(topic(t), { qos: 1 });
    const closed = new Promise<void>(resolve => first.once("close", () => resolve()));
    await publisher.publishAsync(topic(t), message, { qos: 1 }); await closed;
    await first.endAsync(true);
    const second = mqtt.connect(broker!, { clientId, clean: false, reconnectPeriod: 0 });
    second.on("error", () => {});
    let result!: string; let ack!: () => void;
    const acknowledged = new Promise<void>(resolve => { ack = resolve; });
    second.handleMessage = sensorMessageHandler((route, bytes) => ingestSensorMessage(pool!, route, bytes),
      error => second.stream.destroy(error), value => { result = value.status; });
    second.on("packetsend", packet => { if (packet.cmd === "puback") ack(); });
    try {
      await acknowledged;
      assert.equal(result, "duplicate");
      assert.deepEqual(await balances(t), [1, 3]);
      assert.equal(await count("stock_movements", "TenantId", t.tenant), 1);
      assert.equal(await count("iot_message_receipts", "DeviceId", t.device), 1);
    } finally { await second.endAsync(true); }
  } finally { await Promise.all([first.endAsync(true), publisher.endAsync(true)]); }
});

// W4-J tests require the fully migrated disposable schema, including actual W2/W4 guards.
const erpTest=(name:string,run:()=>Promise<void>)=>test("ERP IoT: "+name,{skip:!connection},run);
type ErpSeed=Seed&{actor:string;unit:string;options:IotErpOptions};
async function erpSeed(cost:string|null=null):Promise<ErpSeed>{
  const t=await seed();const actor=randomUUID();
  await query(`UPDATE tenants SET "Modules"='["inventory"]'::jsonb WHERE "Id"=$1`,[t.tenant]);
  await query(`INSERT INTO users("Id","TenantId","Email","FullName","PasswordHash","Role") VALUES($1,$2,$3,'Synthetic IoT authorizer','not-a-real-password','store_manager')`,[actor,t.tenant,actor+"@test.local"]);
  await query(`INSERT INTO user_locations("TenantId","UserId","LocationId") VALUES($1,$2,$3)`,[t.tenant,actor,t.store]);
  const unit=(await query(`SELECT "Unit" FROM items WHERE "Id"=$1`,[t.product])).rows[0].Unit as string;
  await query(`UPDATE iot_devices SET "Config"=$1::jsonb WHERE "Id"=$2`,[JSON.stringify({product_id:t.product,unit_weight_grams:245,inventory_unit:unit}),t.device]);
  const doc=randomUUID(),key=randomUUID(),posting=randomUUID();const entries:Record<string,unknown>[]=[];
  const client=await pool!.connect();
  try{
    await client.query("BEGIN");
    await client.query(`INSERT INTO business_documents("Id","TenantId","DocumentKey","Version","DocumentType","SourceSystem","SourceId","Currency","CreatedBy","ContentHash","OccurredAt") VALUES($1,$2,$3,1,'opening_balance','synthetic.iot.opening',$1::uuid::text,'UAH',$4,repeat('a',64),now())`,[doc,t.tenant,key,actor]);
    for(const [index,batch] of [t.early,t.late].entries()){
      const line=randomUUID();
      await client.query(`INSERT INTO document_lines("Id","TenantId","DocumentId","LineNumber","ItemId","ToLocationId","Quantity","Unit","UnitCost","ItemNameSnapshot","ExpiryDateSnapshot") SELECT $1,$2,$3,$4,"ProductId","LocationId","Quantity",$5,$6,'Synthetic opening',"ExpiryDate" FROM product_stock WHERE "Id"=$7`,[line,t.tenant,doc,index+1,unit,cost,batch]);
      entries.push({entryId:randomUUID(),entryNumber:index+1,itemId:t.product,locationId:t.store,batchId:batch,zoneId:t.zone,quantityDelta:index===0?"2":"3",unitCost:cost,valueDelta:null,currency:"UAH",reversesEntryId:null,lineId:line});
    }
    await client.query(`UPDATE business_documents SET "Status"='submitted' WHERE "Id"=$1`,[doc]);
    await client.query(`UPDATE business_documents SET "Status"='approved' WHERE "Id"=$1`,[doc]);
    await client.query(`INSERT INTO document_postings("Id","TenantId","DocumentId","IdempotencyKey","RequestHash","PerformedBy") VALUES($1,$2,$3,$3::uuid::text,$4,$5)`,[posting,t.tenant,doc,"a".repeat(64),actor]);
    for(const e of entries){
      const v=await client.query<{value:string|null}>(`SELECT round($1::numeric*$2::numeric,4)::text value`,[e.quantityDelta,cost]);e.valueDelta=v.rows[0].value;
      await client.query(`INSERT INTO inventory_ledger_entries("Id","TenantId","DocumentId","PostingId","DocumentLineId","EntryNumber","ItemId","LocationId","BatchId","ZoneId","QuantityDelta","QuantityBefore","QuantityAfter","UnitCost","ValueDelta","Currency","ExpiryDateSnapshot","OccurredAt") SELECT $1,$2,$3,$4,$5,$6,"ProductId","LocationId","Id","ZoneId","Quantity",0,"Quantity",$7,$8,'UAH',"ExpiryDate",now() FROM product_stock WHERE "Id"=$9`,[e.entryId,t.tenant,doc,posting,e.lineId,e.entryNumber,cost,e.valueDelta,e.batchId]);
      await client.query(`INSERT INTO inventory_balance_projections("TenantId","ItemId","LocationId","BatchId","Quantity","ReservedQuantity","LastLedgerEntryId") VALUES($1,$2,$3,$4,$5,0,$6)`,[t.tenant,t.product,t.store,e.batchId,e.quantityDelta,e.entryId]);
    }
    await client.query(`INSERT INTO outbox_events("TenantId","PostingId","EventType","SchemaVersion","AggregateType","AggregateId","AggregateVersion","EventOrdinal","Payload","OccurredAt","Attempts","DeliveryScope","Status") VALUES($1,$2,'inventory.document_posted',1,'business_document',$3,1,1,$4::jsonb,now(),0,'historical','suppressed')`,[t.tenant,posting,key,JSON.stringify({documentId:doc,documentKey:key,documentVersion:1,postingId:posting,entries,reversesPostingId:null})]);
    await client.query(`UPDATE business_documents SET "Status"='posted',"PostedAt"=now() WHERE "Id"=$1`,[doc]);
    await client.query("COMMIT");
  }catch(e){await client.query("ROLLBACK");throw e;}finally{client.release();}
  await query(`INSERT INTO iot_inventory_policies("TenantId","DeviceId","Version","Enabled","ActorId","ItemId","LocationId","ZoneId","Unit","UnitWeightGrams","AccountingMode","Reason") VALUES($1,$2,1,true,$3,$4,$5,$6,$7,245,'automatic_write_down','Approved synthetic scale calibration for testing')`,[t.tenant,t.device,actor,t.product,t.store,t.zone,unit]);
  const fp=(await query(`SELECT public.erp_w4_scope_fingerprint($1,$2) fp`,[t.tenant,t.store])).rows[0].fp;
  // Only disposable fixture setup retries NOWAIT maintenance with the same identity/fingerprint.
  const activation=randomUUID();
  for(let attempt=0;;attempt++)try{await query(`SELECT public.erp_w4_activate_scope($1,$2,$3,$4,$5,'Reviewed synthetic IoT scope')`,[t.tenant,t.store,activation,actor,fp]);break;}
    catch(error){if(attempt>=2||(error as {code?:string}).code!=="55P03")throw error;await new Promise(resolve=>setTimeout(resolve,100*(attempt+1)));}
  return {...t,actor,unit,options:{enabled:true,scopes:[{tenantId:t.tenant,locationId:t.store}]}};
}
erpTest("one multi-batch transaction has exact costs, projections, telemetry, live event and replay",async()=>{
  const t=await erpSeed("12.3456");const message=raw(t,{delta:-735,event_id:"erp-stable"});
  assert.equal((await ingestSensorMessage(pool!,topic(t),message,t.options)).status,"created");
  assert.equal((await ingestSensorMessage(pool!,topic(t),message,t.options)).status,"duplicate");assert.deepEqual(await balances(t),[0,2]);
  const entries=(await query(`SELECT e.* FROM inventory_ledger_entries e JOIN business_documents d ON d."Id"=e."DocumentId" WHERE d."TenantId"=$1 AND d."SourceSystem"='iot.weight.v1' ORDER BY e."EntryNumber"`,[t.tenant])).rows;
  assert.deepEqual(entries.map(e=>e.QuantityDelta),["-2.0000","-1.0000"]);assert.deepEqual(entries.map(e=>e.ValueDelta),["-24.6912","-12.3456"]);
  assert.equal(await count("iot_message_receipts","DeviceId",t.device),1);assert.equal(await count("weight_readings","DeviceId",t.device),1);
  assert.equal((await query(`SELECT count(*) n FROM outbox_events WHERE "TenantId"=$1 AND "DeliveryScope"='live'`,[t.tenant])).rows[0].n,"1");
  for(const e of entries){const b=(await query(`SELECT * FROM inventory_balance_projections WHERE "BatchId"=$1`,[e.BatchId])).rows[0];assert.equal(b.LastLedgerEntryId,e.Id);assert.equal(b.Quantity,e.QuantityAfter);}
});
erpTest("unknown cost stays null and changed repeated content conflicts",async()=>{
  const t=await erpSeed();const message=raw(t,{event_id:"erp-null"});await ingestSensorMessage(pool!,topic(t),message,t.options);
  assert.equal((await ingestSensorMessage(pool!,topic(t),raw(t,{event_id:"erp-null",delta:-490}),t.options)).status,"conflict");
  const rows=(await query(`SELECT e."UnitCost",e."ValueDelta" FROM inventory_ledger_entries e JOIN business_documents d ON d."Id"=e."DocumentId" WHERE d."TenantId"=$1 AND d."SourceSystem"='iot.weight.v1'`,[t.tenant])).rows;
  assert.equal(rows.length,1);assert.equal(rows[0].UnitCost,null);assert.equal(rows[0].ValueDelta,null);assert.deepEqual(await balances(t),[1,3]);
});
for(const blocked of ["disabled","unlisted","paused-policy","revoked","unit","stale","missing-time","low-confidence","insufficient","projection","precision"]){
  erpTest(blocked+" preserves telemetry and never applies a partial deduction",async()=>{
    const t=await erpSeed();let options=t.options;const extra:Record<string,unknown>={};
    if(blocked==="disabled")options={...options,enabled:false};if(blocked==="unlisted")options={...options,scopes:[]};
    if(blocked==="paused-policy")await query(`INSERT INTO iot_inventory_policies("TenantId","DeviceId","Version","Enabled","ActorId","ItemId","LocationId","ZoneId","Unit","UnitWeightGrams","Reason") SELECT "TenantId","DeviceId",2,false,"ActorId","ItemId","LocationId","ZoneId","Unit","UnitWeightGrams",'Paused synthetic scale automatic posting' FROM iot_inventory_policies WHERE "DeviceId"=$1`,[t.device]);
    if(blocked==="revoked")await query(`UPDATE users SET "IsActive"=false WHERE "Id"=$1`,[t.actor]);
    if(blocked==="unit")await query(`UPDATE iot_devices SET "Config"="Config"||'{"inventory_unit":"different"}'::jsonb WHERE "Id"=$1`,[t.device]);
    if(blocked==="stale")extra.timestamp=new Date(Date.now()-3600000).toISOString();if(blocked==="low-confidence")extra.delta=-100;if(blocked==="insufficient")extra.delta=-2450;
    if(blocked==="precision")extra.delta=-245.001;
    if(blocked==="missing-time")extra.timestamp=undefined;
    if(blocked==="projection")await query(`UPDATE inventory_balance_projections SET "Quantity"=999 WHERE "BatchId"=$1`,[t.early]);
    assert.equal((await ingestSensorMessage(pool!,topic(t),raw(t,extra),options)).status,"created");assert.deepEqual(await balances(t),[2,3]);
    assert.equal((await query(`SELECT "Processed" FROM weight_readings WHERE "DeviceId"=$1`,[t.device])).rows[0].Processed,false);
    assert.equal((await query(`SELECT count(*) n FROM business_documents WHERE "TenantId"=$1 AND "SourceSystem"='iot.weight.v1'`,[t.tenant])).rows[0].n,"0");
  });
}
for(const phase of ["outbox","reading","event","projection"]){
  erpTest("failure at "+phase+" rolls back inbox, balances, audit and heartbeat",async()=>{
    const t=await erpSeed();const message=raw(t,{delta:-735});
    const table={outbox:"outbox_events",reading:"weight_readings",event:"stock_events",projection:"inventory_balance_projections"}[phase]!;
    const predicate=phase==="reading"?`NEW."DeviceId"='${t.device}'::uuid`:`NEW."TenantId"='${t.tenant}'::uuid`;
    await query(`CREATE FUNCTION iot_erp_test_fault() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF ${predicate} THEN RAISE EXCEPTION 'synthetic ERP IoT fault'; END IF;RETURN NEW;END $$;CREATE TRIGGER iot_erp_test_fault BEFORE INSERT OR UPDATE ON ${table} FOR EACH ROW EXECUTE FUNCTION iot_erp_test_fault()`);
    try{await assert.rejects(ingestSensorMessage(pool!,topic(t),message,t.options),/synthetic ERP IoT fault/);}finally{await query(`DROP TRIGGER iot_erp_test_fault ON ${table};DROP FUNCTION iot_erp_test_fault()`);}
    assert.deepEqual(await balances(t),[2,3]);assert.equal(await count("iot_message_receipts","DeviceId",t.device),0);assert.equal(await count("stock_movements","TenantId",t.tenant),0);
    assert.equal((await query(`SELECT "LastSeenAt" FROM iot_devices WHERE "Id"=$1`,[t.device])).rows[0].LastSeenAt,null);
    assert.equal((await ingestSensorMessage(pool!,topic(t),message,t.options)).status,"created");
  });
}
erpTest("concurrent identical messages create one posting",async()=>{
  const t=await erpSeed();const message=raw(t,{delta:-735});const results=await Promise.all([ingestSensorMessage(pool!,topic(t),message,t.options),ingestSensorMessage(pool!,topic(t),message,t.options)]);
  assert.deepEqual(results.map(r=>r.status).sort(),["created","duplicate"]);assert.deepEqual(await balances(t),[0,2]);
});

erpTest("a declared but unfenced scope never falls back to legacy quantity writes",async()=>{
  const t=await seed();const options={enabled:true,scopes:[{tenantId:t.tenant,locationId:t.store}]};
  await ingestSensorMessage(pool!,topic(t),raw(t),options);assert.deepEqual(await balances(t),[2,3]);
  assert.equal((await query(`SELECT "Processed" FROM weight_readings WHERE "DeviceId"=$1`,[t.device])).rows[0].Processed,false);
});
erpTest("a topic for the wrong zone is ignored without accepting an inbox identity",async()=>{
  const t=await erpSeed();assert.equal((await ingestSensorMessage(pool!,topic(t).replace(t.zone,randomUUID()),raw(t),t.options)).status,"ignored");
  assert.deepEqual(await balances(t),[2,3]);assert.equal(await count("iot_message_receipts","DeviceId",t.device),0);
});
for(const phase of ["outbox","reading","event","movement","projection"]){
  erpTest("database rejects silently omitted "+phase+" companion and rolls back",async()=>{
    const t=await erpSeed();const table={outbox:"outbox_events",reading:"weight_readings",event:"stock_events",movement:"stock_movements",projection:"inventory_balance_projections"}[phase]!;
    const predicate=phase==="reading"?`NEW."DeviceId"='${t.device}'::uuid`:`NEW."TenantId"='${t.tenant}'::uuid`;
    await query(`CREATE FUNCTION iot_erp_test_omit() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF ${predicate} THEN RETURN NULL; END IF;RETURN NEW;END $$;CREATE TRIGGER iot_erp_test_omit BEFORE INSERT OR UPDATE ON ${table} FOR EACH ROW EXECUTE FUNCTION iot_erp_test_omit()`);
    try{await assert.rejects(ingestSensorMessage(pool!,topic(t),raw(t),t.options));}finally{await query(`DROP TRIGGER iot_erp_test_omit ON ${table};DROP FUNCTION iot_erp_test_omit()`);}
    assert.deepEqual(await balances(t),[2,3]);assert.equal(await count("iot_message_receipts","DeviceId",t.device),0);
  });
}
erpTest("a runtime NOBYPASSRLS role can post but cannot authorize devices",async()=>{
  const t=await erpSeed();const role="iot_erp_"+randomUUID().replaceAll("-","");const password=randomUUID();
  await query(`CREATE ROLE ${role} LOGIN PASSWORD '${password}' NOSUPERUSER NOBYPASSRLS;GRANT USAGE ON SCHEMA public TO ${role};GRANT SELECT,INSERT,UPDATE,DELETE ON ALL TABLES IN SCHEMA public TO ${role};GRANT USAGE,SELECT ON ALL SEQUENCES IN SCHEMA public TO ${role}`);
  const url=new URL(connection!);url.username=role;url.password=password;const runtime=new Pool({connectionString:url.toString(),max:2});
  try{
    assert.equal((await ingestSensorMessage(runtime,topic(t),raw(t),t.options)).status,"created");assert.deepEqual(await balances(t),[1,3]);
    const c=await runtime.connect();try{
      await c.query("BEGIN");await c.query("SET LOCAL app.role='provider_admin'; SELECT set_config('app.tenant_id','',true)");
      assert.equal((await c.query(`SELECT count(*) n FROM iot_inventory_policies`)).rows[0].n,"0");
      await c.query("SELECT set_config('app.role','store_manager',true),set_config('app.tenant_id',$1,true)",[t.tenant]);
      assert.equal((await c.query(`SELECT count(*) n FROM iot_inventory_policies WHERE "TenantId"=$1`,[t.tenant])).rows[0].n,"1");
      assert.equal((await c.query(`SELECT count(*) n FROM iot_inventory_policies WHERE "TenantId"<>$1`,[t.tenant])).rows[0].n,"0");
      await c.query("SET LOCAL app.role='worker'");
      await assert.rejects(c.query(`INSERT INTO iot_inventory_policies("TenantId","DeviceId","Version","Enabled","ActorId","ItemId","LocationId","ZoneId","Unit","UnitWeightGrams","Reason") SELECT "TenantId","DeviceId",2,true,"ActorId","ItemId","LocationId","ZoneId","Unit","UnitWeightGrams",'Forged runtime authorization must be denied' FROM iot_inventory_policies WHERE "DeviceId"=$1`,[t.device]),(e:unknown)=>(e as {code:string}).code==="P4F01");
      await c.query("ROLLBACK");
    }finally{c.release();}
  }finally{await runtime.end();await query(`DROP OWNED BY ${role};DROP ROLE ${role}`);}
});
erpTest("temperature telemetry in a fenced scope never creates inventory ledger entries",async()=>{
  const t=await erpSeed();await query(`UPDATE iot_devices SET "DeviceType"='temp_sensor',"Config"='{"profile":"fridge"}'::jsonb WHERE "Id"=$1`,[t.device]);
  await query(`INSERT INTO temperature_readings("DeviceId","LocationId","ZoneId","Temperature","IsAlert","RecordedAt") VALUES($1,$2,$3,10,true,now()-interval '119 minutes')`,[t.device,t.store,t.zone]);
  await ingestSensorMessage(pool!,topic(t),raw(t,{delta:undefined,temperature:10}),t.options);
  assert.deepEqual(await balances(t),[2,3]);assert.equal(await count("notification_queue","TenantId",t.tenant),1);
  assert.equal((await query(`SELECT count(*) n FROM business_documents WHERE "TenantId"=$1 AND "SourceSystem"='iot.weight.v1'`,[t.tenant])).rows[0].n,"0");
});
erpTest("disabled database guard makes ERP unavailable without using legacy writes",async()=>{
  const t=await erpSeed();await query("ALTER TABLE iot_message_receipts DISABLE TRIGGER erp_w4_iot_receipt_guard");
  try{await ingestSensorMessage(pool!,topic(t),raw(t),t.options);assert.deepEqual(await balances(t),[2,3]);}
  finally{await query("ALTER TABLE iot_message_receipts ENABLE TRIGGER erp_w4_iot_receipt_guard");}
});
erpTest("two authorized scales sharing a location cannot overdraw the same stock",async()=>{
  const t=await erpSeed();const second=randomUUID();
  await query(`INSERT INTO iot_devices("Id","TenantId","LocationId","ZoneId","DeviceType","DeviceId","Config") SELECT $1,"TenantId","LocationId","ZoneId","DeviceType",$2,"Config" FROM iot_devices WHERE "Id"=$3`,[second,"second-erp-scale",t.device]);
  await query(`INSERT INTO iot_inventory_policies("TenantId","DeviceId","Version","Enabled","ActorId","ItemId","LocationId","ZoneId","Unit","UnitWeightGrams","AccountingMode","Reason") SELECT "TenantId",$1,1,true,"ActorId","ItemId","LocationId","ZoneId","Unit","UnitWeightGrams",'automatic_write_down','Authorized second synthetic scale for concurrency test' FROM iot_inventory_policies WHERE "DeviceId"=$2`,[second,t.device]);
  const results=await Promise.all([ingestSensorMessage(pool!,topic(t),raw(t,{delta:-980}),t.options),ingestSensorMessage(pool!,topic(t),raw(t,{device_id:"second-erp-scale",delta:-980}),t.options)]);
  assert.ok(results.every(r=>r.status==="created"));assert.deepEqual(await balances(t),[0,1]);
  assert.equal((await query(`SELECT count(*) n FROM business_documents WHERE "TenantId"=$1 AND "SourceSystem"='iot.weight.v1'`,[t.tenant])).rows[0].n,"1");
});
test("ERP IoT real MQTT lost commit acknowledgement redelivers without a second posting",{skip:!broker,timeout:20000},async()=>{
  const t=await erpSeed();const clientId="erp-iot-lost-ack-"+randomUUID();const first=mqtt.connect(broker!,{clientId,clean:false,reconnectPeriod:0});first.on("error",()=>{});
  first.handleMessage=sensorMessageHandler(async(route,bytes)=>{await ingestSensorMessage(pool!,route,bytes,t.options);throw new Error("synthetic ERP commit acknowledgement lost");},e=>first.stream.destroy(e),()=>{});
  try{
    await connected(first);await first.subscribeAsync(topic(t),{qos:1});const closed=new Promise<void>(resolve=>first.once("close",()=>resolve()));
    await publishSimulation(broker!,topic(t),simulatedReading("weight",t.physical,"sim:lost-ack",-735));await closed;await first.endAsync(true);
    const second=mqtt.connect(broker!,{clientId,clean:false,reconnectPeriod:0});second.on("error",()=>{});let status="";let ack!:()=>void;const done=new Promise<void>(resolve=>{ack=resolve;});
    second.handleMessage=sensorMessageHandler((route,bytes)=>ingestSensorMessage(pool!,route,bytes,t.options),e=>second.stream.destroy(e),r=>{status=r.status;});
    second.on("packetsend",p=>{if(p.cmd==="puback")ack();});
    try{await done;assert.equal(status,"duplicate");assert.deepEqual(await balances(t),[0,2]);assert.equal((await query(`SELECT count(*) n FROM business_documents WHERE "TenantId"=$1 AND "SourceSystem"='iot.weight.v1'`,[t.tenant])).rows[0].n,"1");}
    finally{await second.endAsync(true);}
  }finally{await first.endAsync(true);}
});
erpTest("POS-enabled tenants treat an automatic sensor policy as observation only",async()=>{
  const t=await erpSeed();await query(`UPDATE tenants SET "Modules"='["inventory","pos"]'::jsonb WHERE "Id"=$1`,[t.tenant]);
  const message=raw(t,{delta:-735,event_id:"pos-physical-movement"});
  assert.equal((await ingestSensorMessage(pool!,topic(t),message,t.options)).status,"created");
  assert.equal((await ingestSensorMessage(pool!,topic(t),message,t.options)).status,"duplicate");assert.deepEqual(await balances(t),[2,3]);
  const event=(await query(`SELECT "Meta" FROM stock_events WHERE "TenantId"=$1`,[t.tenant])).rows[0].Meta;
  assert.equal(event.reason,"pos_observation");assert.equal(event.requiresReview,true);assert.equal(event.source.posPrimary,true);
  assert.equal(JSON.parse(event.canonicalPayload).event_id,"pos-physical-movement");
  assert.equal((await query(`SELECT count(*) n FROM outbox_events WHERE "TenantId"=$1 AND "DeliveryScope"='live'`,[t.tenant])).rows[0].n,"0");
});
erpTest("a new authorization version defaults to observation without discarding telemetry",async()=>{
  const t=await erpSeed();await query(`INSERT INTO iot_inventory_policies("TenantId","DeviceId","Version","Enabled","ActorId","ItemId","LocationId","ZoneId","Unit","UnitWeightGrams","Reason") SELECT "TenantId","DeviceId",2,true,"ActorId","ItemId","LocationId","ZoneId","Unit","UnitWeightGrams",'Observed scale movements require review by default' FROM iot_inventory_policies WHERE "DeviceId"=$1`,[t.device]);
  await ingestSensorMessage(pool!,topic(t),raw(t),t.options);assert.deepEqual(await balances(t),[2,3]);
  assert.equal((await query(`SELECT "Meta"->>'reason' reason FROM stock_events WHERE "TenantId"=$1`,[t.tenant])).rows[0].reason,"policy_observation");
});
erpTest("legacy POS observations also cannot deduct stock twice",async()=>{
  const t=await seed();await query(`UPDATE tenants SET "Modules"='["inventory","pos"]'::jsonb WHERE "Id"=$1`,[t.tenant]);
  await ingestSensorMessage(pool!,topic(t),raw(t,{delta:-735}));assert.deepEqual(await balances(t),[2,3]);
  assert.equal((await query(`SELECT "Meta"->>'reason' reason FROM stock_events WHERE "TenantId"=$1`,[t.tenant])).rows[0].reason,"pos_observation");
});

erpTest("expiry maintenance rechecks sensor holds after its snapshot and updates only eligible batches",async()=>{
  const t=await erpSeed();
  const snapshot=await query(`SELECT "Status" FROM product_stock WHERE "Id"=$1`,[t.early]);
  assert.notEqual(snapshot.rows[0].Status,"temp_violation");
  await query(`UPDATE product_stock SET "Status"='temp_violation' WHERE "Id"=$1`,[t.early]);
  const now=new Date().toISOString();
  const held=await query(updateBatchExpirySql,["critical",now,true,true,t.early]);
  assert.equal(held.rowCount,0);
  const row=(await query(`SELECT "Status","NotifiedWarningAt","NotifiedCriticalAt" FROM product_stock WHERE "Id"=$1`,[t.early])).rows[0];
  assert.equal(row.Status,"temp_violation");assert.equal(row.NotifiedWarningAt,null);assert.equal(row.NotifiedCriticalAt,null);
  assert.equal((await query(updateBatchExpirySql,["warning",now,true,false,t.late])).rowCount,1);
  assert.equal((await query(`SELECT "Status" FROM product_stock WHERE "Id"=$1`,[t.late])).rows[0].Status,"warning");
  assert.deepEqual(await balances(t),[2,3]);
});
erpTest("missing integrated migration never falls back to legacy quantity writes",async()=>{
  const t=await erpSeed();await query("ALTER FUNCTION public.erp_w4_integrated_ready(uuid,uuid) RENAME TO iot_test_integrated_ready");
  try{await ingestSensorMessage(pool!,topic(t),raw(t),t.options);assert.deepEqual(await balances(t),[2,3]);
    assert.equal((await query(`SELECT "Meta"->>'reason' reason FROM stock_events WHERE "TenantId"=$1`,[t.tenant])).rows[0].reason,"erp_schema_required");
  }finally{await query("ALTER FUNCTION public.iot_test_integrated_ready(uuid,uuid) RENAME TO erp_w4_integrated_ready");}
});
erpTest("a POS configuration change during an automatic posting aborts the entire sensor transaction",async()=>{
  const t=await erpSeed();await query(`CREATE FUNCTION iot_test_enable_pos() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW."TenantId"='${t.tenant}'::uuid THEN UPDATE tenants SET "Modules"='["inventory","pos"]'::jsonb WHERE "Id"=NEW."TenantId";END IF;RETURN NEW;END $$;CREATE TRIGGER iot_test_enable_pos AFTER UPDATE ON product_stock FOR EACH ROW EXECUTE FUNCTION iot_test_enable_pos()`);
  try{await assert.rejects(ingestSensorMessage(pool!,topic(t),raw(t),t.options));}
  finally{await query("DROP TRIGGER iot_test_enable_pos ON product_stock;DROP FUNCTION iot_test_enable_pos()");}
  assert.deepEqual(await balances(t),[2,3]);assert.equal(await count("iot_message_receipts","DeviceId",t.device),0);
});
for(const table of ["iot_message_receipts","weight_readings","stock_events"]){
  erpTest("posted "+table+" source cannot be deleted",async()=>{
    const t=await erpSeed();await ingestSensorMessage(pool!,topic(t),raw(t),t.options);
    const column=table==="stock_events"?"TenantId":"DeviceId";const id=table==="stock_events"?t.tenant:t.device;
    await assert.rejects(query(`DELETE FROM ${table} WHERE "${column}"=$1`,[id]),(e:unknown)=>(e as {code:string}).code==="P4F01");
  });
}
test("ERP IoT simulator through real MQTT commits once despite duplicate publishes",{skip:!broker,timeout:20000},async()=>{
  const t=await erpSeed();const subscriber=mqtt.connect(broker!,{clientId:"erp-iot-sim-"+randomUUID(),clean:false,reconnectPeriod:0});subscriber.on("error",()=>{});
  const results:string[]=[];let completed!:()=>void;const done=new Promise<void>(resolve=>{completed=resolve;});
  subscriber.handleMessage=sensorMessageHandler((route,bytes)=>ingestSensorMessage(pool!,route,bytes,t.options),e=>subscriber.stream.destroy(e),r=>{results.push(r.status);if(results.length===2)completed();});
  try{await connected(subscriber);await subscriber.subscribeAsync(topic(t),{qos:1});await publishSimulation(broker!,topic(t),simulatedReading("weight",t.physical,"sim:duplicate",-735),2);await done;
    assert.deepEqual(results,["created","duplicate"]);assert.deepEqual(await balances(t),[0,2]);
  }finally{await subscriber.endAsync(true);}
});


