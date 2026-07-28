"use client";

import { useTranslations } from "next-intl";
import { useAudienceBuilderStore } from "../store/useAudienceBuilderStore";

const inputStyle: React.CSSProperties = {
  background: "#0D1117",
  border: "1px solid #1F2937",
  borderRadius: 6,
  color: "#E8EDF5",
  fontSize: 13,
  padding: "8px 10px",
  outline: "none",
  width: 170,
};
const labelStyle: React.CSSProperties = { color: "#4B5563", fontSize: 12, marginBottom: 4, display: "block" };

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
          style={inputStyle}
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
          style={inputStyle}
        />
      </div>
    </div>
  );
}
