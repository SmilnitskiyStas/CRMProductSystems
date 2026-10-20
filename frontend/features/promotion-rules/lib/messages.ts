import type { RuleDefinition } from "../types";

/** Server validation codes we ship a translation for (Dashboard.promotionRules.issues.<code>). */
export const ISSUE_CODES = [
  "no_targets",
  "missing_param",
  "percent_out_of_range",
  "price_below_minimum",
  "invalid_quantity",
  "invalid_period",
  "unknown_item",
  "unknown_category",
  "unknown_store",
  "unknown_tier",
  "tier_required",
  "store_scope_violation",
  "overlaps_other_rule",
  "all_stores",
  "already_expired",
  "promo_price_not_lower",
] as const;

/** Client-only codes (stepProblems) share the same `issues` namespace. */
export const CLIENT_CODES = ["type_required", "name_required", "no_stores_selected"] as const;

const KNOWN = new Set<string>([...ISSUE_CODES, ...CLIENT_CODES]);

/** Translation key (inside `issues`) for a code; unknown codes fall back to "unknown". */
export function issueKey(code: string): string {
  return KNOWN.has(code) ? code : "unknown";
}

const KNOWN_FIELDS = [
  "targets",
  "targets.itemIds",
  "targets.categoryIds",
  "params.percent",
  "params.amount",
  "params.price",
  "params.buyQty",
  "params.getQty",
  "params.getPrice",
  "params.getPercent",
  "scope.storeIds",
  "scope.validFrom",
  "scope.validTo",
  "audience.tierIds",
];

/** Translation key (inside `fields`) of the contract's field path, or null when not mapped. */
export function fieldKey(field: string | null): string | null {
  if (!field) return null;
  return KNOWN_FIELDS.includes(field) ? field.replace(/\./g, "_") : null;
}

/** Which wizard step (1..6) owns a validation field, for "go fix it" links. */
export function stepOfField(field: string | null): number {
  if (!field) return 6;
  if (field.startsWith("targets")) return 2;
  if (field.startsWith("params") || field.startsWith("limits")) return 3;
  if (field.startsWith("scope") || field === "priority") return 4;
  if (field.startsWith("audience")) return 5;
  return 6;
}

type T = (key: string, values?: Record<string, string | number>) => string;

export function formatMoney(n: number | undefined): string {
  return n === undefined
    ? "?"
    : n.toLocaleString("uk-UA", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
}

/** One-line plain-language description of the rule. `t` = the Dashboard.promotionRules translator. */
export function summarizeDefinition(d: RuleDefinition, t: T): string {
  const p = d.params ?? {};
  switch (d.type) {
    case "percent_off":
      return t("summary.percent_off", { percent: p.percent ?? "?" });
    case "fixed_off":
      return t("summary.fixed_off", { amount: formatMoney(p.amount) });
    case "fixed_price":
      return t("summary.fixed_price", { price: formatMoney(p.price) });
    case "buy_n_get_m": {
      const base = { buy: p.buyQty ?? "?", get: p.getQty ?? "?" };
      return p.getPercent !== undefined && p.getPrice === undefined
        ? t("summary.buy_n_get_m_percent", { ...base, percent: p.getPercent })
        : t("summary.buy_n_get_m_price", { ...base, price: formatMoney(p.getPrice) });
    }
    default:
      return "";
  }
}

/** "All stores" / "N stores" for the list. */
export function storesSummary(storeIds: string[], t: T): string {
  return storeIds.length === 0 ? t("stores.all") : t("stores.count", { count: storeIds.length });
}

/** "2 × 30,00 + 1 × 0,01" for the simulator. */
export function segmentsText(segments: { quantity: number; unitPrice: number }[]): string {
  return segments.map((s) => `${s.quantity} × ${formatMoney(s.unitPrice)}`).join(" + ");
}

/** Localized text for an API error: first `{errors:[{code}]}` entry by code, else the raw message. */
export function apiErrorText(
  err: unknown,
  t: (key: string) => string,
): string {
  const body = (err as { body?: { errors?: { code?: string }[] } } | null)?.body;
  const code = body?.errors?.[0]?.code;
  if (code) return t(`issues.${issueKey(code)}`);
  return err instanceof Error ? err.message : String(err);
}
