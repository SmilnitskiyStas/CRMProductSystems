"use client";

import { useState } from "react";
import { useTranslations } from "next-intl";
import type { ScheduleShiftDto, AddShiftPayload, UpdateShiftPayload, ShiftStatus } from "../types";
import { Btn } from "@/components/ui/Btn";

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

const SHIFT_STATUSES: ShiftStatus[] = ["scheduled", "confirmed", "completed", "cancelled"];

interface UserOption {
  id: string;
  name: string;
}

interface CreateProps {
  mode: "create";
  scheduleId: string;
  users: UserOption[];
  defaultDate?: string;
  isPending: boolean;
  error?: string | null;
  onClose: () => void;
  onSubmit: (data: AddShiftPayload) => void;
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
    userId?: string;
    shiftDate: string;
    startTime: string;
    endTime: string;
    breakMinutes: number;
  },
): Record<string, string> {
  const errors: Record<string, string> = {};
  if ("userId" in fields && !fields.userId) errors.userId = t("workerRequiredError");
  if (!fields.shiftDate) errors.shiftDate = t("dateRequiredError");
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
  const isEdit = props.mode === "edit";
  const shift = isEdit ? props.shift : null;

  const [userId, setUserId]           = useState(shift?.userId ?? "");
  const [shiftDate, setShiftDate]     = useState(shift?.shiftDate ?? (props.mode === "create" ? (props.defaultDate ?? "") : ""));
  const [startTime, setStartTime]     = useState(shift ? shift.startTime.slice(0, 5) : "09:00");
  const [endTime, setEndTime]         = useState(shift ? shift.endTime.slice(0, 5) : "18:00");
  const [breakMinutes, setBreak]      = useState(shift?.breakMinutes ?? 60);
  const [roleOverride, setRole]       = useState(shift?.roleOverride ?? "");
  const [notes, setNotes]             = useState(shift?.notes ?? "");
  const [status, setStatus]           = useState<ShiftStatus>(shift?.status ?? "scheduled");
  const [errors, setErrors]           = useState<Record<string, string>>({});

  function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    const errs = validate(t, { userId: isEdit ? undefined : userId, shiftDate, startTime, endTime, breakMinutes });
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
      (props as CreateProps).onSubmit({
        userId,
        locationId: "", // filled by parent from selected schedule locationId
        shiftDate,
        startTime,
        endTime,
        breakMinutes,
        roleOverride: roleOverride.trim() || undefined,
        notes: notes.trim() || undefined,
      });
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
          {/* Worker select — create mode only */}
          {!isEdit && (
            <div>
              <label style={labelStyle}>{t("workerLabel")}</label>
              <select
                value={userId}
                onChange={(e) => setUserId(e.target.value)}
                style={{ ...inputStyle, borderColor: errors.userId ? "#EF4444" : "#374151" }}
              >
                <option value="">{t("workerPlaceholder")}</option>
                {(props as CreateProps).users.map((u) => (
                  <option key={u.id} value={u.id}>{u.name}</option>
                ))}
              </select>
              {errors.userId && <p style={{ color: "#EF4444", fontSize: 11, marginTop: 4 }}>{errors.userId}</p>}
            </div>
          )}

          {/* Date */}
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
