"use client";

import { useState } from "react";
import { useTranslations, useLocale } from "next-intl";
import { Plus, ClipboardList, List, CalendarDays } from "lucide-react";
import {
  useSupplierTasks,
  useCreateSupplierTask,
  useUpdateSupplierTask,
  useUpdateSupplierTaskStatus,
  useCabinetStaff,
} from "../hooks/useSupplierCabinet";
import type { SupplierTaskDto, SupplierTaskStatus } from "../types";
import { Btn } from "@/components/ui/Btn";
import { Modal } from "@/components/ui/Modal";
import { StatusPill } from "@/components/ui/StatusPill";
import { inputStyle, filterInputStyle } from "@/components/ui/fieldStyles";
import { TasksCalendar } from "./TasksCalendar";

const STATUS_COLORS: Record<SupplierTaskStatus, { bg: string; color: string; border: string }> = {
  pending:     { bg: "#1F2937",   color: "#9CA3AF", border: "#374151" },
  in_progress: { bg: "#1E3A5F22", color: "#60A5FA", border: "#1D4ED855" },
  completed:   { bg: "#1a3a2e",   color: "#4ADE80", border: "#065F46" },
  cancelled:   { bg: "#2d0a0a",   color: "#F87171", border: "#7F1D1D" },
};

const ALL_STATUSES: SupplierTaskStatus[] = ["pending", "in_progress", "completed", "cancelled"];

function StatusBadge({ status }: { status: SupplierTaskStatus }) {
  const t = useTranslations("Dashboard.supplierCabinet.taskStatus");
  const c = STATUS_COLORS[status];
  return (
    <StatusPill bg={c.bg} text={c.color}>
      {t(status)}
    </StatusPill>
  );
}

type ViewMode = "list" | "calendar";

