"use client";

import { useState } from "react";
import { useTranslations } from "next-intl";
import type { ScheduleShiftDto, AddShiftPayload, UpdateShiftPayload, ShiftStatus } from "../types";
import { Btn } from "@/components/ui/Btn";
import { LocationsMultiSelectDropdown } from "@/features/users/components/LocationsMultiSelectDropdown";

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

const DAY_KEYS = ["mon", "tue", "wed", "thu", "fri", "sat", "sun"] as const;

const SHIFT_STATUSES: ShiftStatus[] = ["scheduled", "confirmed", "completed", "cancelled"];

interface UserOption {
  id: string;
  name: string;
}

interface CreateProps {
  mode: "create";
  scheduleId: string;
  users: UserOption[];
  /** The 7 dates (YYYY-MM-DD, Mon..Sun) of the schedule's week — day chips are built from these. */
  weekDates: string[];
  defaultUserId?: string;
  defaultDate?: string;
  isPending: boolean;
  error?: string | null;
  onClose: () => void;
  /** One payload per (worker x day) pair — the parent creates them all. */
  onSubmit: (data: AddShiftPayload[]) => void;
}

interface EditProps {
  mode: "edit";
  shift: ScheduleShiftDto;
  isPending: boolean;
  error?: string | null;
  onClose: () => void;
  onSubmit: (data: UpdateShiftPayload) => void;
}

type Props = CreateProps | EditProps;

function validate(
  t: (key: string) => string,
  fields: {
    userIds?: string[];
    shiftDates?: string[];
    shiftDate?: string;
    startTime: string;
    endTime: string;
    breakMinutes: number;
  },
): Record<string, string> {
  const errors: Record<string, string> = {};
  if (fields.userIds && fields.userIds.length === 0) errors.userId = t("workerRequiredError");
  if (fields.shiftDates && fields.shiftDates.length === 0) errors.shiftDate = t("dateRequiredError");
  if (fields.shiftDate !== undefined && !fields.shiftDate) errors.shiftDate = t("dateRequiredError");
  if (!fields.startTime) errors.startTime = t("startRequiredError");
  if (!fields.endTime) errors.endTime = t("endRequiredError");
  if (fields.startTime && fields.endTime && fields.startTime >= fields.endTime) {
    errors.endTime = t("endAfterStartError");
  }
  if (fields.breakMinutes < 0) errors.breakMinutes = t("breakNegativeError");
  return errors;
}

