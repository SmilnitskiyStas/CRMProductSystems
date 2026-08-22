"use client";

import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { useTranslations } from "next-intl";
import { Modal } from "@/components/ui/Modal";
import { Btn } from "@/components/ui/Btn";
import type { StoreDto as Store } from "@/features/stores/types";
import { LocationsMultiSelectDropdown } from "@/features/users/components/LocationsMultiSelectDropdown";
import { EVENT_TYPES, getEventTypeLabel, type DemandEvent, type EventType, type UpsertEventPayload } from "../types";
import { useAddCoefficient, useUpdateCoefficient } from "../hooks/useEvents";
import { toast } from "sonner";
import { useMemo, useState } from "react";

// Zod .min(1, message) messages need translated text, so the schema is built once per
// render inside the component (see `useMemo(() => buildSchema(t), [t])` below) — mirrors
// `buildSchema(t)` in features/legal-entities/components/LegalEntityFormDialog.tsx (i18n
// Block 8b), which itself mirrors features/locations/components/LocationFormDialog.tsx
// (i18n Block 2).
function buildSchema(t: ReturnType<typeof useTranslations>) {
  return z
    .object({
      name: z.string().min(1, t("validationRequired")).max(255),
      eventType: z.enum(["holiday", "promo", "local_event", "season_start", "custom"]),
      scope: z.enum(["network", "store", "stores"]),
      storeId: z.string().optional(),
      storeIds: z.array(z.string()).optional(),
      startsAt: z.string().min(1, t("validationDateRequired")),
      endsAt: z.string().min(1, t("validationDateRequired")),
      isRecurring: z.boolean(),
      notes: z.string().optional(),
    })
    .refine((v) => v.scope !== "stores" || (v.storeIds && v.storeIds.length > 0), {
      message: t("validationStoresRequired"),
      path: ["storeIds"],
    });
}

type FormValues = z.infer<ReturnType<typeof buildSchema>>;

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
  const t = useTranslations("Dashboard.events.eventForm");
  const tTypes = useTranslations("Dashboard.events.types");
  const isEditing = event !== null;

  const schema = useMemo(() => buildSchema(t), [t]);

  const { register, handleSubmit, watch, setValue, formState: { errors } } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: event
      ? {
          name: event.name, eventType: event.eventType, scope: event.scope,
          storeId: event.storeId ?? "", storeIds: event.storeIds ?? [],
          startsAt: event.startsAt, endsAt: event.endsAt,
          isRecurring: event.isRecurring, notes: event.notes ?? "",
        }
      : {
          name: "", eventType: "custom", scope: "network", storeId: "", storeIds: [],
          startsAt: initialDate ?? new Date().toISOString().slice(0, 10),
          endsAt: initialDate ?? new Date().toISOString().slice(0, 10),
          isRecurring: false, notes: "",
        },
  });

  const scope = watch("scope");
  const storeIds = watch("storeIds") ?? [];

  function toggleStoreId(id: string) {
    const next = storeIds.includes(id) ? storeIds.filter((x) => x !== id) : [...storeIds, id];
    setValue("storeIds", next, { shouldValidate: true });
  }

  function submit(v: FormValues) {
    onSubmit({
      name: v.name,
      eventType: v.eventType as EventType,
      scope: v.scope,
      storeId: v.scope === "store" ? v.storeId || null : null,
      storeIds: v.scope === "stores" ? v.storeIds ?? [] : [],
      startsAt: v.startsAt,
      endsAt: v.endsAt,
      isRecurring: v.isRecurring,
      notes: v.notes || null,
    });
  }

  return (
    <Modal title={isEditing ? t("editTitle") : t("createTitle")} onClose={onClose} width={560}>
      <form onSubmit={handleSubmit(submit)} style={{ display: "grid", gap: 14 }}>
        <div>
          <label style={labelStyle}>{t("nameLabel")}</label>
          <input {...register("name")} style={inputStyle} placeholder={t("namePlaceholder")} />
          {errors.name && <div style={errStyle}>{errors.name.message}</div>}
        </div>

        <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 12 }}>
          <div>
            <label style={labelStyle}>{t("typeLabel")}</label>
            <select {...register("eventType")} style={inputStyle}>
              {EVENT_TYPES.map((value) => (
                <option key={value} value={value}>{getEventTypeLabel(tTypes, value)}</option>
              ))}
            </select>
          </div>
          <div>
            <label style={labelStyle}>{t("scopeLabel")}</label>
            <select {...register("scope")} style={inputStyle}>
              <option value="network">{t("scopeNetworkOption")}</option>
              <option value="store">{t("scopeStoreOption")}</option>
              <option value="stores">{t("scopeStoresOption")}</option>
            </select>
          </div>
        </div>

        {scope === "store" && (
          <div>
            <label style={labelStyle}>{t("storeLabel")}</label>
            <select {...register("storeId")} style={inputStyle}>
              <option value="">{t("storePlaceholder")}</option>
              {stores.map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}
            </select>
          </div>
        )}

        {scope === "stores" && (
          <div>
            <label style={labelStyle}>{t("storesLabel")}</label>
            <LocationsMultiSelectDropdown
              locations={stores}
              selectedIds={storeIds}
              onToggle={toggleStoreId}
              summaryLabel={t("storesSelectedCount", { count: storeIds.length })}
              placeholderLabel={t("storesPlaceholder")}
              doneLabel={t("storesDoneButton")}
            />
            {errors.storeIds && <div style={errStyle}>{errors.storeIds.message}</div>}
          </div>
        )}

        <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 12 }}>
          <div>
            <label style={labelStyle}>{t("startsAtLabel")}</label>
            <input type="date" {...register("startsAt")} style={inputStyle} />
            {errors.startsAt && <div style={errStyle}>{errors.startsAt.message}</div>}
          </div>
          <div>
            <label style={labelStyle}>{t("endsAtLabel")}</label>
            <input type="date" {...register("endsAt")} style={inputStyle} />
            {errors.endsAt && <div style={errStyle}>{errors.endsAt.message}</div>}
          </div>
        </div>

        <label style={{ display: "flex", alignItems: "center", gap: 8, color: "#9CA3AF", fontSize: 13 }}>
          <input type="checkbox" {...register("isRecurring")} />
          {t("recurringLabel")}
        </label>

        <div>
          <label style={labelStyle}>{t("notesLabel")}</label>
          <input {...register("notes")} style={inputStyle} placeholder={t("notesPlaceholder")} />
        </div>

        {isEditing && <CoefficientEditor event={event} />}

        <div style={{ display: "flex", justifyContent: "space-between", marginTop: 6 }}>
          <div>
            {isEditing && onDelete && (
              <Btn variant="danger" type="button" onClick={() => onDelete(event.id)}>
                {t("deleteButton")}
              </Btn>
            )}
          </div>
          <div style={{ display: "flex", gap: 10 }}>
            <Btn variant="ghost" type="button" onClick={onClose}>{t("cancelButton")}</Btn>
            <Btn type="submit" disabled={isPending}>
              {isPending ? t("savingButton") : t("saveButton")}
            </Btn>
          </div>
        </div>
      </form>
    </Modal>
  );
}

