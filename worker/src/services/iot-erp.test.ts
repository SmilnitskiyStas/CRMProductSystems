import assert from "node:assert/strict";
import { test } from "node:test";
import { readIotErpOptions } from "./iot-erp";

test("IoT ERP automation is disabled and has no scopes by default", () => {
  assert.deepEqual(readIotErpOptions({}), { enabled: false, scopes: [] });
});
test("IoT ERP scope identifiers are normalized without widening the allowlist", () => {
  const tenantId="AAAA1111-1111-1111-1111-111111111111", locationId="BBBB2222-2222-2222-2222-222222222222";
  assert.deepEqual(readIotErpOptions({ ERP_IOT_ENABLED: "true", ERP_IOT_SCOPES: JSON.stringify([{tenantId,locationId}]) }),
    { enabled:true,scopes:[{tenantId:tenantId.toLowerCase(),locationId:locationId.toLowerCase()}] });
});
for(const env of [{ERP_IOT_ENABLED:"1"},{ERP_IOT_ENABLED:"TRUE"},{ERP_IOT_SCOPES:"broken"},{ERP_IOT_SCOPES:"null"},
  {ERP_IOT_SCOPES:"{}"},{ERP_IOT_SCOPES:'[{}]'},{ERP_IOT_SCOPES:'[null]'},{ERP_IOT_SCOPES:'[{"tenantId":"all","locationId":"*"}]'}])
  test("Malformed IoT ERP configuration fails closed: "+JSON.stringify(env),()=>assert.throws(()=>readIotErpOptions(env)));
