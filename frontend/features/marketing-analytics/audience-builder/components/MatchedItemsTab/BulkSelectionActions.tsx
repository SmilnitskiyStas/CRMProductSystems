"use client";

import { useTranslations } from "next-intl";
import { Btn } from "@/components/ui/Btn";
import { useAudienceBuilderStore } from "../../store/useAudienceBuilderStore";

/**
 * "Обрати всі" / "Зняти показані" (analysis §20.5). "Обрати всі" clears the WHOLE exclusion list
 * (design doc §6 — not scoped to the current page) and disables once nothing is excluded anywhere,
 * mirroring the competitor's own confirmed disabled-state rule. "Зняти показані" only touches the
 * currently loaded page's item ids — never the full matched-items universe — and disables once
 * every row on the current page is already excluded (nothing left to remove from this page).
 */
export function BulkSelectionActions({ currentPageItemIds }: { currentPageItemIds: string[] }) {
  const t = useTranslations("Dashboard.audienceBuilder.bulkSelectionActions");
  const excludedItemIds = useAudienceBuilderStore((s) => s.excludedItemIds);
  const selectAllItems = useAudienceBuilderStore((s) => s.selectAllItems);
  const deselectShownItems = useAudienceBuilderStore((s) => s.deselectShownItems);

  const allShownAlreadyExcluded =
    currentPageItemIds.length > 0 && currentPageItemIds.every((id) => excludedItemIds.includes(id));

  return (
    <div style={{ display: "flex", gap: 8 }}>
      <Btn variant="ghost" size="sm" disabled={excludedItemIds.length === 0} onClick={selectAllItems}>
        {t("selectAll")}
      </Btn>
      <Btn
        variant="ghost"
        size="sm"
        disabled={currentPageItemIds.length === 0 || allShownAlreadyExcluded}
        onClick={() => deselectShownItems(currentPageItemIds)}
      >
        {t("deselectShown")}
      </Btn>
    </div>
  );
}
