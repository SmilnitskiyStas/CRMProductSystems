"use client";

import { useTranslations } from "next-intl";

export type PriceSegmentsMode = "comparison" | "frequency" | "allTime";

interface Props {
  mode: PriceSegmentsMode;
  onModeChange: (m: PriceSegmentsMode) => void;
}

const MODES: PriceSegmentsMode[] = ["comparison", "frequency", "allTime"];

/**
 * Three modes — TABS, never separate pages/routes (brief: "Три режими — вкладки, НЕ окремі
 * сторінки"). "Весь час" is deliberately its own tab here rather than a period preset inside
 * ComparisonFilterBar (analysis doc §10 confirms the competitor treats it as a genuinely
 * different mode — different KPI set, no comparison cohort, no period concept at all — not just
 * an unbounded period). Comparison and Frequency share the SAME period+store filter state one
 * level up (page.tsx) — mirrors the competitor's own single top control governing both of its
 * tabs (analysis doc §4.1) — so switching between those two never resets the period/stores;
 * switching to/from Весь час simply stops using them (nothing is destroyed, so switching back
 * restores the last-used period rather than defaulting again).
 */
export function ModeTabs({ mode, onModeChange }: Props) {
  const t = useTranslations("Dashboard.priceSegments.modeTabs");

  return (
    <div style={{ display: "flex", gap: 4, borderBottom: "1px solid #1F2937" }}>
      {MODES.map((m) => {
        const active = mode === m;
        return (
          <button
            key={m}
            onClick={() => onModeChange(m)}
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
            {t(m)}
          </button>
        );
      })}
    </div>
  );
}
