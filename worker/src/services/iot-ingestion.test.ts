import assert from "node:assert/strict";
import { test } from "node:test";
import type { IPublishPacket } from "mqtt";
import { parseSensorMessage } from "./iot-ingestion";
import { assessWeightDelta, parseDeviceConfig } from "./iot-rules";
import { sensorMessageHandler } from "./iot-mqtt-delivery";

const topic = "shelfguard/11111111-1111-1111-1111-111111111111/22222222-2222-2222-2222-222222222222/zone/shelf";
const now = Date.parse("2026-10-24T10:00:00Z");
const payload = { device_id: "scale", event_id: "boot-id:42", timestamp: "2026-10-24T10:00:00Z", delta: -245 };
const parse = (value: unknown, route = topic) => parseSensorMessage(route, Buffer.from(JSON.stringify(value)), now);

test("producer identity ignores JSON ordering and timestamps use a canonical offset", () => {
  const original = parse(payload)!;
  const reordered = parse({ delta: -245, timestamp: "2026-10-24T12:00:00+02:00", event_id: "boot-id:42", device_id: "scale" })!;
  assert.equal(original.messageKey, reordered.messageKey);
  assert.equal(original.payloadHash, reordered.payloadHash);
  assert.notEqual(original.payloadHash, parse({ ...payload, delta: -490 })!.payloadHash);
  assert.notEqual(original.messageKey, parse({ ...payload, event_id: "boot-id:43" })!.messageKey);
});
test("legacy timestamp is a stable key but arrival time and MQTT packet ids never are", () => {
  const old = { ...payload, event_id: undefined };
  assert.equal(parse(old)!.messageKey, parse({ ...old, delta: -490 })!.messageKey);
  assert.equal(parse({ ...old, timestamp: undefined }), null);
  assert.ok(parse({ ...payload, timestamp: undefined }));
});
for (const invalid of [null, [], "bad", { ...payload, device_id: " " }, { ...payload, event_id: " " },
  { ...payload, timestamp: "2026-10-24T10:06:00Z" }, { ...payload, timestamp: "not-a-date" },
  { ...payload, timestamp: "2026-10-24T10:00:00" }, { ...payload, delta: "-245" },
  { ...payload, delta: 100_000_000 }, { ...payload, battery: 101 }, { ...payload, battery: 1.1 },
  { ...payload, humidity: -1 }, { ...payload, temperature: 61 },
  { ...payload, weight_before: 100, weight_after: 200 }, { ...payload, weight_before: -1 }])
  test("reject malformed telemetry: " + JSON.stringify(invalid), () => assert.equal(parse(invalid), null));
test("tenant and store must be present in the MQTT namespace", () => {
  assert.equal(parse(payload, "shelfguard/scale"), null);
  assert.equal(parse(payload, topic.replace("shelfguard", "other")), null);
  assert.equal(parseSensorMessage(topic, Buffer.alloc(16_385), now), null);
  assert.equal(parseSensorMessage(topic, Buffer.from('{"delta":1e400}'), now), null);
});
test("broken weight configuration never results in an unbounded write-down", () => {
  for (const weight of [0, -1, NaN, Infinity, Number.MIN_VALUE])
    assert.deepEqual(assessWeightDelta(-245, weight), { units: 0, confidence: 0 });
  assert.deepEqual(assessWeightDelta(NaN, 245), { units: 0, confidence: 0 });
  for (const raw of [null, [], "null", "[]", "42", "broken"])
    assert.deepEqual(parseDeviceConfig(raw), {});
  assert.deepEqual(assessWeightDelta(-490, 245), { units: 2, confidence: 85 });
});

const packet = { topic, payload: Buffer.from(JSON.stringify(payload)), retain: false } as IPublishPacket;
test("delivery is acknowledged only after ingestion commits", async () => {
  let commit!: () => void;
  const waiting = new Promise<void>(resolve => { commit = resolve; });
  const events: string[] = [];
  const handler = sensorMessageHandler(async () => { await waiting; return { status: "created" }; },
    () => events.push("disconnect"), () => events.push("commit"));
  const finished = new Promise<void>(resolve => handler(packet, error => { assert.equal(error, undefined); events.push("ack"); resolve(); }));
  assert.deepEqual(events, []);
  commit(); await finished;
  assert.deepEqual(events, ["commit", "ack"]);
});
test("database failure withholds success acknowledgment and disconnects for redelivery", async () => {
  const failure = new Error("synthetic database failure"); const events: string[] = [];
  const handler = sensorMessageHandler(async () => { throw failure; },
    error => { assert.equal(error, failure); events.push("disconnect"); }, () => assert.fail("not committed"));
  await new Promise<void>(resolve => handler(packet, error => { assert.equal(error, failure); events.push("error"); resolve(); }));
  assert.deepEqual(events, ["disconnect", "error"]);
});
test("retained sensor state is never interpreted as a new stock delta", () => {
  let acked = false;
  sensorMessageHandler(async () => assert.fail("retained ingestion"), () => assert.fail("disconnect"), () => assert.fail("commit"))(
    { ...packet, retain: true }, error => { assert.equal(error, undefined); acked = true; });
  assert.ok(acked);
});
