"use client";

import { useTranslations } from "next-intl";

export type AudienceResultTab = "buyers" | "competitor" | "matchedItems";

const TABS: AudienceResultTab[] = ["buyers", "competitor", "matchedItems"];

interface Props {
  tab: AudienceResultTab;
  onTabChange: (t: AudienceResultTab) => void;
}

/**
 * 3 result tabs — Покупці товару / Конкурентна аудиторія / Знайдені товари (analysis §3/§13).
 * Presentational only (mirrors price-segments/ModeTabs.tsx exactly) — page.tsx owns which tab is
 * currently active, this component just renders the header and reports clicks.
 */
export function ResultTabs({ tab, onTabChange }: Props) {
  const t = useTranslations("Dashboard.audienceBuilder.resultTabs");

  return (
    <div style={{ display: "flex", gap: 4, borderBottom: "1px solid #1F2937" }}>
      {TABS.map((tb) => {
        const active = tab === tb;
        return (
          <button
            key={tb}
            type="button"
            onClick={() => onTabChange(tb)}
            style={{
              padding: "10px 18px",
              background: "transparent",
              border: "none",
              borderBottom: active ? "2px solid #3B82F6" : "2px solid transparent",
              color: active ? "#3B82F6" : "#6B7280",
              fontSize: 13,
              fontWeight: active ? 600 : 400,
              cursor: "pointer",
              marginBottom: -1,
              transition: "color 0.15s",
            }}
          >
            {t(tb)}
          </button>
        );
      })}
    </div>
  );
}
