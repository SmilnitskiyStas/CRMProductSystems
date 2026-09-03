"use client";

import { useState } from "react";
import { useTranslations } from "next-intl";
import type {
  WorkScheduleDto,
  CreateSchedulePayload,
  UpdateSchedulePayload,
  ScheduleStatus,
} from "@/features/schedules/types";
import { useSupplierWarehouses } from "../../hooks/useSupplierWarehouses";
import { Btn } from "@/components/ui/Btn";

// Supplier-portal expansion Phase 5 (plan D6). Fork of
// features/schedules/components/ScheduleForm.tsx — identical except the location picker
// is the supplier's own warehouses (useSupplierWarehouses) instead of retail useLocations.
// The retail component/page is live and deliberately untouched.

const inputStyle: React.CSSProperties = {
  width: "100%",
  background: "#0D1117",
  border: "1px solid #374151",
  borderRadius: 8,
  padding: "9px 12px",
  color: "#E8EDF5",
  fontSize: 13,
  outline: "none",
  boxSizing: "border-box",
};

const labelStyle: React.CSSProperties = {
  display: "block",
  color: "#9CA3AF",
  fontSize: 12,
  fontWeight: 500,
  marginBottom: 6,
};

const SCHEDULE_STATUSES: ScheduleStatus[] = ["draft", "published", "archived"];
const STATUS_I18N_KEY: Record<ScheduleStatus, string> = {
  draft: "statusDraft",
  published: "statusPublished",
  archived: "statusArchived",
};

/** Rounds a date string back to the nearest Monday (ISO YYYY-MM-DD). */
function toMonday(dateStr: string): string {
  const d = new Date(dateStr);
  const day = d.getDay(); // 0=Sun, 1=Mon ... 6=Sat
  const diff = day === 0 ? -6 : 1 - day;
  d.setDate(d.getDate() + diff);
  return d.toISOString().slice(0, 10);
}

interface CreateProps {
  mode: "create";
  isPending: boolean;
  error?: string | null;
  onClose: () => void;
  onSubmit: (data: CreateSchedulePayload) => void;
}

interface EditProps {
  mode: "edit";
  schedule: WorkScheduleDto;
  isPending: boolean;
  error?: string | null;
  onClose: () => void;
  onSubmit: (data: UpdateSchedulePayload) => void;
}

type Props = CreateProps | EditProps;