export function TasksBoard() {
  const t = useTranslations("Dashboard.supplierCabinet.tasksBoard");
  const tTaskStatus = useTranslations("Dashboard.supplierCabinet.taskStatus");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";
  const [view, setView] = useState<ViewMode>("list");
  const [scope, setScope] = useState<"mine" | "all">("all");
  const [clientFilter, setClientFilter] = useState("");
  const [statusFilter, setStatusFilter] = useState<SupplierTaskStatus | "">("");
  const [creating, setCreating] = useState(false);
  const [editTask, setEditTask] = useState<SupplierTaskDto | null>(null);
  const [prefillDueDate, setPrefillDueDate] = useState<string | null>(null);

  const { data: tasks, isLoading, isError } = useSupplierTasks({
    assignedToMe: scope === "mine" ? true : undefined,
    clientTenantId: clientFilter.trim() || undefined,
    status: statusFilter || undefined,
  });
  const updateStatus = useUpdateSupplierTaskStatus();

  function handleAddTaskForDay(dueDate: string) {
    setPrefillDueDate(dueDate);
    setCreating(true);
  }

  function closeCreate() {
    setCreating(false);
    setPrefillDueDate(null);
  }

  return (
    <div
      style={{
        background: "#111827", border: "1px solid #1F2937",
        borderRadius: 12, padding: "20px 24px",
      }}
    >
      <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginBottom: 16, gap: 12, flexWrap: "wrap" }}>
        <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
          <ClipboardList size={18} color="#60A5FA" />
          <h2 style={{ color: "#E8EDF5", fontSize: 16, fontWeight: 600, margin: 0 }}>
            {t("title")}
          </h2>
          {tasks && (
            <span style={{
              padding: "2px 8px", borderRadius: 10,
              background: "#1F2937", color: "#9CA3AF", fontSize: 11,
            }}>
              {tasks.length}
            </span>
          )}
        </div>
        <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
          <div style={{ display: "flex", background: "#0D1117", border: "1px solid #1F2937", borderRadius: 8, padding: 3, gap: 4 }}>
            <Btn variant={view === "list" ? "primary" : "ghost"} size="sm" icon={<List size={13} />} onClick={() => setView("list")}>
              {t("listViewButton")}
            </Btn>
            <Btn variant={view === "calendar" ? "primary" : "ghost"} size="sm" icon={<CalendarDays size={13} />} onClick={() => setView("calendar")}>
              {t("calendarViewButton")}
            </Btn>
          </div>
          <Btn icon={<Plus size={14} />} onClick={() => setCreating(true)}>
            {t("newTaskButton")}
          </Btn>
        </div>
      </div>

      {view === "list" && (
        <>
          {/* Filters */}
          <div style={{ display: "flex", gap: 10, marginBottom: 16, flexWrap: "wrap", alignItems: "center" }}>
            <div style={{ display: "flex", background: "#0D1117", border: "1px solid #1F2937", borderRadius: 8, padding: 3, gap: 4 }}>
              <Btn variant={scope === "mine" ? "primary" : "ghost"} size="sm" onClick={() => setScope("mine")}>
                {t("scopeMine")}
              </Btn>
              <Btn variant={scope === "all" ? "primary" : "ghost"} size="sm" onClick={() => setScope("all")}>
                {t("scopeAll")}
              </Btn>
            </div>

            <input
              value={clientFilter}
              onChange={(e) => setClientFilter(e.target.value)}
              placeholder={t("clientFilterPlaceholder")}
              style={{ ...filterInputStyle, minWidth: 220 }}
            />

            <select
              value={statusFilter}
              onChange={(e) => setStatusFilter(e.target.value as SupplierTaskStatus | "")}
              style={{ ...filterInputStyle, appearance: "none" }}
            >
              <option value="">{t("allStatusesOption")}</option>
              {ALL_STATUSES.map((s) => (
                <option key={s} value={s}>{tTaskStatus(s)}</option>
              ))}
            </select>
          </div>

          {isLoading && <div style={{ color: "#4B5563", fontSize: 13 }}>{t("loading")}</div>}
          {isError && <div style={{ color: "#F87171", fontSize: 13 }}>{t("errorLoad")}</div>}

          {!isLoading && !isError && (
            <div style={{ display: "flex", flexDirection: "column", gap: 8 }}>
              {tasks?.map((task) => (
                <div
                  key={task.id}
                  style={{
                    background: "#0D1117", border: "1px solid #1F2937", borderRadius: 10,
                    padding: "12px 16px", display: "flex", alignItems: "flex-start", gap: 12,
                    cursor: "pointer",
                  }}
                  onClick={() => setEditTask(task)}
                >
                  <div style={{ flex: 1, minWidth: 0 }}>
                    <div style={{ display: "flex", alignItems: "center", gap: 8, marginBottom: 4, flexWrap: "wrap" }}>
                      <span style={{ color: "#E8EDF5", fontSize: 14, fontWeight: 600 }}>{task.title}</span>
                      <StatusBadge status={task.status} />
                    </div>
                    {task.description && (
                      <div style={{ color: "#6B7280", fontSize: 12, marginBottom: 6 }}>{task.description}</div>
                    )}
                    <div style={{ display: "flex", gap: 14, flexWrap: "wrap", color: "#4B5563", fontSize: 11 }}>
                      {task.clientTenantName && <span>{t("clientLabel", { name: task.clientTenantName })}</span>}
                      {task.assignedToUserName && <span>{t("assigneeLabel", { name: task.assignedToUserName })}</span>}
                      {task.dueDate && <span>{t("dueDateLabel", { date: new Date(task.dueDate).toLocaleDateString(intlLocale) })}</span>}
                    </div>
                  </div>

                  <select
                    value={task.status}
                    onClick={(e) => e.stopPropagation()}
                    onChange={(e) =>
                      updateStatus.mutate({ id: task.id, body: { status: e.target.value as SupplierTaskStatus } })
                    }
                    style={{ ...filterInputStyle, width: 140, flexShrink: 0 }}
                  >
                    {ALL_STATUSES.map((s) => (
                      <option key={s} value={s}>{tTaskStatus(s)}</option>
                    ))}
                  </select>
                </div>
              ))}

              {(!tasks || tasks.length === 0) && (
                <div style={{ color: "#4B5563", fontSize: 13, textAlign: "center", padding: "24px 0" }}>
                  {t("empty")}
                </div>
              )}
            </div>
          )}
        </>
      )}

      {view === "calendar" && (
        <>
          {isLoading && <div style={{ color: "#4B5563", fontSize: 13 }}>{t("loading")}</div>}
          {isError && <div style={{ color: "#F87171", fontSize: 13 }}>{t("errorLoad")}</div>}
          {!isLoading && !isError && (
            <TasksCalendar tasks={tasks ?? []} onAddTask={handleAddTaskForDay} />
          )}
        </>
      )}

      {creating && <TaskFormModal defaultDueDate={prefillDueDate ?? undefined} onClose={closeCreate} />}
      {editTask && <TaskFormModal task={editTask} onClose={() => setEditTask(null)} />}
    </div>
  );
}

