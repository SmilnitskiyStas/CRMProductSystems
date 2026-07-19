"use client";

import { useState } from "react";
import { CalendarPlus, ChevronLeft, ChevronRight, Sparkles } from "lucide-react";
import { toast } from "sonner";
import { useTranslations, useLocale } from "next-intl";
import { Btn } from "@/components/ui/Btn";
import { EventCalendar } from "@/features/events/components/EventCalendar";
import { EventForm } from "@/features/events/components/EventForm";
import {
  useCreateEvent, useDeleteEvent, useEvents, useSeedDefaults, useUpdateEvent,
} from "@/features/events/hooks/useEvents";
import { useStores } from "@/features/stores/hooks/useStores";
import { EVENT_TYPES, EVENT_TYPE_STYLES, getEventTypeLabel, type DemandEvent, type UpsertEventPayload } from "@/features/events/types";

export default function EventsPage() {
  const t = useTranslations("Dashboard.events.page");
  const tTypes = useTranslations("Dashboard.events.types");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";
  const now = new Date();
  const [year, setYear] = useState(now.getFullYear());
  const [month, setMonth] = useState(now.getMonth() + 1);
  const [editing, setEditing] = useState<DemandEvent | null>(null);
  const [creating, setCreating] = useState<string | null>(null); // initial date or null

  const from = `${year}-${String(month).padStart(2, "0")}-01`;
  const to = `${year}-${String(month).padStart(2, "0")}-${new Date(year, month, 0).getDate()}`;

  const { data: events = [], isLoading } = useEvents(from, to);
  const { data: stores = [] } = useStores();
  const createEvent = useCreateEvent();
  const updateEvent = useUpdateEvent();
  const deleteEvent = useDeleteEvent();
  const seedDefaults = useSeedDefaults();

  const prev = () => (month === 1 ? (setMonth(12), setYear(year - 1)) : setMonth(month - 1));
  const next = () => (month === 12 ? (setMonth(1), setYear(year + 1)) : setMonth(month + 1));

  const handleCreate = (payload: UpsertEventPayload) =>
    createEvent.mutate(payload, {
      onSuccess: () => { toast.success(t("toastCreated")); setCreating(null); },
      onError: (err) => toast.error(err.message),
    });

  const handleUpdate = (payload: UpsertEventPayload) =>
    updateEvent.mutate({ id: editing!.id, payload }, {
      onSuccess: () => { toast.success(t("toastUpdated")); setEditing(null); },
      onError: (err) => toast.error(err.message),
    });

  const handleDelete = (id: string) =>
    deleteEvent.mutate(id, {
      onSuccess: () => { toast.success(t("toastDeleted")); setEditing(null); },
      onError: (err) => toast.error(err.message),
    });

  return (
    <div style={{ padding: "28px 32px" }}>
      {/* Header */}
      <div style={{ display: "flex", alignItems: "flex-start", justifyContent: "space-between", marginBottom: 22 }}>
        <div>
          <h1 style={{ color: "#E8EDF5", fontSize: 22, fontWeight: 700, margin: 0 }}>
            {t("title")}
          </h1>
          <p style={{ color: "#4B5563", fontSize: 13, marginTop: 6, marginBottom: 0 }}>
            {t("subtitle")}
          </p>
        </div>
        <div style={{ display: "flex", gap: 10 }}>
          <Btn variant="ghost" icon={<Sparkles size={15} />}
            onClick={() =>
              seedDefaults.mutate(undefined, {
                onSuccess: (r) =>
                  r.eventsCreated > 0
                    ? toast.success(t("toastSeeded", { count: r.eventsCreated }))
                    : toast.info(t("toastAlreadySeeded")),
                onError: (err) => toast.error(err.message),
              })
            }>
            {t("seedButton")}
          </Btn>
          <Btn icon={<CalendarPlus size={15} />} onClick={() => setCreating(from)}>
            {t("addEventButton")}
          </Btn>
        </div>
      </div>

      {/* Month navigation + legend */}
      <div style={{ display: "flex", alignItems: "center", gap: 12, marginBottom: 18 }}>
        <Btn size="sm" variant="ghost" onClick={prev}><ChevronLeft size={14} /></Btn>
        <span style={{ color: "#E8EDF5", fontSize: 16, fontWeight: 600, minWidth: 160, textAlign: "center" }}>
          {new Date(year, month - 1, 1).toLocaleDateString(intlLocale, { month: "long", year: "numeric" })}
        </span>
        <Btn size="sm" variant="ghost" onClick={next}><ChevronRight size={14} /></Btn>
        <Btn size="sm" variant="ghost"
          onClick={() => { setYear(now.getFullYear()); setMonth(now.getMonth() + 1); }}>
          {t("todayButton")}
        </Btn>

        <div style={{ display: "flex", gap: 10, marginLeft: "auto" }}>
          {EVENT_TYPES.map((key) => {
            const style = EVENT_TYPE_STYLES[key];
            return (
              <span key={key} style={{ display: "flex", alignItems: "center", gap: 5, color: "#6B7280", fontSize: 11 }}>
                <span style={{ width: 8, height: 8, borderRadius: 2, background: style.bg, border: `1px solid ${style.color}` }} />
                {getEventTypeLabel(tTypes, key)}
              </span>
            );
          })}
        </div>
      </div>

      {isLoading ? (
        <p style={{ color: "#4B5563", fontSize: 13 }}>{t("loading")}</p>
      ) : (
        <EventCalendar
          year={year}
          month={month}
          events={events}
          onEventClick={setEditing}
          onDayClick={setCreating}
        />
      )}

      {creating !== null && (
        <EventForm
          event={null}
          initialDate={creating}
          stores={stores}
          isPending={createEvent.isPending}
          onClose={() => setCreating(null)}
          onSubmit={handleCreate}
        />
      )}

      {editing !== null && (
        <EventForm
          event={editing}
          stores={stores}
          isPending={updateEvent.isPending}
          onClose={() => setEditing(null)}
          onSubmit={handleUpdate}
          onDelete={handleDelete}
        />
      )}
    </div>
  );
}
