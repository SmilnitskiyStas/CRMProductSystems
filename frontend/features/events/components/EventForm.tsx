"use client";

import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { Modal } from "@/components/ui/Modal";
import { Btn } from "@/components/ui/Btn";
import type { StoreDto as Store } from "@/features/stores/types";
import { EVENT_TYPE_META, type DemandEvent, type EventType, type UpsertEventPayload } from "../types";
import { useAddCoefficient, useUpdateCoefficient } from "../hooks/useEvents";
import { toast } from "sonner";
import { useState } from "react";

const eventSchema = z.object({
  name: z.string().min(1, "Обов'язкове поле").max(255),
  eventType: z.enum(["holiday", "promo", "local_event", "season_start", "custom"]),
  scope: z.enum(["network", "store"]),
  storeId: z.string().optional(),
  startsAt: z.string().min(1, "Вкажіть дату"),
  endsAt: z.string().min(1, "Вкажіть дату"),
  isRecurring: z.boolean(),
  notes: z.string().optional(),
});

type FormValues = z.infer<typeof eventSchema>;

interface Props {
  event: DemandEvent | null; // null = create
  initialDate?: string;
  stores: Store[];
  isPending: boolean;
  onClose: () => void;
  onSubmit: (payload: UpsertEventPayload) => void;
  onDelete?: (id: string) => void;
}

const inputStyle: React.CSSProperties = {
  width: "100%", background: "#111827", border: "1px solid #1F2937",
  borderRadius: 8, color: "#E8EDF5", fontSize: 13, padding: "8px 12px",
  outline: "none", boxSizing: "border-box",
};

const labelStyle: React.CSSProperties = {
  display: "block", color: "#9CA3AF", fontSize: 12, fontWeight: 500, marginBottom: 5,
};

const errStyle: React.CSSProperties = { color: "#F87171", fontSize: 11, marginTop: 3 };

export function EventForm({ event, initialDate, stores, isPending, onClose, onSubmit, onDelete }: Props) {
  const isEditing = event !== null;

  const { register, handleSubmit, watch, formState: { errors } } = useForm<FormValues>({
    resolver: zodResolver(eventSchema),
    defaultValues: event
      ? {
          name: event.name, eventType: event.eventType, scope: event.scope,
          storeId: event.storeId ?? "", startsAt: event.startsAt, endsAt: event.endsAt,
          isRecurring: event.isRecurring, notes: event.notes ?? "",
        }
      : {
          name: "", eventType: "custom", scope: "network", storeId: "",
          startsAt: initialDate ?? new Date().toISOString().slice(0, 10),
          endsAt: initialDate ?? new Date().toISOString().slice(0, 10),
          isRecurring: false, notes: "",
        },
  });

  const scope = watch("scope");

  function submit(v: FormValues) {
    onSubmit({
      name: v.name,
      eventType: v.eventType as EventType,
      scope: v.scope,
      storeId: v.scope === "store" ? v.storeId || null : null,
      startsAt: v.startsAt,
      endsAt: v.endsAt,
      isRecurring: v.isRecurring,
      notes: v.notes || null,
    });
  }

  return (
    <Modal title={isEditing ? "Редагувати подію" : "Нова подія"} onClose={onClose} width={560}>
      <form onSubmit={handleSubmit(submit)} style={{ display: "grid", gap: 14 }}>
        <div>
          <label style={labelStyle}>Назва</label>
          <input {...register("name")} style={inputStyle} placeholder="Ярмарок біля магазину" />
          {errors.name && <div style={errStyle}>{errors.name.message}</div>}
        </div>

        <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 12 }}>
          <div>
            <label style={labelStyle}>Тип</label>
            <select {...register("eventType")} style={inputStyle}>
              {Object.entries(EVENT_TYPE_META).map(([value, meta]) => (
                <option key={value} value={value}>{meta.label}</option>
              ))}
            </select>
          </div>
          <div>
            <label style={labelStyle}>Масштаб</label>
            <select {...register("scope")} style={inputStyle}>
              <option value="network">Вся мережа</option>
              <option value="store">Один магазин</option>
            </select>
          </div>
        </div>

        {scope === "store" && (
          <div>
            <label style={labelStyle}>Магазин</label>
            <select {...register("storeId")} style={inputStyle}>
              <option value="">— оберіть магазин —</option>
              {stores.map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}
            </select>
          </div>
        )}

        <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 12 }}>
          <div>
            <label style={labelStyle}>Початок</label>
            <input type="date" {...register("startsAt")} style={inputStyle} />
            {errors.startsAt && <div style={errStyle}>{errors.startsAt.message}</div>}
          </div>
          <div>
            <label style={labelStyle}>Кінець</label>
            <input type="date" {...register("endsAt")} style={inputStyle} />
            {errors.endsAt && <div style={errStyle}>{errors.endsAt.message}</div>}
          </div>
        </div>

        <label style={{ display: "flex", alignItems: "center", gap: 8, color: "#9CA3AF", fontSize: 13 }}>
          <input type="checkbox" {...register("isRecurring")} />
          Щорічна подія (повторюється за місяцем і днем)
        </label>

        <div>
          <label style={labelStyle}>Нотатки</label>
          <input {...register("notes")} style={inputStyle} placeholder="необов'язково" />
        </div>

        {isEditing && <CoefficientEditor event={event} />}

        <div style={{ display: "flex", justifyContent: "space-between", marginTop: 6 }}>
          <div>
            {isEditing && onDelete && (
              <Btn variant="danger" type="button" onClick={() => onDelete(event.id)}>
                Видалити
              </Btn>
            )}
          </div>
          <div style={{ display: "flex", gap: 10 }}>
            <Btn variant="ghost" type="button" onClick={onClose}>Скасувати</Btn>
            <Btn type="submit" disabled={isPending}>
              {isPending ? "Збереження…" : "Зберегти"}
            </Btn>
          </div>
        </div>
      </form>
    </Modal>
  );
}

