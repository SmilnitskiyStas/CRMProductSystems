import type { AudienceKind, ItemRef, MatchMode, RuleDefinition, RuleType } from "../types";

/** Everything the wizard edits. Numbers are kept as strings so the user can type "9,99". */
export interface WizardState {
  name: string;
  description: string;
  type: RuleType | null;
  items: ItemRef[];
  categories: ItemRef[];
  percent: string;
  amount: string;
  price: string;
  buyQty: string;
  getQty: string;
  getMode: "price" | "percent";
  getPrice: string;
  getPercent: string;
  matchMode: MatchMode;
  maxApplications: string;
  excludeAlreadyDiscounted: boolean;
  allStores: boolean;
  storeIds: string[];
  validFrom: string; // datetime-local value
  validTo: string;
  weekdays: number[];
  timeFrom: string;
  timeTo: string;
  priority: string;
  audienceKind: AudienceKind;
  tierIds: string[];
}

export const INITIAL_STATE: WizardState = {
  name: "",
  description: "",
  type: null,
  items: [],
  categories: [],
  percent: "10",
  amount: "",
  price: "",
  buyQty: "2",
  getQty: "1",
  getMode: "price",
  getPrice: "0,01",
  getPercent: "100",
  matchMode: "per_item",
  maxApplications: "",
  excludeAlreadyDiscounted: false,
  allStores: true,
  storeIds: [],
  validFrom: "",
  validTo: "",
  weekdays: [],
  timeFrom: "",
  timeTo: "",
  priority: "100",
  audienceKind: "all",
  tierIds: [],
};

/** "9,99" / "9.99" / " 10 " -> number; empty / garbage -> null. */
export function parseDecimal(raw: string): number | null {
  const s = raw.trim().replace(/\s/g, "").replace(",", ".");
  if (!/^\d+(\.\d+)?$|^\.\d+$/.test(s)) return null;
  const n = Number(s);
  return Number.isFinite(n) ? n : null;
}

export function parseIntStrict(raw: string): number | null {
  const s = raw.trim();
  return /^\d+$/.test(s) ? Number(s) : null;
}

/** Number -> input text with a decimal comma ("9,99"). */
export function formatDecimalInput(n: number | undefined | null): string {
  return n === undefined || n === null ? "" : String(n).replace(".", ",");
}

/** datetime-local ("2026-10-20T08:00", local time) -> ISO UTC; empty -> null. */
export function localToIso(v: string): string | null {
  if (!v) return null;
  const d = new Date(v);
  return Number.isNaN(d.getTime()) ? null : d.toISOString();
}

function pad(n: number) {
  return String(n).padStart(2, "0");
}

