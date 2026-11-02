import { describe, expect, it } from "vitest";
import { signedMovementQuantity } from "./movementQuantity";

const movement = {
  movementType: "adjustment" as const, quantity: 3,
  quantityBefore: 5 as number | null, quantityAfter: 2 as number | null,
  fromStoreId: null as string | null, toStoreId: null as string | null,
};

describe("movement direction", () => {
  it("shows an adjustment decrease as negative", () => {
    expect(signedMovementQuantity(movement)).toBe(-3);
  });
  it("keeps decimal stock quantities readable without floating point artifacts", () => {
    expect(signedMovementQuantity({ ...movement, quantityBefore: 5.7, quantityAfter: 2.4 })).toBe(-3.3);
  });
  it("shows a transfer cancellation restoration as positive", () => {
    expect(signedMovementQuantity({ ...movement, movementType: "transfer", quantityBefore: 0, quantityAfter: 3,
      toStoreId: "source" })).toBe(3);
  });
  it("uses batch changes for both outgoing and incoming transfer rows", () => {
    expect(signedMovementQuantity({ ...movement, movementType: "transfer", fromStoreId: "from", toStoreId: "to" })).toBe(-3);
    expect(signedMovementQuantity({ ...movement, movementType: "transfer", quantityAfter: 8,
      fromStoreId: "from", toStoreId: "to" })).toBe(3);
  });
  it("preserves legacy signed quantities and directional rows without batch snapshots", () => {
    const legacy = { ...movement, quantityBefore: null, quantityAfter: null };
    expect(signedMovementQuantity({ ...legacy, quantity: -3 })).toBe(-3);
    expect(signedMovementQuantity({ ...legacy, fromStoreId: "source" })).toBe(-3);
    expect(signedMovementQuantity({ ...legacy, movementType: "transfer", toStoreId: "source" })).toBe(3);
    expect(signedMovementQuantity({ ...legacy, movementType: "write_off" })).toBe(-3);
  });
});
