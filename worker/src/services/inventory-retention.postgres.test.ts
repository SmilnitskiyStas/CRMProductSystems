import assert from "node:assert/strict";
import { randomUUID } from "node:crypto";
import { test } from "node:test";
import { Pool } from "pg";
import { inventoryRetentionPredicate } from "./inventory-retention";

const connection = process.env.SHELFGUARD_RETENTION_TEST_DB;
if (connection && !new URL(connection).pathname.startsWith("/erm_schema_test_"))
  throw new Error("Retention tests require an explicit disposable erm_schema_test_* database.");
test("cleanup deletes legacy events but retains fenced ERP events and depleted batches", { skip: !connection }, async () => {
  const pool = new Pool({ connectionString: connection, max: 1 });
  const client = await pool.connect();
  try {
    await client.query("BEGIN");
    await client.query("SET LOCAL app.role='worker'");
    const before = await client.query(`SELECT count(*)::int AS n FROM stock_events e JOIN inventory_posting_scopes f ON f."TenantId"=e."TenantId" AND f."LocationId"=e."StoreId"`);
    assert.ok(before.rows[0].n > 0, "Provision ERP posting fixtures first");
    const tenant=randomUUID(), location=randomUUID(), event=randomUUID();
    await client.query(`INSERT INTO tenants ("Id","Name","Slug") VALUES ($1,'Retention legacy',$2)`,[tenant,tenant]);
    await client.query(`INSERT INTO locations ("Id","TenantId","Name","Type") VALUES ($1,$2,'Legacy location','store')`,[location,tenant]);
    await client.query(`INSERT INTO stock_events ("Id","TenantId","StoreId","EventType","CreatedAt","Confidence") VALUES ($1,$2,$3,'scan',NOW()-INTERVAL '200 days',100)`,[event,tenant,location]);
    const events = await inventoryRetentionPredicate(client,"StoreId");
    const removed=await client.query(`DELETE FROM stock_events retained WHERE "CreatedAt"<NOW()+INTERVAL '1 day'${events} RETURNING "Id"`);
    assert.ok(removed.rows.some(r=>r.Id===event));
    const after = await client.query(`SELECT count(*)::int AS n FROM stock_events e JOIN inventory_posting_scopes f ON f."TenantId"=e."TenantId" AND f."LocationId"=e."StoreId"`);
    assert.equal(after.rows[0].n,before.rows[0].n);
    const stocks=await inventoryRetentionPredicate(client,"LocationId");
    const protectedStock=await client.query(`SELECT s."Id",s."Status" FROM product_stock s JOIN inventory_posting_scopes f ON f."TenantId"=s."TenantId" AND f."LocationId"=s."LocationId" WHERE s."Quantity"=0 ORDER BY s."Id"`);
    assert.ok(protectedStock.rowCount!>0,"Provision a depleted ERP batch first");
    const archived=await client.query(`UPDATE product_stock retained SET "Status"='archived' WHERE "Quantity"=0${stocks} RETURNING "TenantId","LocationId"`);
    for(const row of archived.rows) assert.equal((await client.query(`SELECT 1 FROM inventory_posting_scopes WHERE "TenantId"=$1 AND "LocationId"=$2`,[row.TenantId,row.LocationId])).rowCount,0);
    const preserved=await client.query(`SELECT "Id","Status" FROM product_stock WHERE "Id"=ANY($1::uuid[]) ORDER BY "Id"`,[protectedStock.rows.map(r=>r.Id)]);
    assert.deepEqual(preserved.rows,protectedStock.rows);
    await client.query("ROLLBACK");
  } finally { client.release(); await pool.end(); }
});
