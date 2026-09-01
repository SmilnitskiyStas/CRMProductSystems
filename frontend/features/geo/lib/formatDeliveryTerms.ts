import type { DeliveryCoverageEntry } from "../types";

/**
 * Minimal translation-function shape this helper needs: a key plus optional
 * interpolation values. Compatible with the `t` returned by next-intl's
 * `useTranslations(...)`. Callers MUST scope it to `Dashboard.geo.deliveryTerms`.
 */
type TermsTranslator = (
  key: string,
  values?: Record<string, string | number>,
) => string;

/** `5000` → `"5000"`, `4999.9` → `"4999.9"` — drops insignificant decimals, no
 *  thousands separator (matches the backend `FormatDeliveryTerms` "0.##"). */
function formatAmount(amount: number): string {
  return String(Math.round(amount * 100) / 100);
}

/**
 * Builds a compact human-readable delivery-terms string from a served region's
 * structured fields (TASK-665):
 *
 *   - days: min + max        → "1–3 дні"
 *           min only         → "від 2 днів"
 *           max only         → "до 3 днів"
 *           min === max      → "2 дн."
 *           neither          → skipped
 *   - amount: minOrderAmount → "від 5000 грн"
 *
 * Present parts are joined with " · ". Returns "" when the entry carries no
 * structured data — callers substitute their own "за домовленістю" fallback.
 *
 * The per-region `entry.note` is deliberately NOT folded in here; callers render
 * it on its own muted line.
 */
export function formatDeliveryTerms(
  entry: DeliveryCoverageEntry,
  t: TermsTranslator,
): string {
  const parts: string[] = [];

  let min = entry.deliveryDaysMin;
  let max = entry.deliveryDaysMax;
  // Defensive: backend normalises reversed pairs, but never trust the wire.
  if (min != null && max != null && min > max) {
    [min, max] = [max, min];
  }

  if (min != null && max != null) {
    parts.push(
      min === max
        ? t("daysExact", { days: min })
        : t("daysRange", { min, max }),
    );
  } else if (min != null) {
    parts.push(t("daysFrom", { min }));
  } else if (max != null) {
    parts.push(t("daysTo", { max }));
  }

  if (entry.minOrderAmount != null && Number.isFinite(entry.minOrderAmount)) {
    parts.push(t("minAmount", { amount: formatAmount(entry.minOrderAmount) }));
  }

  return parts.join(" · ");
}
