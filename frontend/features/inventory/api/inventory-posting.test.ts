import { afterEach, expect, it, vi } from "vitest";
import { api } from "@/lib/api";
import { createInventoryAdjustment, inventoryPostingApi } from "./inventory-posting";

const input = { requestId: "33333333-3333-3333-3333-333333333333", locationId: "44444444-4444-4444-4444-444444444444",
  batchId: "55555555-5555-5555-5555-555555555555", expectedQuantity: "3.2", quantity: "4.5", reason: "  Physical count  " };
afterEach(() => { vi.restoreAllMocks(); });

it("freezes the exact command and reuses the request identity and decimal strings on retry", async () => {
  const command = createInventoryAdjustment(input);
  expect(Object.isFrozen(command)).toBe(true);
  expect(command).toMatchObject({ expectedQuantity: "3.20", quantity: "4.50", reason: "Physical count" });
  const post = vi.spyOn(api, "post").mockRejectedValueOnce(new Error("unknown network outcome")).mockResolvedValueOnce({ replayed: true });
  await expect(inventoryPostingApi.adjust(command)).rejects.toThrow("unknown network outcome");
  await expect(inventoryPostingApi.adjust(command)).resolves.toEqual({ replayed: true });
  expect(post.mock.calls[0]).toEqual(post.mock.calls[1]);
  expect(post.mock.calls[0]).toEqual(["/api/erp/inventory/adjustments", command]);
});

it.each(["0.001", "-1", "1e2", "NaN", "Infinity", "100000000", "1,25", "01", "", " 1"])("rejects unsafe or ambiguous quantities: %s", value => {
  expect(() => createInventoryAdjustment({ ...input, quantity: value })).toThrow();
  expect(() => createInventoryAdjustment({ ...input, expectedQuantity: value })).toThrow();
});
it("keeps a valid explicit zero and the numeric(10,2) maximum exact", () => {
  expect(createInventoryAdjustment({ ...input, expectedQuantity: "0", quantity: "99999999.99" })).toMatchObject({ expectedQuantity: "0.00", quantity: "99999999.99" });
});
it("rejects a no-op, missing reason and empty identities before sending", () => {
  expect(() => createInventoryAdjustment({ ...input, quantity: "3.20" })).toThrow();
  expect(() => createInventoryAdjustment({ ...input, reason: "\t" })).toThrow();
  expect(() => createInventoryAdjustment({ ...input, requestId: "00000000-0000-0000-0000-000000000000" })).toThrow();
});
it("reads the server-side fence and application gate separately", async () => {
  const status = { configured: false, fenced: true, canAdjust: false, locationId: input.locationId };
  const get = vi.spyOn(api, "get").mockResolvedValue(status);
  expect(await inventoryPostingApi.status(input.locationId)).toEqual(status);
  expect(get).toHaveBeenCalledWith(`/api/erp/inventory/scopes/${input.locationId}`);
});
