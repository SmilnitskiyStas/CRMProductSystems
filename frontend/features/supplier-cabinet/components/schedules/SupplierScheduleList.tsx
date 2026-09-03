"use client";

import { useState } from "react";
import { useTranslations } from "next-intl";
import { Plus, Pencil, Trash2 } from "lucide-react";
import { toast } from "sonner";
import type {
  WorkScheduleDto,
  ScheduleStatus,
  CreateSchedulePayload,
  UpdateSchedulePayload,
} from "@/features/schedules/types";
import { SupplierScheduleForm } from "./SupplierScheduleForm";
import {
  useSupplierSchedules,
  useCreateSupplierSchedule,
  useUpdateSupplierSchedule,
  useDeleteSupplierSchedule,
} from "../../hooks/useSupplierSchedules";
import { Btn } from "@/components/ui/Btn";

// Supplier-portal expansion Phase 5 (plan D6). Fork of
// features/schedules/components/ScheduleList.tsx — same UI, wired to the supplier
// cabinet hooks + SupplierScheduleForm (warehouse picker).

const STATUS_STYLE: Record<ScheduleStatus, { bg: string; color: string; border: string; i18nKey: string }> = {
  draft:     { bg: "#111827", color: "#6B7280", border: "#374151", i18nKey: "statusDraft"     },
  published: { bg: "#052e16", color: "#4ADE80", border: "#16A34A", i18nKey: "statusPublished" },
  archived:  { bg: "#1F2937", color: "#4B5563", border: "#374151", i18nKey: "statusArchived"  },
};

function StatusBadge({ status }: { status: ScheduleStatus }) {
  const t = useTranslations("Dashboard.schedules.list");
  const s = STATUS_STYLE[status];
  return (
    <span
      style={{
        background: s.bg,
        color: s.color,
        border: `1px solid ${s.border}`,
        borderRadius: 20,
        padding: "2px 10px",
        fontSize: 11,
        fontWeight: 600,
        whiteSpace: "nowrap",
      }}
    >
      {t(s.i18nKey)}
    </span>
  );
}

function formatWeek(weekStart: string): string {
  const d = new Date(weekStart);
  const end = new Date(weekStart);
  end.setDate(end.getDate() + 6);
  const fmt = (dt: Date) =>
    `${String(dt.getDate()).padStart(2, "0")}.${String(dt.getMonth() + 1).padStart(2, "0")}`;
  return `${fmt(d)}–${fmt(end)}.${end.getFullYear()}`;
}

interface Props {
  selectedId: string | null;
  onSelect: (schedule: WorkScheduleDto) => void;
}