/** Inline editor for event order-multipliers. Category/segment pickers — follow-up. */
function CoefficientEditor({ event }: { event: DemandEvent }) {
  const addCoef = useAddCoefficient();
  const updateCoef = useUpdateCoefficient();
  const [newK, setNewK] = useState("1.5");
  const [newScope, setNewScope] = useState("category");

  return (
    <div style={{ background: "#111827", border: "1px solid #1F2937", borderRadius: 8, padding: 12 }}>
      <div style={{ color: "#9CA3AF", fontSize: 12, fontWeight: 600, marginBottom: 8 }}>
        Коефіцієнти замовлення ({event.coefficients.length})
      </div>

      {event.coefficients.map((c) => (
        <div key={c.id} style={{ display: "flex", alignItems: "center", gap: 8, marginBottom: 6 }}>
          <span style={{ color: "#6B7280", fontSize: 12, width: 90 }}>{c.scopeType}</span>
          <span style={{ color: "#4B5563", fontSize: 10, fontFamily: "monospace", flex: 1 }}>
            {c.scopeId ? c.scopeId.slice(0, 8) : "(не прив'язано)"}
          </span>
          <input
            type="number" step="0.1" min={0.01} max={10} defaultValue={c.coefficient}
            onBlur={(e) => {
              const v = Number(e.target.value);
              if (v !== c.coefficient && v > 0) {
                updateCoef.mutate(
                  { eventId: event.id, coefId: c.id, coefficient: v },
                  { onSuccess: () => toast.success("Коефіцієнт оновлено"), onError: (err) => toast.error(err.message) },
                );
              }
            }}
            style={{ ...inputStyle, width: 80, padding: "4px 8px" }}
          />
        </div>
      ))}

      <div style={{ display: "flex", gap: 8, marginTop: 10 }}>
        <select value={newScope} onChange={(e) => setNewScope(e.target.value)}
          style={{ ...inputStyle, width: 110, padding: "4px 8px" }}>
          <option value="category">категорія</option>
          <option value="segment">сегмент</option>
          <option value="product">товар</option>
        </select>
        <input type="number" step="0.1" min={0.01} max={10} value={newK}
          onChange={(e) => setNewK(e.target.value)}
          style={{ ...inputStyle, width: 80, padding: "4px 8px" }} />
        <Btn size="sm" variant="ghost" type="button"
          onClick={() =>
            addCoef.mutate(
              { eventId: event.id, payload: { scopeType: newScope, scopeId: null, coefficient: Number(newK) } },
              { onSuccess: () => toast.success("Коефіцієнт додано"), onError: (err) => toast.error(err.message) },
            )
          }>
          Додати
        </Btn>
      </div>
    </div>
  );
}
