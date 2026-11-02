import assert from "node:assert/strict";
import {test} from "node:test";
import {simulatedReading} from "./iot-simulator";
import {parseSensorMessage} from "../services/iot-ingestion";
const topic="shelfguard/11111111-1111-1111-1111-111111111111/22222222-2222-2222-2222-222222222222/33333333-3333-3333-3333-333333333333/shelf";
for(const kind of ["weight","temperature","low-confidence"] as const)test("Simulator emits accepted "+kind+" payloads",()=>{
  const now=new Date();const raw=simulatedReading(kind,"synthetic-scale","sim:42",undefined,now);
  assert.ok(parseSensorMessage(topic,raw,now.getTime()));assert.equal(JSON.parse(raw.toString()).event_id,"sim:42");
});
test("Simulator can repeat the identical identity and content",()=>{
  const now=new Date();assert.deepEqual(simulatedReading("weight","scale","sim:42",-735,now),simulatedReading("weight","scale","sim:42",-735,now));
});
