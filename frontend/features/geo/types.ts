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

/** One served region plus optional free-text delivery terms for that region. */
export interface DeliveryCoverageEntry {
  regionCode: string;
  terms: string | null;
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
