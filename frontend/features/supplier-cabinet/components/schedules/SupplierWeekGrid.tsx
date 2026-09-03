"use client";

import { useState } from "react";
import { useTranslations } from "next-intl";
import { toast } from "sonner";
import { Plus } from "lucide-react";
import type {
  WorkScheduleDetailDto,
  ScheduleShiftDto,
  AddShiftPayload,
  UpdateShiftPayload,
} from "@/features/schedules/types";
import { ShiftCard } from "@/features/schedules/components/ShiftCard";
import { ShiftForm } from "@/features/schedules/components/ShiftForm";
import {
  useAddSupplierShift,
  useUpdateSupplierShift,
  useDeleteSupplierShift,
  useSupplierScheduleStaff,
} from "../../hooks/useSupplierSchedules";

// Supplier-portal expansion Phase 5 (plan D6). Fork of
// features/schedules/components/WeekGrid.tsx. The genuinely-presentational
// ShiftCard / ShiftForm are reused as-is from the retail feature; only the data
// layer differs — supplier cabinet shift hooks + the supplier staff picker
// (GET /api/supplier-cabinet/schedules/staff) instead of retail useUsers.

const DAY_KEYS = ["mon", "tue", "wed", "thu", "fri", "sat", "sun"] as const;

function addDays(dateStr: string, days: number): string {
  const d = new Date(dateStr);
  d.setDate(d.getDate() + days);
  return d.toISOString().slice(0, 10);
}

function formatDay(dateStr: string): string {
  const d = new Date(dateStr);
  return `${String(d.getDate()).padStart(2, "0")}.${String(d.getMonth() + 1).padStart(2, "0")}`;
}

function buildGrid(shifts: ScheduleShiftDto[]): Map<string, Map<string, ScheduleShiftDto[]>> {
  const grid = new Map<string, Map<string, ScheduleShiftDto[]>>();
  for (const shift of shifts) {
    if (!grid.has(shift.userId)) grid.set(shift.userId, new Map());
    const userMap = grid.get(shift.userId)!;
    if (!userMap.has(shift.shiftDate)) userMap.set(shift.shiftDate, []);
    userMap.get(shift.shiftDate)!.push(shift);
  }
  return grid;
}

function uniqueWorkers(shifts: ScheduleShiftDto[]): { userId: string; userName: string }[] {
  const seen = new Set<string>();
  const result: { userId: string; userName: string }[] = [];
  for (const s of shifts) {
    if (!seen.has(s.userId)) {
      seen.add(s.userId);
      result.push({ userId: s.userId, userName: s.userName });
    }
  }
  return result;
}

interface Props {
  schedule: WorkScheduleDetailDto;
  canManage: boolean;
}