export function ShiftForm(props: Props) {
  const t = useTranslations("Dashboard.schedules.shiftForm");
  const tShiftStatus = useTranslations("Dashboard.schedules.shiftStatus");
  const tDay = useTranslations("Dashboard.schedules.weekGrid.dayShort");
  const isEdit = props.mode === "edit";
  const shift = isEdit ? props.shift : null;

  const [userIds, setUserIds]         = useState<string[]>(props.mode === "create" && props.defaultUserId ? [props.defaultUserId] : []);
  const [shiftDates, setShiftDates]   = useState<string[]>(props.mode === "create" && props.defaultDate ? [props.defaultDate] : []);
  const [shiftDate, setShiftDate]     = useState(shift?.shiftDate ?? "");
  const [startTime, setStartTime]     = useState(shift ? shift.startTime.slice(0, 5) : "09:00");
  const [endTime, setEndTime]         = useState(shift ? shift.endTime.slice(0, 5) : "18:00");
  const [breakMinutes, setBreak]      = useState(shift?.breakMinutes ?? 60);
  const [roleOverride, setRole]       = useState(shift?.roleOverride ?? "");
  const [notes, setNotes]             = useState(shift?.notes ?? "");
  const [status, setStatus]           = useState<ShiftStatus>(shift?.status ?? "scheduled");
  const [errors, setErrors]           = useState<Record<string, string>>({});

  function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    const errs = validate(
      t,
      isEdit
        ? { shiftDate, startTime, endTime, breakMinutes }
        : { userIds, shiftDates, startTime, endTime, breakMinutes },
    );
    setErrors(errs);
    if (Object.keys(errs).length > 0) return;

    if (isEdit) {
      (props as EditProps).onSubmit({
        startTime,
        endTime,
        breakMinutes,
        roleOverride: roleOverride.trim() || undefined,
        notes: notes.trim() || undefined,
        status,
      });
    } else {
      const dates = [...shiftDates].sort();
      (props as CreateProps).onSubmit(
        userIds.flatMap((userId) =>
          dates.map((date) => ({
            userId,
            locationId: "", // filled by parent from selected schedule locationId
            shiftDate: date,
            startTime,
            endTime,
            breakMinutes,
            roleOverride: roleOverride.trim() || undefined,
            notes: notes.trim() || undefined,
          })),
        ),
      );
    }
  }

  return (
    <>
      {/* Backdrop */}
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

      {/* Modal */}
      <div
        style={{
          position: "fixed",
          top: "50%",
          left: "50%",
          transform: "translate(-50%, -50%)",
          width: "min(480px, 95vw)",
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
        {/* Header */}
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

        {/* Form */}
        <form
          onSubmit={handleSubmit}
          style={{ padding: 22, overflowY: "auto", display: "flex", flexDirection: "column", gap: 14 }}
        >
          {/* Workers + days — create mode: multi-select, one shift per (worker x day) */}
          {!isEdit && (
            <>
              <div>
                <label style={labelStyle}>{t("workersLabel")}</label>
                {(props as CreateProps).users.length === 0 ? (
                  <div style={{ color: "#6B7280", fontSize: 12 }}>{t("noWorkers")}</div>
                ) : (
                  <LocationsMultiSelectDropdown
                    locations={(props as CreateProps).users}
                    selectedIds={userIds}
                    onToggle={(id) => setUserIds((p) => (p.includes(id) ? p.filter((x) => x !== id) : [...p, id]))}
                    summaryLabel={t("workersSelected", { count: userIds.length })}
                    placeholderLabel={t("workersPlaceholder")}
                    doneLabel={t("workersDone")}
                  />
                )}
                {errors.userId && <p style={{ color: "#EF4444", fontSize: 11, marginTop: 4 }}>{errors.userId}</p>}
              </div>

              <div>
                <label style={labelStyle}>{t("daysLabel")}</label>
                <div style={{ display: "flex", gap: 6, flexWrap: "wrap", marginBottom: 8 }}>
                  {(props as CreateProps).weekDates.map((d, i) => {
                    const on = shiftDates.includes(d);
                    return (
                      <button
                        type="button"
                        key={d}
                        onClick={() => setShiftDates((p) => (p.includes(d) ? p.filter((x) => x !== d) : [...p, d]))}
                        style={{
                          padding: "6px 10px", borderRadius: 8, fontSize: 12, cursor: "pointer",
                          border: `1px solid ${on ? "#3B82F6" : "#374151"}`,
                          background: on ? "#1D3461" : "transparent",
                          color: on ? "#93C5FD" : "#9CA3AF",
                        }}
                      >
                        {tDay(DAY_KEYS[i])} {d.slice(8, 10)}.{d.slice(5, 7)}
                      </button>
                    );
                  })}
                </div>
                <div style={{ display: "flex", gap: 8 }}>
                  <Btn type="button" variant="ghost" size="sm" onClick={() => setShiftDates((props as CreateProps).weekDates.slice(0, 5))}>
                    {t("presetWeekdays")}
                  </Btn>
                  <Btn type="button" variant="ghost" size="sm" onClick={() => setShiftDates((props as CreateProps).weekDates)}>
                    {t("presetAllWeek")}
                  </Btn>
                  <Btn type="button" variant="ghost" size="sm" onClick={() => setShiftDates([])}>
                    {t("presetClear")}
                  </Btn>
                </div>
                {errors.shiftDate && <p style={{ color: "#EF4444", fontSize: 11, marginTop: 4 }}>{errors.shiftDate}</p>}
                {userIds.length > 0 && shiftDates.length > 0 && (
                  <p style={{ color: "#6B7280", fontSize: 11, marginTop: 6 }}>
                    {t("willCreate", { count: userIds.length * shiftDates.length })}
                  </p>
                )}
              </div>
            </>
          )}

          {/* Date — edit mode only */}
          {isEdit && (
            <div>
              <label style={labelStyle}>{t("dateLabel")}</label>
              <input
                type="date"
                value={shiftDate}
                onChange={(e) => setShiftDate(e.target.value)}
                style={{ ...inputStyle, borderColor: errors.shiftDate ? "#EF4444" : "#374151" }}
              />
              {errors.shiftDate && <p style={{ color: "#EF4444", fontSize: 11, marginTop: 4 }}>{errors.shiftDate}</p>}
            </div>
          )}

          {/* Start / End */}
          <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 12 }}>
            <div>
              <label style={labelStyle}>{t("startLabel")}</label>
              <input
                type="time"
                value={startTime}
                onChange={(e) => setStartTime(e.target.value)}
                style={{ ...inputStyle, borderColor: errors.startTime ? "#EF4444" : "#374151" }}
              />
              {errors.startTime && <p style={{ color: "#EF4444", fontSize: 11, marginTop: 4 }}>{errors.startTime}</p>}
            </div>
            <div>
              <label style={labelStyle}>{t("endLabel")}</label>
              <input
                type="time"
                value={endTime}
                onChange={(e) => setEndTime(e.target.value)}
                style={{ ...inputStyle, borderColor: errors.endTime ? "#EF4444" : "#374151" }}
              />
              {errors.endTime && <p style={{ color: "#EF4444", fontSize: 11, marginTop: 4 }}>{errors.endTime}</p>}
            </div>
          </div>

          {/* Break */}
          <div>
            <label style={labelStyle}>{t("breakLabel")}</label>
            <input
              type="number"
              min={0}
              max={480}
              value={breakMinutes}
              onChange={(e) => setBreak(Number(e.target.value))}
              style={{ ...inputStyle, borderColor: errors.breakMinutes ? "#EF4444" : "#374151" }}
            />
            {errors.breakMinutes && <p style={{ color: "#EF4444", fontSize: 11, marginTop: 4 }}>{errors.breakMinutes}</p>}
          </div>

          {/* Role override */}
          <div>
            <label style={labelStyle}>{t("roleOverrideLabel")}</label>
            <input
              value={roleOverride}
              onChange={(e) => setRole(e.target.value)}
              placeholder={t("roleOverridePlaceholder")}
              style={inputStyle}
            />
          </div>

          {/* Status — edit only */}
          {isEdit && (
            <div>
              <label style={labelStyle}>{t("statusLabel")}</label>
              <select
                value={status}
                onChange={(e) => setStatus(e.target.value as ShiftStatus)}
                style={inputStyle}
              >
                {SHIFT_STATUSES.map((s) => (
                  <option key={s} value={s}>{tShiftStatus(s)}</option>
                ))}
              </select>
            </div>
          )}

          {/* Notes */}
          <div>
            <label style={labelStyle}>{t("notesLabel")}</label>
            <textarea
              value={notes}
              onChange={(e) => setNotes(e.target.value)}
              placeholder={t("notesPlaceholder")}
              rows={2}
              style={{ ...inputStyle, resize: "vertical", fontFamily: "inherit" }}
            />
          </div>

          {/* Server error */}
          {props.error && <p style={{ color: "#F87171", fontSize: 12 }}>{props.error}</p>}

          {/* Actions */}
          <div style={{ display: "flex", gap: 10, paddingTop: 4 }}>
            <Btn type="submit" disabled={props.isPending} style={{ flex: 1, justifyContent: "center" }}>
              {props.isPending ? t("savingButton") : isEdit ? t("saveButton") : t("addButton")}
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
