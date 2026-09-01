import { describe, expect, it } from "vitest";
import { formatDeliveryTerms } from "./formatDeliveryTerms";
import type { DeliveryCoverageEntry } from "../types";

// Stub translator scoped to `Dashboard.geo.deliveryTerms` — mirrors the uk.json
// literals so the assertions read like the real UI output.
const t = (key: string, v: Record<string, string | number> = {}): string => {
  switch (key) {
    case "daysRange":
      return `${v.min}–${v.max} дні`;
    case "daysExact":
      return `${v.days} дн.`;
    case "daysFrom":
      return `від ${v.min} днів`;
    case "daysTo":
      return `до ${v.max} днів`;
    case "minAmount":
      return `від ${v.amount} грн`;
    default:
      return key;
  }
};

function entry(patch: Partial<DeliveryCoverageEntry>): DeliveryCoverageEntry {
  return {
    regionCode: "UA-32",
    deliveryDaysMin: null,
    deliveryDaysMax: null,
    minOrderAmount: null,
    note: null,
    ...patch,
  };
}

describe("formatDeliveryTerms", () => {
  it("renders a day range when both bounds are present", () => {
    expect(
      formatDeliveryTerms(entry({ deliveryDaysMin: 1, deliveryDaysMax: 3 }), t),
    ).toBe("1–3 дні");
  });

  it("collapses an equal min/max pair to a single value", () => {
    expect(
      formatDeliveryTerms(entry({ deliveryDaysMin: 2, deliveryDaysMax: 2 }), t),
    ).toBe("2 дн.");
  });

  it("renders 'від N днів' with only a min bound", () => {
    expect(formatDeliveryTerms(entry({ deliveryDaysMin: 2 }), t)).toBe(
      "від 2 днів",
    );
  });

  it("renders 'до N днів' with only a max bound", () => {
    expect(formatDeliveryTerms(entry({ deliveryDaysMax: 3 }), t)).toBe(
      "до 3 днів",
    );
  });

  it("renders the minimum order amount", () => {
    expect(formatDeliveryTerms(entry({ minOrderAmount: 5000 }), t)).toBe(
      "від 5000 грн",
    );
  });

  it("joins days and amount with ' · '", () => {
    expect(
      formatDeliveryTerms(
        entry({ deliveryDaysMin: 1, deliveryDaysMax: 3, minOrderAmount: 5000 }),
        t,
      ),
    ).toBe("1–3 дні · від 5000 грн");
  });

  it("returns an empty string when no structured data is present", () => {
    expect(formatDeliveryTerms(entry({ note: "Новою Поштою" }), t)).toBe("");
  });

  it("swaps a reversed day pair defensively", () => {
    expect(
      formatDeliveryTerms(entry({ deliveryDaysMin: 5, deliveryDaysMax: 2 }), t),
    ).toBe("2–5 дні");
  });

  it("drops insignificant decimals in the amount", () => {
    expect(formatDeliveryTerms(entry({ minOrderAmount: 4999.9 }), t)).toBe(
      "від 4999.9 грн",
    );
  });
});
