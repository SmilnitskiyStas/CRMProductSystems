"use client";

import { useState } from "react";
import { ChevronLeft } from "lucide-react";
import { useLocale, useTranslations } from "next-intl";
import { DetailDrawer } from "@/components/ui/DetailDrawer";
import { Btn } from "@/components/ui/Btn";
import type { StoreDto as Store } from "@/features/stores/types";
import { EVENT_TYPE_STYLES, getEventTypeLabel, type DemandEvent } from "../types";
import { isEventActiveOnDate } from "../utils";
import { EventDetailPanel } from "./EventDetailPanel";

interface Props {
  dateIso: string;
  allEvents: DemandEvent[];
  stores: Store[];
  onAddEvent: () => void;
  onEditEvent: (event: DemandEvent) => void;
  onClose: () => void;
}

const LIST_WIDTH = 480;
const DETAIL_WIDTH = 860;

export function EventDayDetailDrawer({ dateIso, allEvents, stores, onAddEvent, onEditEvent, onClose }: Props) {
  const t = useTranslations("Dashboard.events.dayDetail");
  const tTypes = useTranslations("Dashboard.events.types");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";
  const [selectedEventId, setSelectedEventId] = useState<string | null>(null);

  const dayEvents = allEvents.filter((ev) => isEventActiveOnDate(ev, dateIso));
  const selectedEvent = selectedEventId
    ? dayEvents.find((ev) => ev.id === selectedEventId) ?? null
    : null;

  const formattedDate = new Date(`${dateIso}T00:00:00`).toLocaleDateString(intlLocale, {
    day: "numeric",
    month: "long",
    year: "numeric",
  });

  function handleClose() {
    setSelectedEventId(null);
    onClose();
  }

  if (selectedEvent) {
    return (
      <DetailDrawer
        isOpen
        onClose={handleClose}
        title={selectedEvent.name}
        subtitle={formattedDate}
        width={DETAIL_WIDTH}
        actions={
          <Btn size="sm" variant="ghost" icon={<ChevronLeft size={14} />} onClick={() => setSelectedEventId(null)}>
            {t("backButton")}
          </Btn>
        }
      >
        <EventDetailPanel
          event={selectedEvent}
          stores={stores}
          referenceDateIso={dateIso}
          onEdit={onEditEvent}
          onBack={() => setSelectedEventId(null)}
        />
      </DetailDrawer>
    );
  }

  return (
    <DetailDrawer
      isOpen
      onClose={handleClose}
      title={t("drawerTitle", { date: formattedDate })}
      width={LIST_WIDTH}
    >
      {dayEvents.length === 0 ? (
        <div style={{ textAlign: "center", padding: "24px 0" }}>
          <p style={{ color: "#4B5563", fontSize: 13, marginBottom: 14 }}>{t("emptyState")}</p>
          <Btn size="sm" onClick={onAddEvent}>{t("addEventButton")}</Btn>
        </div>
      ) : (
        <>
          <div style={{ display: "flex", flexDirection: "column", gap: 8, marginBottom: 14 }}>
            {dayEvents.map((ev) => {
              const style = EVENT_TYPE_STYLES[ev.eventType];
              const label = getEventTypeLabel(tTypes, ev.eventType);
              return (
                <div
                  key={ev.id}
                  onClick={() => setSelectedEventId(ev.id)}
                  style={{
                    background: "#111827",
                    border: "1px solid #1F2937",
                    borderRadius: 10,
                    padding: "10px 12px",
                    cursor: "pointer",
                    display: "flex",
                    flexDirection: "column",
                    gap: 6,
                  }}
                >
                  <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", gap: 8 }}>
                    <span style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 600 }}>{ev.name}</span>
                    <span
                      style={{
                        background: style.bg,
                        color: style.color,
                        fontSize: 10,
                        borderRadius: 5,
                        padding: "2px 8px",
                        flexShrink: 0,
                      }}
                    >
                      {label}
                    </span>
                  </div>
                  <span style={{ color: "#6B7280", fontSize: 12 }}>{ev.startsAt} – {ev.endsAt}</span>
                </div>
              );
            })}
          </div>
          <Btn size="sm" variant="ghost" onClick={onAddEvent}>{t("addAnotherButton")}</Btn>
        </>
      )}
    </DetailDrawer>
  );
}
