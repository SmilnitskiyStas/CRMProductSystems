"use client";

import { useTranslations } from "next-intl";
import { useAudienceBuilderStore } from "../store/useAudienceBuilderStore";
import { filterInputStyle, labelStyle } from "@/components/ui/fieldStyles";


function parseNumberOrNull(raw: string): number | null {
  if (raw.trim() === "") return null;
  const n = Number(raw);
  return Number.isFinite(n) ? n : null;
}

/** Мін. кількість / мін. сума — both optional, combined via AND when both set (analysis §11/§12).
 * Writes directly to the store. */
export function ThresholdInputs() {
  const t = useTranslations("Dashboard.audienceBuilder.thresholdInputs");
  const minQuantity = useAudienceBuilderStore((s) => s.minQuantity);
  const minAmount = useAudienceBuilderStore((s) => s.minAmount);
  const setMinQuantity = useAudienceBuilderStore((s) => s.setMinQuantity);
  const setMinAmount = useAudienceBuilderStore((s) => s.setMinAmount);

  return (
    <div style={{ display: "flex", gap: 16, flexWrap: "wrap" }}>
      <div>
        <label style={labelStyle}>{t("minQuantityLabel")}</label>
        <input
          type="number"
          min={0}
          inputMode="decimal"
          value={minQuantity ?? ""}
          onChange={(e) => setMinQuantity(parseNumberOrNull(e.target.value))}
          style={{ ...filterInputStyle, width: 170 }}
        />
      </div>
      <div>
        <label style={labelStyle}>{t("minAmountLabel")}</label>
        <input
          type="number"
          min={0}
          inputMode="decimal"
          value={minAmount ?? ""}
          onChange={(e) => setMinAmount(parseNumberOrNull(e.target.value))}
          style={{ ...filterInputStyle, width: 170 }}
        />
      </div>
    </div>
  );
}