/* ─────────────────────────── Task form modal ─────────────────────────── */

interface TaskFormModalProps {
  task?: SupplierTaskDto;
  /** Prefills the due date field — used when adding a task from a calendar day cell. */
  defaultDueDate?: string;
  onClose: () => void;
}

function TaskFormModal({ task, defaultDueDate, onClose }: TaskFormModalProps) {
  const t = useTranslations("Dashboard.supplierCabinet.tasksBoard.modal");
  const create = useCreateSupplierTask();
  const update = useUpdateSupplierTask();
  const { data: staff } = useCabinetStaff();

  const [title, setTitle] = useState(task?.title ?? "");
  const [description, setDescription] = useState(task?.description ?? "");
  const [clientTenantId, setClientTenantId] = useState(task?.clientTenantId ?? "");
  const [assignedToUserId, setAssignedToUserId] = useState(task?.assignedToUserId ?? "");
  const [dueDate, setDueDate] = useState(
    task?.dueDate ? task.dueDate.slice(0, 10) : (defaultDueDate ?? "")
  );
  const [error, setError] = useState<string | null>(null);

  const isEdit = !!task;
  const isPending = create.isPending || update.isPending;

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    const req = {
      title,
      description: description.trim() || null,
      clientTenantId: clientTenantId.trim() || null,
      assignedToUserId: assignedToUserId || null,
      dueDate: dueDate || null,
    };
    try {
      if (isEdit) await update.mutateAsync({ id: task.id, body: req });
      else await create.mutateAsync(req);
      onClose();
    } catch (err: unknown) {
      const e = err as { error?: string };
      setError(e?.error ?? t("errorSaveDefault"));
    }
  }

  return (
    <Modal title={isEdit ? t("titleEdit") : t("titleCreate")} onClose={onClose} width={480}>
        <form onSubmit={handleSubmit} style={{ display: "flex", flexDirection: "column", gap: 14 }}>
          <input
            required
            type="text"
            placeholder={t("titlePlaceholder")}
            value={title}
            onChange={(e) => setTitle(e.target.value)}
            style={inputStyle}
          />

          <textarea
            placeholder={t("descriptionPlaceholder")}
            value={description}
            onChange={(e) => setDescription(e.target.value)}
            rows={3}
            style={{ ...inputStyle, resize: "vertical" }}
          />

          <div>
            <div style={{ color: "#6B7280", fontSize: 12, marginBottom: 6 }}>{t("clientLabel")}</div>
            <input
              type="text"
              placeholder={t("clientPlaceholder")}
              value={clientTenantId}
              onChange={(e) => setClientTenantId(e.target.value)}
              style={inputStyle}
            />
          </div>

          <div>
            <div style={{ color: "#6B7280", fontSize: 12, marginBottom: 6 }}>{t("assigneeLabel")}</div>
            <select
              value={assignedToUserId}
              onChange={(e) => setAssignedToUserId(e.target.value)}
              style={{ ...inputStyle, appearance: "none" }}
            >
              <option value="">{t("assigneeNoneOption")}</option>
              {staff?.map((u) => (
                <option key={u.id} value={u.id}>{u.fullName}</option>
              ))}
            </select>
          </div>

          <div>
            <div style={{ color: "#6B7280", fontSize: 12, marginBottom: 6 }}>{t("dueDateLabel")}</div>
            <input
              type="date"
              value={dueDate}
              onChange={(e) => setDueDate(e.target.value)}
              style={inputStyle}
            />
          </div>

          {error && (
            <div style={{
              color: "#F87171", fontSize: 13, padding: "8px 12px",
              background: "#1F0A0A", borderRadius: 8, border: "1px solid #7F1D1D",
            }}>
              {error}
            </div>
          )}

          <Btn type="submit" fullWidth disabled={isPending}>
            {isPending ? t("saving") : (isEdit ? t("saveChanges") : t("createTask"))}
          </Btn>
        </form>
    </Modal>
  );
}
