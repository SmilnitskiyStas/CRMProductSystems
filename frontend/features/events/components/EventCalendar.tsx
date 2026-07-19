"use client";

import { useTranslations } from "next-intl";
import { EVENT_TYPE_STYLES, getEventTypeLabel, type DemandEvent } from "../types";

interface Props {
  year: number;
  month: number; // 1-12
  events: DemandEvent[];
  onEventClick: (event: DemandEvent) => void;
  onDayClick: (isoDate: string) => void;
}

function iso(y: number, m: number, d: number): string {
  return `${y}-${String(m).padStart(2, "0")}-${String(d).padStart(2, "0")}`;
}

/** Recurring events match by month/day each year, incl. windows wrapping over New Year. */
function isActiveOn(ev: DemandEvent, dateIso: string): boolean {
  if (!ev.isRecurring) return dateIso >= ev.startsAt && dateIso <= ev.endsAt;

  const md = dateIso.slice(5);          // "MM-dd"
  const s = ev.startsAt.slice(5);
  const e = ev.endsAt.slice(5);
  return s <= e ? md >= s && md <= e : md >= s || md <= e;
}

export function EventCalendar({ year, month, events, onEventClick, onDayClick }: Props) {
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
          const dayEvents = events.filter((ev) => isActiveOn(ev, dateIso));
          const isToday = dateIso === todayIso;

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
                color: isToday ? "#93C5FD" : "#6B7280",
                fontSize: 12, fontWeight: isToday ? 700 : 500, marginBottom: 6,
              }}>
                {day}
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
