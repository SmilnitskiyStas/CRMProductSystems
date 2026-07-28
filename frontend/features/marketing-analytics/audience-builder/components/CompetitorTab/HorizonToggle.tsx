"use client";

import { useTranslations } from "next-intl";
import { useAudienceBuilderStore } from "../../store/useAudienceBuilderStore";
import type { CompetitorHorizon } from "../../types";

const HORIZONS: CompetitorHorizon[] = ["InPeriod", "AllTime"];

/**
 * "...у періоді" / "...будь-коли" — the two competitor-exclusion horizons (analysis §16/§17):
 * InPeriod excludes only customers who bought the own product in the SAME active window; AllTime
 * excludes anyone who EVER bought it. The two modes give substantially different audience sizes
 * (mandatory requirement — analysis §17.1 measured AllTime at ~23% of InPeriod's size on real
 * data) — the hint line makes the active rule explicit in plain language so that size gap reads as
 * intentional, not as a bug.
 */
export function HorizonToggle() {
  const t = useTranslations("Dashboard.audienceBuilder.horizonToggle");
  const horizon = useAudienceBuilderStore((s) => s.horizon);
  const setHorizon = useAudienceBuilderStore((s) => s.setHorizon);

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 6 }}>
      <div style={{ display: "flex", gap: 4 }}>
        {HORIZONS.map((h) => {
          const active = horizon === h;
          return (
            <button
              key={h}
              type="button"
              onClick={() => setHorizon(h)}
              style={{
                padding: "7px 14px",
                fontSize: 12,
                fontWeight: 600,
                borderRadius: 6,
                border: `1px solid ${active ? "#3B82F6" : "#1F2937"}`,
                cursor: "pointer",
                background: active ? "#1D3461" : "#0D1117",
                color: active ? "#93C5FD" : "#6B7280",
              }}
            >
              {t(h === "InPeriod" ? "inPeriod" : "allTime")}
            </button>
          );
        })}
      </div>
      <p style={{ color: "#4B5563", fontSize: 12, margin: 0, maxWidth: 480 }}>
        {t(horizon === "InPeriod" ? "inPeriodHint" : "allTimeHint")}
      </p>
    </div>
  );
}