export function SupplierScheduleForm(props: Props) {
  const t = useTranslations("Dashboard.schedules.form");
  const tSup = useTranslations("Dashboard.supplierCabinet.schedules");
  const isEdit = props.mode === "edit";
  const schedule = isEdit ? props.schedule : null;

  const [name, setName] = useState(schedule?.name ?? "");
  const [locationId, setLocationId] = useState(schedule?.locationId ?? "");
  const [weekStart, setWeekStart] = useState(schedule?.weekStart ?? "");
  const [status, setStatus] = useState<ScheduleStatus>(schedule?.status ?? "draft");
  const [errors, setErrors] = useState<Record<string, string>>({});

  const { data: warehouses } = useSupplierWarehouses();
  const activeWarehouses = (warehouses ?? []).filter((w) => w.isActive);

  function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    const errs: Record<string, string> = {};
    if (!name.trim()) errs.name = t("nameRequiredError");
    if (!isEdit && !locationId) errs.locationId = tSup("warehouseRequiredError");
    if (!isEdit && !weekStart) errs.weekStart = t("weekStartRequiredError");
    setErrors(errs);
    if (Object.keys(errs).length > 0) return;

    if (isEdit) {
      (props as EditProps).onSubmit({ name: name.trim(), status });
    } else {
      (props as CreateProps).onSubmit({
        name: name.trim(),
        locationId,
        weekStart: toMonday(weekStart),
      });
    }
  }

  return (
    <>
      <div
        onClick={props.onClose}
        style={{
          position: "fixed",
          inset: 0,
          background: "rgba(0,0,0,0.6)",
          zIndex: 300,
          backdropFilter: "blur(2px)",
        }}
      />

      <div
        style={{
          position: "fixed",
          top: "50%",
          left: "50%",
          transform: "translate(-50%, -50%)",
          width: "min(460px, 95vw)",
          background: "#0D1117",
          border: "1px solid #1F2937",
          borderRadius: 14,
          zIndex: 301,
          display: "flex",
          flexDirection: "column",
          maxHeight: "90vh",
          overflow: "hidden",
        }}
      >
        <div
          style={{
            display: "flex",
            alignItems: "center",
            justifyContent: "space-between",
            padding: "18px 22px",
            borderBottom: "1px solid #1F2937",
            flexShrink: 0,
          }}
        >
          <h2 style={{ color: "#E8EDF5", fontSize: 15, fontWeight: 700, margin: 0 }}>
            {isEdit ? t("editTitle") : t("createTitle")}
          </h2>
          <button
            onClick={props.onClose}
            style={{
              background: "transparent",
              border: "1px solid #1F2937",
              borderRadius: 8,
              padding: "5px 9px",
              color: "#4B5563",
              fontSize: 16,
              cursor: "pointer",
            }}
          >
            ✕
          </button>
        </div>

        <form
          onSubmit={handleSubmit}
          style={{ padding: 22, overflowY: "auto", display: "flex", flexDirection: "column", gap: 14 }}
        >
          <div>
            <label style={labelStyle}>{t("nameLabel")}</label>
            <input
              value={name}
              onChange={(e) => setName(e.target.value)}
              placeholder={t("namePlaceholder")}
              style={{ ...inputStyle, borderColor: errors.name ? "#EF4444" : "#374151" }}
            />
            {errors.name && <p style={{ color: "#EF4444", fontSize: 11, marginTop: 4 }}>{errors.name}</p>}
          </div>

          {!isEdit && (
            <div>
              <label style={labelStyle}>{tSup("warehouseLabel")}</label>
              <select
                value={locationId}
                onChange={(e) => setLocationId(e.target.value)}
                style={{ ...inputStyle, borderColor: errors.locationId ? "#EF4444" : "#374151" }}
              >
                <option value="">{tSup("warehousePlaceholder")}</option>
                {activeWarehouses.map((w) => (
                  <option key={w.id} value={w.id}>{w.name}</option>
                ))}
              </select>
              {errors.locationId && <p style={{ color: "#EF4444", fontSize: 11, marginTop: 4 }}>{errors.locationId}</p>}
            </div>
          )}

          {!isEdit && (
            <div>
              <label style={labelStyle}>{t("weekStartLabel")}</label>
              <input
                type="date"
                value={weekStart}
                onChange={(e) => setWeekStart(e.target.value)}
                style={{ ...inputStyle, borderColor: errors.weekStart ? "#EF4444" : "#374151" }}
              />
              {weekStart && (
                <p style={{ color: "#6B7280", fontSize: 11, marginTop: 4 }}>
                  {t("mondaySnapHint", { date: toMonday(weekStart) })}
                </p>
              )}
              {errors.weekStart && <p style={{ color: "#EF4444", fontSize: 11, marginTop: 4 }}>{errors.weekStart}</p>}
            </div>
          )}

          {isEdit && (
            <div>
              <label style={labelStyle}>{t("statusLabel")}</label>
              <select
                value={status}
                onChange={(e) => setStatus(e.target.value as ScheduleStatus)}
                style={inputStyle}
              >
                {SCHEDULE_STATUSES.map((s) => (
                  <option key={s} value={s}>{t(STATUS_I18N_KEY[s])}</option>
                ))}
              </select>
            </div>
          )}

          {props.error && <p style={{ color: "#F87171", fontSize: 12 }}>{props.error}</p>}

          <div style={{ display: "flex", gap: 10, paddingTop: 4 }}>
            <Btn type="submit" disabled={props.isPending} style={{ flex: 1, justifyContent: "center" }}>
              {props.isPending ? t("savingButton") : isEdit ? t("saveButton") : t("createButton")}
            </Btn>
            <Btn type="button" variant="ghost" onClick={props.onClose}>
              {t("cancelButton")}
            </Btn>
          </div>
        </form>
      </div>
    </>
  );
}
