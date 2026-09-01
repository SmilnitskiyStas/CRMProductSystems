// Shared geo taxonomy types. Mirrors the backend `RegionDto` contract from
// `GET /api/geo/regions` (Ukraine region registry — oblasts + major cities).
//
// Oblast codes are ISO 3166-2:UA. Note the classic pair:
//   UA-30 = м. Київ (the city, kind "oblast" — top-level administrative unit)
//   UA-32 = Київська обл. (the surrounding region)
// City codes look like `UA-18-ZHYTOMYR` with `parentCode: "UA-18"`.

export interface Region {
  code: string;
  nameUa: string;
  kind: "oblast" | "city";
  parentCode: string | null;
}

/**
 * One served region with structured per-region delivery terms (TASK-665).
 * Every field is optional. Mirrors the backend `DeliveryCoverageEntryDto`
 * `(regionCode, deliveryDaysMin, deliveryDaysMax, minOrderAmount, note)`.
 * The legacy single free-text `terms` string was replaced by these — the
 * backend self-heals a legacy `terms` value into `note` on read.
 */
export interface DeliveryCoverageEntry {
  regionCode: string;
  /** Lower bound of the delivery-time range, in whole days. */
  deliveryDaysMin: number | null;
  /** Upper bound of the delivery-time range, in whole days. */
  deliveryDaysMax: number | null;
  /** Minimum order amount for this region, in UAH. */
  minOrderAmount: number | null;
  /** Free-text per-region note (delivery carrier, conditions, …). */
  note: string | null;
}

/**
 * A supplier's declared delivery coverage. `served` and `notServed` are mutually
 * exclusive sets of region codes; `note` is a free-text catch-all.
 */
export interface DeliveryCoverage {
  served: DeliveryCoverageEntry[];
  notServed: string[];
  note: string | null;
}
