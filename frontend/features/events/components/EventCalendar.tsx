"use client";

import { useTranslations } from "next-intl";
import type { WeatherDay } from "@/features/weather/types";
import { weatherBucketEmoji, weatherCodeBucket } from "@/features/weather/weatherCodes";
import { EVENT_TYPE_STYLES, getEventTypeLabel, type DemandEvent } from "../types";
import { isEventActiveOnDate } from "../utils";

interface Props {
  year: number;
  month: number; // 1-12
  events: DemandEvent[];
  onEventClick: (event: DemandEvent) => void;
  onDayClick: (isoDate: string) => void;
  /** Weather per `yyyy-MM-dd`, when a single location is selected. */
  weatherByDate?: Map<string, WeatherDay>;
}

function iso(y: number, m: number, d: number): string {
  return `${y}-${String(m).padStart(2, "0")}-${String(d).padStart(2, "0")}`;
}

export function EventCalendar({ year, month, events, onEventClick, onDayClick, weatherByDate }: Props) {
  const t = useTranslations("Dashboard.events.calendar");
  const tTypes = useTranslations("Dashboard.events.types");
  const weekdayLabels = t.raw("weekdayLabels") as string[];
  const first = new Date(Date.UTC(year, month - 1, 1));
  const daysInMonth = new Date(Date.UTC(year, month, 0)).getUTCDate();
  const startOffset = (first.getUTCDay() + 6) % 7; // Monday-first
  const todayIso = new Date().toISOString().slice(0, 10);

  const cells: (number | null)[] = [
    ...Array.from({ length: startOffset }, () => null),
    ...Array.from({ length: daysInMonth }, (_, i) => i + 1),
  ];
  while (cells.length % 7 !== 0) cells.push(null);

  return (
    <div>
      <div style={{ display: "grid", gridTemplateColumns: "repeat(7, 1fr)", gap: 6, marginBottom: 6 }}>
        {weekdayLabels.map((d) => (
          <div key={d} style={{
            color: "#6B7280", fontSize: 11, fontWeight: 600,
            textTransform: "uppercase", textAlign: "center", padding: 4,
          }}>{d}</div>
        ))}
      </div>

      <div style={{ display: "grid", gridTemplateColumns: "repeat(7, 1fr)", gap: 6 }}>
        {cells.map((day, idx) => {
          if (day === null) {
            return <div key={`e${idx}`} style={{ minHeight: 88 }} />;
          }

          const dateIso = iso(year, month, day);
          const dayEvents = events.filter((ev) => isEventActiveOnDate(ev, dateIso));
          const isToday = dateIso === todayIso;

          const weather = weatherByDate?.get(dateIso);
          const showWeather = weather != null && weather.tempMax != null && weather.tempMin != null;
          const weatherBucket = weather ? weatherCodeBucket(weather.weatherCode) : null;
          const weatherEmoji = weatherBucket ? `${weatherBucketEmoji(weatherBucket)} ` : "";

          return (
            <div
              key={dateIso}
              onClick={() => onDayClick(dateIso)}
              style={{
                minHeight: 88,
                background: "#0D1117",
                border: isToday ? "1px solid #3B82F6" : "1px solid #1F2937",
                borderRadius: 10,
                padding: 8,
                cursor: "pointer",
              }}
            >
              <div style={{
                display: "flex", alignItems: "center", justifyContent: "space-between",
                gap: 4, marginBottom: 6,
              }}>
                <span style={{
                  color: isToday ? "#93C5FD" : "#6B7280",
                  fontSize: 12, fontWeight: isToday ? 700 : 500,
                }}>
                  {day}
                </span>
                {showWeather && weather && (
                  <span
                    style={{
                      display: "inline-flex", alignItems: "center", gap: 3,
                      color: "#9CA3AF", fontSize: 10, whiteSpace: "nowrap",
                      opacity: weather.isForecast ? 0.7 : 1,
                    }}
                  >
                    {weather.isForecast && (
                      <span style={{
                        width: 3, height: 3, borderRadius: "50%",
                        background: "#9CA3AF", flexShrink: 0,
                      }} />
                    )}
                    {weatherEmoji}
                    {Math.round(weather.tempMax!)}° / {Math.round(weather.tempMin!)}°
                  </span>
                )}
              </div>
              <div style={{ display: "flex", flexDirection: "column", gap: 3 }}>
                {dayEvents.slice(0, 3).map((ev) => {
                  const style = EVENT_TYPE_STYLES[ev.eventType];
                  const label = getEventTypeLabel(tTypes, ev.eventType);
                  return (
                    <div
                      key={ev.id}
                      onClick={(e) => { e.stopPropagation(); onEventClick(ev); }}
                      title={t("eventTooltip", { name: ev.name, type: label, count: ev.coefficients.length })}
                      style={{
                        background: style.bg, color: style.color,
                        fontSize: 10, borderRadius: 5, padding: "2px 6px",
                        whiteSpace: "nowrap", overflow: "hidden", textOverflow: "ellipsis",
                      }}
                    >
                      {ev.name}
                    </div>
                  );
                })}
                {dayEvents.length > 3 && (
                  <div style={{ color: "#4B5563", fontSize: 10 }}>{t("moreEvents", { count: dayEvents.length - 3 })}</div>
                )}
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
}
