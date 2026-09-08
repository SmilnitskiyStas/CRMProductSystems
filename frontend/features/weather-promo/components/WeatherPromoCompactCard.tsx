"use client";

import Link from "next/link";
import { useTranslations } from "next-intl";
import { ArrowRight, CloudSun, Sparkles } from "lucide-react";
import { useWeatherPromoSuggestions } from "../hooks/useWeatherPromo";

/**
 * Compact dashboard teaser. Reads the shared weather-promo cache and never triggers a generate —
 * self-hides unless there is at least one cached suggestion.
 */
export function WeatherPromoCompactCard() {
  const t = useTranslations("Dashboard.weatherPromo");
  const { data } = useWeatherPromoSuggestions({ enabled: false });

  if (!data || data.status !== "ok" || data.suggestions.length === 0) return null;

  const top = data.suggestions[0];
  const productNames = top.products.slice(0, 3).map((p) => p.name).join(", ");

  return (
    <Link
      href="/events"
      style={{
        display: "flex",
        alignItems: "flex-start",
        gap: 12,
        background: "#0D1117",
        border: "1px solid #1F2937",
        borderRadius: 12,
        padding: "16px 20px",
        textDecoration: "none",
      }}
    >
      <div
        style={{
          flexShrink: 0,
          width: 32,
          height: 32,
          borderRadius: 8,
          background: "#1D3461",
          display: "flex",
          alignItems: "center",
          justifyContent: "center",
        }}
      >
        <Sparkles size={16} color="#93C5FD" />
      </div>
      <div style={{ flex: 1, minWidth: 0 }}>
        <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", gap: 10 }}>
          <span
            style={{
              color: "#8A94A8",
              fontSize: 11,
              fontWeight: 600,
              textTransform: "uppercase",
              letterSpacing: "0.06em",
            }}
          >
            {t("compactTitle")}
          </span>
          <span style={{ display: "flex", alignItems: "center", gap: 4, color: "#93C5FD", fontSize: 12, flexShrink: 0 }}>
            {t("viewAll")} <ArrowRight size={13} />
          </span>
        </div>
        <div style={{ color: "#E8EDF5", fontSize: 14, fontWeight: 600, marginTop: 4 }}>{top.title}</div>
        <div style={{ display: "flex", alignItems: "center", gap: 6, color: "#6B7280", fontSize: 12, marginTop: 4 }}>
          <CloudSun size={13} style={{ flexShrink: 0 }} />
          <span>{top.weatherSummary}</span>
        </div>
        {productNames && (
          <div style={{ color: "#9CA3AF", fontSize: 12, marginTop: 4, overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>
            {productNames}
          </div>
        )}
      </div>
    </Link>
  );
}
