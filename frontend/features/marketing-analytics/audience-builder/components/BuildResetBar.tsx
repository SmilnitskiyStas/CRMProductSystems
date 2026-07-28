"use client";

import { useTranslations, useLocale } from "next-intl";
import { Btn } from "@/components/ui/Btn";
import { useAudienceBuilderStore } from "../store/useAudienceBuilderStore";

/**
 * "Сформувати список" / "Скинути" (analysis §13). Build is disabled until at least one term
 * exists (mandatory requirement). Reset only appears once built (matches analysis: the button
 * shows up alongside the result tabs, not before) and clears the store AND notifies the parent
 * (`onAfterReset`) so page.tsx can also clear the page-local UI state this store doesn't own —
 * active result tab and each table's pagination (period/store filter is deliberately NOT reset,
 * see the store's own `reset()` doc comment).
 */
export function BuildResetBar({ onAfterReset }: { onAfterReset: () => void }) {
  const t = useTranslations("Dashboard.audienceBuilder.buildResetBar");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";

  const terms = useAudienceBuilderStore((s) => s.terms);
  const mode = useAudienceBuilderStore((s) => s.mode);
  const minQuantity = useAudienceBuilderStore((s) => s.minQuantity);
  const minAmount = useAudienceBuilderStore((s) => s.minAmount);
  const excludedItemIds = useAudienceBuilderStore((s) => s.excludedItemIds);
  const isBuilt = useAudienceBuilderStore((s) => s.isBuilt);
  const build = useAudienceBuilderStore((s) => s.build);
  const reset = useAudienceBuilderStore((s) => s.reset);

  function handleReset() {
    reset();
    onAfterReset();
  }

  const statusParts: string[] = [];
  if (isBuilt) {
    statusParts.push(
      terms.length > 1
        ? t("statusTermsMulti", { count: terms.length, mode: t(mode === "Any" ? "statusModeAny" : "statusModeAll") })
        : t("statusTermsSingle"),
    );
    if (minQuantity != null) statusParts.push(t("statusMinQuantity", { value: minQuantity.toLocaleString(intlLocale) }));
    if (minAmount != null) statusParts.push(t("statusMinAmount", { value: minAmount.toLocaleString(intlLocale) }));
    if (excludedItemIds.length > 0) statusParts.push(t("statusExcluded", { count: excludedItemIds.length }));
  }

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 8 }}>
      <div style={{ display: "flex", gap: 10, alignItems: "center", flexWrap: "wrap" }}>
        <Btn variant="primary" disabled={terms.length === 0} onClick={build}>
          {t("buildButton")}
        </Btn>
        {isBuilt && (
          <Btn variant="ghost" onClick={handleReset}>
            {t("resetButton")}
          </Btn>
        )}
      </div>

      {statusParts.length > 0 && <div style={{ color: "#6B7280", fontSize: 12 }}>{statusParts.join(" · ")}</div>}
    </div>
  );
}
