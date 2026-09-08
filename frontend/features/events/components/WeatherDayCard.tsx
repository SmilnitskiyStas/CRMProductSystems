"use client";

import { useTranslations } from "next-intl";
import type { WeatherDay } from "@/features/weather/types";
import { weatherBucketEmoji, weatherBucketLabelKey, weatherCodeBucket } from "@/features/weather/weatherCodes";

interface Props {
  weather?: WeatherDay;
}

const cardStyle: React.CSSProperties = {
  background: "#111827",
  border: "1px solid #1F2937",
  borderRadius: 10,
  padding: "12px 14px",
  marginBottom: 16,
};

const rowStyle: React.CSSProperties = {
  display: "flex",
  alignItems: "center",
  justifyContent: "space-between",
  fontSize: 12,
  padding: "3px 0",
};

/** Weather summary for one day — shown at the top of the day-detail drawer's list view.
 * Renders nothing when there's no weather for the day. */
export function WeatherDayCard({ weather }: Props) {
  const t = useTranslations("Dashboard.events.weather");

  if (!weather) return null;

  const bucket = weatherCodeBucket(weather.weatherCode);
  const rows: { label: string; value: string }[] = [];
  if (weather.tempMax != null) rows.push({ label: t("tempMax"), value: `${Math.round(weather.tempMax)}°C` });
  if (weather.tempMin != null) rows.push({ label: t("tempMin"), value: `${Math.round(weather.tempMin)}°C` });
  if (weather.tempAvg != null) rows.push({ label: t("tempAvg"), value: `${Math.round(weather.tempAvg)}°C` });
  if (weather.precipitation != null) rows.push({ label: t("precipitation"), value: t("precipitationValue", { value: Math.round(weather.precipitation * 10) / 10 }) });

  return (
    <div style={cardStyle}>
      <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", gap: 8, marginBottom: rows.length > 0 ? 8 : 0 }}>
        <span style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 600 }}>
          {bucket ? `${weatherBucketEmoji(bucket)} ${t(weatherBucketLabelKey(bucket))}` : "—"}
        </span>
        <span
          style={{
            fontSize: 10,
            fontWeight: 600,
            borderRadius: 5,
            padding: "2px 8px",
            flexShrink: 0,
            background: weather.isForecast ? "#78350F" : "#374151",
            color: weather.isForecast ? "#FCD34D" : "#D1D5DB",
          }}
        >
          {weather.isForecast ? t("forecastBadge") : t("actualBadge")}
        </span>
      </div>
      {rows.map((r) => (
        <div key={r.label} style={rowStyle}>
          <span style={{ color: "#6B7280" }}>{r.label}</span>
          <span style={{ color: "#E8EDF5", fontWeight: 500 }}>{r.value}</span>
        </div>
      ))}
    </div>
  );
}
