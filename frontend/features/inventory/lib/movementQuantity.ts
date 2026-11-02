import type { MovementDto } from "../api/movements";

/** The batch delta determines direction; an adjustment or transfer can go either way. */
export function signedMovementQuantity(movement: Pick<MovementDto,
  "quantityBefore" | "quantityAfter" | "quantity" | "movementType" | "fromStoreId" | "toStoreId"
>): number {
  if (movement.quantityBefore != null && movement.quantityAfter != null) {
    return Math.round((movement.quantityAfter - movement.quantityBefore) * 100) / 100;
  }
  if (movement.quantity < 0) return movement.quantity;
  if (movement.toStoreId && !movement.fromStoreId) return Math.abs(movement.quantity);
  if (movement.fromStoreId && !movement.toStoreId) return -Math.abs(movement.quantity);
  return (movement.movementType === "receipt" || movement.movementType === "adjustment" ? 1 : -1)
    * Math.abs(movement.quantity);
}