/** ISO UTC -> datetime-local value in the browser's local time; null -> "". */
export function isoToLocal(iso: string | null | undefined): string {
  if (!iso) return "";
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return "";
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

/** Wizard state -> the rule JSON the API stores. Params are emitted only for the chosen type. */
export function buildDefinition(s: WizardState): RuleDefinition {
  const params: RuleDefinition["params"] = {};
  switch (s.type) {
    case "percent_off": {
      const v = parseDecimal(s.percent);
      if (v !== null) params.percent = v;
      break;
    }
    case "fixed_off": {
      const v = parseDecimal(s.amount);
      if (v !== null) params.amount = v;
      break;
    }
    case "fixed_price": {
      const v = parseDecimal(s.price);
      if (v !== null) params.price = v;
      break;
    }
    case "buy_n_get_m": {
      const buy = parseIntStrict(s.buyQty);
      const get = parseIntStrict(s.getQty);
      if (buy !== null) params.buyQty = buy;
      if (get !== null) params.getQty = get;
      if (s.getMode === "price") {
        const v = parseDecimal(s.getPrice);
        if (v !== null) params.getPrice = v;
      } else {
        const v = parseDecimal(s.getPercent);
        if (v !== null) params.getPercent = v;
      }
      params.matchMode = s.matchMode;
      break;
    }
    default:
      break;
  }

  const audience: RuleDefinition["audience"] = { kind: s.audienceKind };
  if (s.audienceKind === "loyalty_tier") audience.tierIds = s.tierIds;

  return {
    // Step 1 gates the type; the fallback only keeps the function total for partial drafts.
    type: s.type ?? "percent_off",
    targets: { itemIds: s.items.map((i) => i.id), categoryIds: s.categories.map((c) => c.id) },
    scope: {
      storeIds: s.allStores ? [] : s.storeIds,
      validFrom: localToIso(s.validFrom),
      validTo: localToIso(s.validTo),
      weekdays: [...s.weekdays].sort((a, b) => a - b),
      timeFrom: s.timeFrom || null,
      timeTo: s.timeTo || null,
    },
    audience,
    params,
    limits: { maxApplicationsPerReceipt: parseIntStrict(s.maxApplications) },
    excludeAlreadyDiscounted: s.excludeAlreadyDiscounted,
    priority: parseIntStrict(s.priority) ?? 100,
  };
}

/**
 * Saved definition -> wizard state. Item/category names are not in the definition: callers pass
 * `names` (id -> name); unknown ids get an empty name (the UI shows a fallback label).
 */
export function stateFromDefinition(
  name: string,
  description: string | null,
  d: RuleDefinition,
  names: { items?: Map<string, string>; categories?: Map<string, string> } = {},
): WizardState {
  const p = d.params ?? {};
  return {
    ...INITIAL_STATE,
    name,
    description: description ?? "",
    type: d.type,
    items: (d.targets?.itemIds ?? []).map((id) => ({ id, name: names.items?.get(id) ?? "" })),
    categories: (d.targets?.categoryIds ?? []).map((id) => ({ id, name: names.categories?.get(id) ?? "" })),
    percent: p.percent !== undefined ? formatDecimalInput(p.percent) : INITIAL_STATE.percent,
    amount: formatDecimalInput(p.amount),
    price: formatDecimalInput(p.price),
    buyQty: p.buyQty !== undefined ? String(p.buyQty) : INITIAL_STATE.buyQty,
    getQty: p.getQty !== undefined ? String(p.getQty) : INITIAL_STATE.getQty,
    getMode: p.getPercent !== undefined && p.getPrice === undefined ? "percent" : "price",
    getPrice: p.getPrice !== undefined ? formatDecimalInput(p.getPrice) : INITIAL_STATE.getPrice,
    getPercent: p.getPercent !== undefined ? formatDecimalInput(p.getPercent) : INITIAL_STATE.getPercent,
    matchMode: p.matchMode ?? "per_item",
    maxApplications: d.limits?.maxApplicationsPerReceipt != null ? String(d.limits.maxApplicationsPerReceipt) : "",
    excludeAlreadyDiscounted: !!d.excludeAlreadyDiscounted,
    allStores: (d.scope?.storeIds ?? []).length === 0,
    storeIds: d.scope?.storeIds ?? [],
    validFrom: isoToLocal(d.scope?.validFrom),
    validTo: isoToLocal(d.scope?.validTo),
    weekdays: d.scope?.weekdays ?? [],
    timeFrom: d.scope?.timeFrom ?? "",
    timeTo: d.scope?.timeTo ?? "",
    priority: String(d.priority ?? 100),
    audienceKind: d.audience?.kind ?? "all",
    tierIds: d.audience?.tierIds ?? [],
  };
}

/**
 * Cheap client-side "is this step complete" gate (no server). Returns issue codes (see
 * messages.ts); the server /validate stays authoritative.
 */
export function stepProblems(s: WizardState, step: number): string[] {
  const out: string[] = [];
  if (step >= 1 && !s.type) out.push("type_required");
  if (step >= 1 && !s.name.trim()) out.push("name_required");
  if (step >= 2 && s.items.length + s.categories.length === 0) out.push("no_targets");
  if (step >= 3 && s.type) {
    if (s.type === "percent_off") {
      const v = parseDecimal(s.percent);
      if (v === null || v <= 0 || v > 100) out.push("percent_out_of_range");
    }
    if (s.type === "fixed_off") {
      const v = parseDecimal(s.amount);
      if (v === null || v <= 0) out.push("missing_param");
    }
    if (s.type === "fixed_price") {
      const v = parseDecimal(s.price);
      if (v === null) out.push("missing_param");
      else if (v < 0.01) out.push("price_below_minimum");
    }
    if (s.type === "buy_n_get_m") {
      const b = parseIntStrict(s.buyQty);
      const g = parseIntStrict(s.getQty);
      if (b === null || b < 1 || g === null || g < 1) out.push("invalid_quantity");
      if (s.getMode === "price") {
        const v = parseDecimal(s.getPrice);
        if (v === null) out.push("missing_param");
        else if (v < 0.01) out.push("price_below_minimum");
      } else {
        const v = parseDecimal(s.getPercent);
        if (v === null || v <= 0 || v > 100) out.push("percent_out_of_range");
      }
    }
  }
  if (step >= 4) {
    if (s.validFrom && s.validTo && new Date(s.validTo) < new Date(s.validFrom)) out.push("invalid_period");
    if (!s.allStores && s.storeIds.length === 0) out.push("no_stores_selected");
  }
  if (step >= 5 && s.audienceKind === "loyalty_tier" && s.tierIds.length === 0) out.push("tier_required");
  return out;
}

/** Store managers / network managers may only save rules scoped to their own stores. */
export function isStoreScopedRole(role: string | undefined | null): boolean {
  return role === "store_manager" || role === "network_manager";
}

/**
 * Default `scope.storeIds` for a NEW rule: scoped roles get the header-selected store when it is
 * one of their locations, else their first location; other roles keep "all stores" (empty).
 */
export function defaultStoreIds(
  role: string | undefined | null,
  primaryStoreId: string | undefined,
  locationIds: string[],
): string[] {
  if (!isStoreScopedRole(role)) return [];
  if (primaryStoreId && locationIds.includes(primaryStoreId)) return [primaryStoreId];
  return locationIds.length > 0 ? [locationIds[0]] : [];
}