export function SupplierScheduleList({ selectedId, onSelect }: Props) {
  const t = useTranslations("Dashboard.schedules.list");
  const [createOpen, setCreateOpen] = useState(false);
  const [editing, setEditing] = useState<WorkScheduleDto | null>(null);

  const { data: schedules = [], isLoading } = useSupplierSchedules();
  const createMutation = useCreateSupplierSchedule();
  const updateMutation = useUpdateSupplierSchedule();
  const deleteMutation = useDeleteSupplierSchedule();

  function handleCreate(data: CreateSchedulePayload) {
    createMutation.mutate(data, {
      onSuccess: (created) => {
        toast.success(t("createdToast"));
        setCreateOpen(false);
        onSelect(created);
      },
      onError: (err) => toast.error(err.message),
    });
  }

  function handleUpdate(data: UpdateSchedulePayload) {
    if (!editing) return;
    updateMutation.mutate(
      { id: editing.id, data },
      {
        onSuccess: () => { toast.success(t("updatedToast")); setEditing(null); },
        onError: (err) => toast.error(err.message),
      },
    );
  }

  function handleDelete(schedule: WorkScheduleDto, e: React.MouseEvent) {
    e.stopPropagation();
    if (!confirm(t("deleteConfirm", { name: schedule.name }))) return;
    deleteMutation.mutate(schedule.id, {
      onSuccess: () => toast.success(t("deletedToast")),
      onError:   (err) => toast.error(err.message),
    });
  }

  return (
    <div>
      <div
        style={{
          display: "flex",
          alignItems: "center",
          justifyContent: "space-between",
          marginBottom: 12,
        }}
      >
        <h2 style={{ color: "#E8EDF5", fontSize: 14, fontWeight: 700, margin: 0 }}>
          {t("title", { count: schedules.length })}
        </h2>
        <Btn size="sm" icon={<Plus size={14} />} onClick={() => setCreateOpen(true)}>
          {t("newButton")}
        </Btn>
      </div>

      {isLoading ? (
        <p style={{ color: "#4B5563", fontSize: 13, padding: "12px 0" }}>{t("loading")}</p>
      ) : schedules.length === 0 ? (
        <p style={{ color: "#4B5563", fontSize: 13, padding: "12px 0" }}>{t("empty")}</p>
      ) : (
        <div style={{ display: "flex", flexDirection: "column", gap: 4 }}>
          {schedules.map((schedule) => {
            const isSelected = schedule.id === selectedId;
            return (
              <div
                key={schedule.id}
                onClick={() => onSelect(schedule)}
                style={{
                  background: isSelected ? "#1D3461" : "#111827",
                  border: `1px solid ${isSelected ? "#3B82F6" : "#1F2937"}`,
                  borderRadius: 10,
                  padding: "10px 12px",
                  cursor: "pointer",
                  transition: "background 0.1s, border-color 0.1s",
                }}
                onMouseEnter={(e) => {
                  if (!isSelected) {
                    (e.currentTarget as HTMLElement).style.background = "#161D2B";
                    (e.currentTarget as HTMLElement).style.borderColor = "#374151";
                  }
                }}
                onMouseLeave={(e) => {
                  if (!isSelected) {
                    (e.currentTarget as HTMLElement).style.background = "#111827";
                    (e.currentTarget as HTMLElement).style.borderColor = "#1F2937";
                  }
                }}
              >
                <div
                  style={{
                    display: "flex",
                    alignItems: "flex-start",
                    justifyContent: "space-between",
                    gap: 8,
                  }}
                >
                  <div style={{ flex: 1, minWidth: 0 }}>
                    <div
                      style={{
                        color: "#E8EDF5",
                        fontSize: 13,
                        fontWeight: 600,
                        whiteSpace: "nowrap",
                        overflow: "hidden",
                        textOverflow: "ellipsis",
                      }}
                    >
                      {schedule.name}
                    </div>
                    <div style={{ color: "#6B7280", fontSize: 11, marginTop: 2 }}>
                      {schedule.locationName} · {formatWeek(schedule.weekStart)}
                    </div>
                  </div>

                  <div style={{ display: "flex", gap: 4, flexShrink: 0 }}>
                    <button
                      onClick={(e) => { e.stopPropagation(); setEditing(schedule); }}
                      title={t("editTitle")}
                      style={{
                        background: "transparent",
                        border: "1px solid #374151",
                        borderRadius: 6,
                        padding: "3px 6px",
                        color: "#6B7280",
                        cursor: "pointer",
                      }}
                    >
                      <Pencil size={12} />
                    </button>
                    <button
                      onClick={(e) => handleDelete(schedule, e)}
                      title={t("deleteTitle")}
                      style={{
                        background: "transparent",
                        border: "1px solid #374151",
                        borderRadius: 6,
                        padding: "3px 6px",
                        color: "#6B7280",
                        cursor: "pointer",
                      }}
                    >
                      <Trash2 size={12} />
                    </button>
                  </div>
                </div>

                <div
                  style={{
                    display: "flex",
                    alignItems: "center",
                    justifyContent: "space-between",
                    marginTop: 8,
                  }}
                >
                  <StatusBadge status={schedule.status} />
                  <span style={{ color: "#4B5563", fontSize: 11 }}>
                    {t("shiftsCount", { count: schedule.shiftCount })}
                  </span>
                </div>
              </div>
            );
          })}
        </div>
      )}

      {createOpen && (
        <SupplierScheduleForm
          mode="create"
          isPending={createMutation.isPending}
          error={createMutation.error?.message ?? null}
          onClose={() => { setCreateOpen(false); createMutation.reset(); }}
          onSubmit={handleCreate}
        />
      )}

      {editing && (
        <SupplierScheduleForm
          mode="edit"
          schedule={editing}
          isPending={updateMutation.isPending}
          error={updateMutation.error?.message ?? null}
          onClose={() => { setEditing(null); updateMutation.reset(); }}
          onSubmit={handleUpdate}
        />
      )}
    </div>
  );
}
