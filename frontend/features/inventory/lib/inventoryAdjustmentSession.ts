import { createInventoryAdjustment, type InventoryAdjustmentCommand } from "../api/inventory-posting";

export type AdjustmentIdentity = { tenantId: string; actorId: string };
export type AdjustmentBatch = {
  id: string; storeId: string; productName: string; storeName: string; batchNumber: string | null; expiryDate: string;
};
export type SavedAdjustment = {
  version: 1; phase: "uncertain" | "rejected"; command: InventoryAdjustmentCommand; batch: AdjustmentBatch;
};

function key(identity: AdjustmentIdentity) {
  return `sg_inventory_adjustment:v1:${identity.tenantId}:${identity.actorId}`;
}

/** One unresolved operator intent per tab/tenant/actor. Never store access tokens or the full stock DTO. */
export function readAdjustment(identity: AdjustmentIdentity, storage: Storage): SavedAdjustment | null {
  const raw = storage.getItem(key(identity));
  if (!raw) return null;
  const saved: SavedAdjustment = JSON.parse(raw);
  if (saved.version !== 1 || !["uncertain", "rejected"].includes(saved.phase)
    || saved.batch?.id !== saved.command?.batchId || saved.batch?.storeId !== saved.command?.locationId
    || ![saved.batch.productName, saved.batch.storeName, saved.batch.expiryDate].every(value => typeof value === "string"))
    throw new Error("Invalid saved adjustment.");
  const command = createInventoryAdjustment(saved.command);
  if (JSON.stringify(command) !== JSON.stringify(saved.command)) throw new Error("Saved command changed.");
  return { ...saved, command };
}

export function saveAdjustment(identity: AdjustmentIdentity, saved: SavedAdjustment | null, storage: Storage): void {
  if (saved) storage.setItem(key(identity), JSON.stringify(saved));
  else storage.removeItem(key(identity));
}
