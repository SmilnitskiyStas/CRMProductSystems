import { describe, expect, it } from "vitest";
import { buildProductChartData } from "./productChartData";
import type { MovementDto } from "../api/movements";

const receipt = (date: string, quantity: number): MovementDto => ({
  createdAt: `${date}T12:00:00Z`, movementType: "receipt", quantity, quantityAfter: quantity,
} as MovementDto);

describe("product stock chart", () => {
  it("uses the total history including sales, rather than a receipt's batch balance", () => {
    const points = buildProductChartData([receipt("2026-10-21", 20)], "2026-10-21", "2026-10-23", [
      { date: "2026-10-21", stock: 30 }, { date: "2026-10-22", stock: 25 }, { date: "2026-10-23", stock: 22 },
    ]);
    expect(points.map(p => p.stock)).toEqual([30, 25, 22]);
    expect(points[0].receipt).toBe(20);
  });
  it("shows stock on sale-only days with no warehouse movements", () => {
    expect(buildProductChartData([], "2026-10-21", "2026-10-22", [
      { date: "2026-10-21", stock: 10 }, { date: "2026-10-22", stock: 0 },
    ]).map(p => p.stock)).toEqual([10, 0]);
  });
  it("does not invent a flat balance for missing or future dates", () => {
    expect(buildProductChartData([receipt("2026-10-21", 20)], "2026-10-21", "2026-10-23", [
      { date: "2026-10-21", stock: 15 },
    ]).map(p => p.stock)).toEqual([15, null, null]);
  });
  it("groups overlays without replacing stock or depending on movement order", () => {
    const movements = [receipt("2026-10-22", 5), receipt("2026-10-21", 20), receipt("2026-10-21", 10)];
    const points = buildProductChartData(movements, "2026-10-21", "2026-10-22", [
      { date: "2026-10-21", stock: 27 }, { date: "2026-10-22", stock: 31 },
    ]);
    expect(points.map(p => [p.stock, p.receipt])).toEqual([[27, 30], [31, 5]]);
  });
});