const COEFFICIENT_SCOPE_TYPES = ["category", "segment", "product"] as const;

/** Inline editor for event order-multipliers. Category/segment pickers — follow-up. */
function CoefficientEditor({ event }: { event: DemandEvent }) {
  const t = useTranslations("Dashboard.events.eventForm.coefficientEditor");
  const addCoef = useAddCoefficient();
  const updateCoef = useUpdateCoefficient();
  const [newK, setNewK] = useState("1.5");
  const [newScope, setNewScope] = useState("category");

  return (
    <div style={{ background: "#111827", border: "1px solid #1F2937", borderRadius: 8, padding: 12 }}>
      <div style={{ color: "#9CA3AF", fontSize: 12, fontWeight: 600, marginBottom: 8 }}>
        {t("title", { count: event.coefficients.length })}
      </div>

      {event.coefficients.map((c) => (
        <div key={c.id} style={{ display: "flex", alignItems: "center", gap: 8, marginBottom: 6 }}>
          <span style={{ color: "#6B7280", fontSize: 12, width: 90 }}>
            {t.has(`scopeTypes.${c.scopeType}`) ? t(`scopeTypes.${c.scopeType}`) : c.scopeType}
          </span>
          <span style={{ color: "#4B5563", fontSize: 10, fontFamily: "monospace", flex: 1 }}>
            {c.scopeId ? c.scopeId.slice(0, 8) : t("unbound")}
          </span>
          <input
            type="number" step="0.1" min={0.01} max={10} defaultValue={c.coefficient}
            onBlur={(e) => {
              const v = Number(e.target.value);
              if (v !== c.coefficient && v > 0) {
                updateCoef.mutate(
                  { eventId: event.id, coefId: c.id, coefficient: v },
                  { onSuccess: () => toast.success(t("toastUpdated")), onError: (err) => toast.error(err.message) },
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
          {COEFFICIENT_SCOPE_TYPES.map((scopeType) => (
            <option key={scopeType} value={scopeType}>{t(`scopeTypes.${scopeType}`)}</option>
          ))}
        </select>
        <input type="number" step="0.1" min={0.01} max={10} value={newK}
          onChange={(e) => setNewK(e.target.value)}
          style={{ ...inputStyle, width: 80, padding: "4px 8px" }} />
        <Btn size="sm" variant="ghost" type="button"
          onClick={() =>
            addCoef.mutate(
              { eventId: event.id, payload: { scopeType: newScope, scopeId: null, coefficient: Number(newK) } },
              { onSuccess: () => toast.success(t("toastAdded")), onError: (err) => toast.error(err.message) },
            )
          }>
          {t("addButton")}
        </Btn>
      </div>
    </div>
  );
}
