import { api } from "@/lib/api";

export type InventoryPostingStatus = { locationId: string; configured: boolean; fenced: boolean; canAdjust: boolean };
export type InventoryAdjustmentCommand = Readonly<{
  requestId: string; locationId: string; batchId: string;
  expectedQuantity: string; quantity: string; reason: string;
}>;
export type InventoryAdjustmentReceipt = {
  tenantId: string; requestId: string; documentId: string; postingId: string; entryId: string; eventId: string;
  batchId: string; quantityBefore: string; quantityAfter: string; postedAt: string; replayed: boolean;
};

function count(value: string): string {
  if (!/^(0|[1-9]\d{0,7})(\.\d{1,2})?$/.test(value)) throw new Error("Provide an exact nonnegative count with at most two decimal places.");
  const [whole, fraction = ""] = value.split(".");
  return `${whole}.${fraction.padEnd(2, "0")}`;
}

/** Create once per operator intent; retry the frozen command, including after an unknown network outcome. */
export function createInventoryAdjustment(input: {
  requestId: string; locationId: string; batchId: string; expectedQuantity: string; quantity: string; reason: string;
}): InventoryAdjustmentCommand {
  const guid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
  for (const value of [input.requestId, input.locationId, input.batchId])
    if (!guid.test(value) || /^0{8}-0{4}-0{4}-0{4}-0{12}$/.test(value)) throw new Error("Explicit request, location and batch IDs are required.");
  const expectedQuantity = count(input.expectedQuantity), quantity = count(input.quantity);
  const reason = input.reason.trim();
  if (quantity === expectedQuantity) throw new Error("The count must change.");
  if (!reason || reason.length > 500 || /[\u0000-\u001f\u007f-\u009f]/.test(input.reason)) throw new Error("Provide an adjustment reason of 1–500 printable characters.");
  return Object.freeze({ ...input, expectedQuantity, quantity, reason });
}

export const inventoryPostingApi = {
  status: (locationId: string) => api.get<InventoryPostingStatus>(`/api/erp/inventory/scopes/${locationId}`),
  adjust: (command: InventoryAdjustmentCommand) => api.post<InventoryAdjustmentReceipt>("/api/erp/inventory/adjustments", command),
};