export function SupplierWeekGrid({ schedule, canManage }: Props) {
  const t = useTranslations("Dashboard.schedules.weekGrid");
  const [addShiftCell, setAddShiftCell] = useState<{ userId?: string; date: string } | null>(null);
  const [editingShift, setEditingShift] = useState<ScheduleShiftDto | null>(null);

  const { data: staffData } = useSupplierScheduleStaff();
  const users = (staffData ?? []).map((u) => ({ id: u.id, name: u.fullName }));

  const addShiftMutation    = useAddSupplierShift(schedule.id);
  const updateShiftMutation = useUpdateSupplierShift(schedule.id);
  const deleteShiftMutation = useDeleteSupplierShift(schedule.id);

  const weekDates = Array.from({ length: 7 }, (_, i) => addDays(schedule.weekStart, i));
  const grid      = buildGrid(schedule.shifts);
  const workers   = uniqueWorkers(schedule.shifts);

  function handleAdd(data: AddShiftPayload) {
    addShiftMutation.mutate(
      { ...data, locationId: schedule.locationId },
      {
        onSuccess: () => { toast.success(t("addShiftToast")); setAddShiftCell(null); },
        onError:   (err) => toast.error(err.message),
      },
    );
  }

  function handleUpdate(data: UpdateShiftPayload) {
    if (!editingShift) return;
    updateShiftMutation.mutate(
      { shiftId: editingShift.id, data },
      {
        onSuccess: () => { toast.success(t("updateShiftToast")); setEditingShift(null); },
        onError:   (err) => toast.error(err.message),
      },
    );
  }

  function handleDeleteShift(shift: ScheduleShiftDto) {
    if (!confirm(t("deleteShiftConfirm", { name: shift.userName, date: shift.shiftDate }))) return;
    deleteShiftMutation.mutate(shift.id, {
      onSuccess: () => toast.success(t("deleteShiftToast")),
      onError:   (err) => toast.error(err.message),
    });
  }

  const CELL_W = 130;
  const ROW_H  = 48;

  return (
    <div style={{ overflowX: "auto" }}>
      <table style={{ borderCollapse: "collapse", width: "100%", minWidth: 700 }}>
        <thead>
          <tr>
            <th
              style={{
                width: 140,
                padding: "10px 12px",
                textAlign: "left",
                color: "#4B5563",
                fontSize: 12,
                fontWeight: 600,
                borderBottom: "1px solid #1F2937",
                background: "#0D1117",
                position: "sticky",
                left: 0,
                zIndex: 2,
              }}
            >
              {t("workerColumnHeader")}
            </th>
            {weekDates.map((date, i) => (
              <th
                key={date}
                style={{
                  width: CELL_W,
                  padding: "10px 8px",
                  textAlign: "center",
                  color: "#9CA3AF",
                  fontSize: 12,
                  fontWeight: 600,
                  borderBottom: "1px solid #1F2937",
                  borderLeft: "1px solid #1F2937",
                }}
              >
                <span style={{ color: "#6B7280" }}>{t(`dayShort.${DAY_KEYS[i]}`)}</span>{" "}
                <span>{formatDay(date)}</span>
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {workers.map(({ userId, userName }) => {
            const userMap = grid.get(userId) ?? new Map<string, ScheduleShiftDto[]>();
            return (
              <tr key={userId} style={{ height: ROW_H }}>
                <td
                  style={{
                    padding: "6px 12px",
                    color: "#E8EDF5",
                    fontSize: 13,
                    fontWeight: 500,
                    borderBottom: "1px solid #1F2937",
                    background: "#0D1117",
                    position: "sticky",
                    left: 0,
                    zIndex: 1,
                    whiteSpace: "nowrap",
                  }}
                >
                  {userName}
                </td>
                {weekDates.map((date) => {
                  const shifts = userMap.get(date) ?? [];
                  return (
                    <td
                      key={date}
                      style={{
                        padding: "4px 6px",
                        verticalAlign: "top",
                        borderBottom: "1px solid #1F2937",
                        borderLeft: "1px solid #1F2937",
                        minWidth: CELL_W,
                      }}
                    >
                      <div style={{ display: "flex", flexDirection: "column", gap: 3 }}>
                        {shifts.map((shift) => (
                          <ShiftCard
                            key={shift.id}
                            shift={shift}
                            onClick={() => canManage ? setEditingShift(shift) : undefined}
                          />
                        ))}
                        {canManage && (
                          <button
                            onClick={() => setAddShiftCell({ userId, date })}
                            style={{
                              background: "transparent",
                              border: "1px dashed #374151",
                              borderRadius: 6,
                              color: "#4B5563",
                              fontSize: 11,
                              cursor: "pointer",
                              padding: "3px 0",
                              transition: "border-color 0.1s, color 0.1s",
                            }}
                            onMouseEnter={(e) => {
                              (e.currentTarget as HTMLElement).style.borderColor = "#3B82F6";
                              (e.currentTarget as HTMLElement).style.color = "#93C5FD";
                            }}
                            onMouseLeave={(e) => {
                              (e.currentTarget as HTMLElement).style.borderColor = "#374151";
                              (e.currentTarget as HTMLElement).style.color = "#4B5563";
                            }}
                          >
                            <Plus size={11} style={{ display: "inline", verticalAlign: "middle" }} />
                          </button>
                        )}
                      </div>
                    </td>
                  );
                })}
              </tr>
            );
          })}

          {canManage && (
            <tr>
              <td
                colSpan={8}
                style={{ padding: "6px 12px", borderTop: "1px solid #1F2937" }}
              >
                <button
                  onClick={() => setAddShiftCell({ date: schedule.weekStart })}
                  style={{
                    background: "transparent",
                    border: "none",
                    color: "#4B5563",
                    fontSize: 12,
                    cursor: "pointer",
                    display: "flex",
                    alignItems: "center",
                    gap: 6,
                    padding: "4px 0",
                  }}
                  onMouseEnter={(e) => { (e.currentTarget as HTMLElement).style.color = "#93C5FD"; }}
                  onMouseLeave={(e) => { (e.currentTarget as HTMLElement).style.color = "#4B5563"; }}
                >
                  <Plus size={14} />
                  {t("addShiftButton")}
                </button>
              </td>
            </tr>
          )}
        </tbody>
      </table>

      {workers.length === 0 && (
        <div
          style={{
            padding: "48px 0",
            textAlign: "center",
            color: "#4B5563",
            fontSize: 13,
          }}
        >
          {t("emptyState")}{canManage ? ` ${t("emptyStateHint")}` : ""}
        </div>
      )}

      {addShiftCell && (
        <ShiftForm
          mode="create"
          scheduleId={schedule.id}
          users={users}
          defaultDate={addShiftCell.date}
          isPending={addShiftMutation.isPending}
          error={addShiftMutation.error?.message ?? null}
          onClose={() => { setAddShiftCell(null); addShiftMutation.reset(); }}
          onSubmit={handleAdd}
        />
      )}

      {editingShift && (
        <ShiftForm
          mode="edit"
          shift={editingShift}
          isPending={updateShiftMutation.isPending}
          error={updateShiftMutation.error?.message ?? null}
          onClose={() => { setEditingShift(null); updateShiftMutation.reset(); }}
          onSubmit={handleUpdate}
        />
      )}

      {/* Delete is reachable from the edit modal (status → "cancelled"); mirrors retail WeekGrid. */}
      <div style={{ display: "none" }}>{deleteShiftMutation.isPending}{handleDeleteShift.name}</div>
    </div>
  );
}
