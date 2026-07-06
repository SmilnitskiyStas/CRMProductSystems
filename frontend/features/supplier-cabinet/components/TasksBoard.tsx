"use client";

import { useState } from "react";
import { Plus, X, ClipboardList, List, CalendarDays } from "lucide-react";
import {
  useSupplierTasks,
  useCreateSupplierTask,
  useUpdateSupplierTask,
  useUpdateSupplierTaskStatus,
  useCabinetStaff,
} from "../hooks/useSupplierCabinet";
import type { SupplierTaskDto, SupplierTaskStatus } from "../types";
import { Btn } from "@/components/ui/Btn";
import { TasksCalendar } from "./TasksCalendar";

const STATUS_LABELS: Record<SupplierTaskStatus, string> = {
  pending: "Очікує",
  in_progress: "В роботі",
  completed: "Завершено",
  cancelled: "Скасовано",
};

const STATUS_COLORS: Record<SupplierTaskStatus, { bg: string; color: string; border: string }> = {
  pending:     { bg: "#1F2937",   color: "#9CA3AF", border: "#374151" },
  in_progress: { bg: "#1E3A5F22", color: "#60A5FA", border: "#1D4ED855" },
  completed:   { bg: "#1a3a2e",   color: "#4ADE80", border: "#065F46" },
  cancelled:   { bg: "#2d0a0a",   color: "#F87171", border: "#7F1D1D" },
};

const ALL_STATUSES: SupplierTaskStatus[] = ["pending", "in_progress", "completed", "cancelled"];

function StatusBadge({ status }: { status: SupplierTaskStatus }) {
  const c = STATUS_COLORS[status];
  return (
    <span
      style={{
        fontSize: 11, padding: "3px 9px", borderRadius: 5,
        background: c.bg, color: c.color, border: `1px solid ${c.border}`,
        whiteSpace: "nowrap",
      }}
    >
      {STATUS_LABELS[status]}
    </span>
  );
}

type ViewMode = "list" | "calendar";

export function TasksBoard() {
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
            Дошка завдань
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
          <div style={{ display: "flex", background: "#0D1117", border: "1px solid #1F2937", borderRadius: 8, padding: 3 }}>
            <button
              onClick={() => setView("list")}
              style={{
                display: "flex", alignItems: "center", gap: 6,
                padding: "6px 14px", borderRadius: 6, border: "none", fontSize: 12, fontWeight: 600, cursor: "pointer",
                background: view === "list" ? "#1D3461" : "transparent",
                color: view === "list" ? "#93C5FD" : "#6B7280",
              }}
            >
              <List size={13} /> Список
            </button>
            <button
              onClick={() => setView("calendar")}
              style={{
                display: "flex", alignItems: "center", gap: 6,
                padding: "6px 14px", borderRadius: 6, border: "none", fontSize: 12, fontWeight: 600, cursor: "pointer",
                background: view === "calendar" ? "#1D3461" : "transparent",
                color: view === "calendar" ? "#93C5FD" : "#6B7280",
              }}
            >
              <CalendarDays size={13} /> Календар
            </button>
          </div>
          <Btn icon={<Plus size={14} />} onClick={() => setCreating(true)}>
            Нове завдання
          </Btn>
        </div>
      </div>

      {view === "list" && (
        <>
          {/* Filters */}
          <div style={{ display: "flex", gap: 10, marginBottom: 16, flexWrap: "wrap", alignItems: "center" }}>
            <div style={{ display: "flex", background: "#0D1117", border: "1px solid #1F2937", borderRadius: 8, padding: 3 }}>
              <button
                onClick={() => setScope("mine")}
                style={{
                  padding: "6px 14px", borderRadius: 6, border: "none", fontSize: 12, fontWeight: 600, cursor: "pointer",
                  background: scope === "mine" ? "#1D3461" : "transparent",
                  color: scope === "mine" ? "#93C5FD" : "#6B7280",
                }}
              >
                Мої
              </button>
              <button
                onClick={() => setScope("all")}
                style={{
                  padding: "6px 14px", borderRadius: 6, border: "none", fontSize: 12, fontWeight: 600, cursor: "pointer",
                  background: scope === "all" ? "#1D3461" : "transparent",
                  color: scope === "all" ? "#93C5FD" : "#6B7280",
                }}
              >
                Усі
              </button>
            </div>

            <input
              value={clientFilter}
              onChange={(e) => setClientFilter(e.target.value)}
              placeholder="Client tenant ID (фільтр)"
              style={{ ...filterInputStyle, minWidth: 220 }}
            />

            <select
              value={statusFilter}
              onChange={(e) => setStatusFilter(e.target.value as SupplierTaskStatus | "")}
              style={{ ...filterInputStyle, appearance: "none" }}
            >
              <option value="">Усі статуси</option>
              {ALL_STATUSES.map((s) => (
                <option key={s} value={s}>{STATUS_LABELS[s]}</option>
              ))}
            </select>
          </div>

          {isLoading && <div style={{ color: "#4B5563", fontSize: 13 }}>Завантаження…</div>}
          {isError && <div style={{ color: "#F87171", fontSize: 13 }}>Не вдалося завантажити завдання.</div>}

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
                      {task.clientTenantName && <span>Клієнт: {task.clientTenantName}</span>}
                      {task.assignedToUserName && <span>Відповідальний: {task.assignedToUserName}</span>}
                      {task.dueDate && <span>Дедлайн: {new Date(task.dueDate).toLocaleDateString("uk-UA")}</span>}
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
                      <option key={s} value={s}>{STATUS_LABELS[s]}</option>
                    ))}
                  </select>
                </div>
              ))}

              {(!tasks || tasks.length === 0) && (
                <div style={{ color: "#4B5563", fontSize: 13, textAlign: "center", padding: "24px 0" }}>
                  Завдань немає.
                </div>
              )}
            </div>
          )}
        </>
      )}

      {view === "calendar" && (
        <>
          {isLoading && <div style={{ color: "#4B5563", fontSize: 13 }}>Завантаження…</div>}
          {isError && <div style={{ color: "#F87171", fontSize: 13 }}>Не вдалося завантажити завдання.</div>}
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
      setError(e?.error ?? "Помилка збереження");
    }
  }

  return (
    <div
      style={{
        position: "fixed", inset: 0, zIndex: 999,
        background: "rgba(0,0,0,0.6)",
        display: "flex", alignItems: "center", justifyContent: "center",
        padding: "20px",
      }}
      onClick={onClose}
    >
      <div
        style={{
          background: "#111827", border: "1px solid #1F2937",
          borderRadius: 14, padding: "28px 32px",
          width: "100%", maxWidth: 480,
          maxHeight: "90vh", overflowY: "auto",
        }}
        onClick={(e) => e.stopPropagation()}
      >
        <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginBottom: 20 }}>
          <h2 style={{ color: "#E8EDF5", fontSize: 16, fontWeight: 700, margin: 0 }}>
            {isEdit ? "Редагувати завдання" : "Нове завдання"}
          </h2>
          <button onClick={onClose} style={{ background: "none", border: "none", color: "#6B7280", cursor: "pointer" }}>
            <X size={18} />
          </button>
        </div>

        <form onSubmit={handleSubmit} style={{ display: "flex", flexDirection: "column", gap: 14 }}>
          <input
            required
            type="text"
            placeholder="Назва завдання"
            value={title}
            onChange={(e) => setTitle(e.target.value)}
            style={inputStyle}
          />

          <textarea
            placeholder="Опис (опційно)"
            value={description}
            onChange={(e) => setDescription(e.target.value)}
            rows={3}
            style={{ ...inputStyle, resize: "vertical" }}
          />

          <div>
            <div style={{ color: "#6B7280", fontSize: 12, marginBottom: 6 }}>Клієнт (tenant ID, опційно)</div>
            <input
              type="text"
              placeholder="Client tenant ID"
              value={clientTenantId}
              onChange={(e) => setClientTenantId(e.target.value)}
              style={inputStyle}
            />
          </div>

          <div>
            <div style={{ color: "#6B7280", fontSize: 12, marginBottom: 6 }}>Відповідальний</div>
            <select
              value={assignedToUserId}
              onChange={(e) => setAssignedToUserId(e.target.value)}
              style={{ ...inputStyle, appearance: "none" }}
            >
              <option value="">Не призначено</option>
              {staff?.map((u) => (
                <option key={u.id} value={u.id}>{u.fullName}</option>
              ))}
            </select>
          </div>

          <div>
            <div style={{ color: "#6B7280", fontSize: 12, marginBottom: 6 }}>Дедлайн</div>
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

          <button
            type="submit"
            disabled={isPending}
            style={{
              padding: "11px 0", borderRadius: 9,
              background: isPending ? "#1D3461" : "linear-gradient(135deg, #3B82F6, #6366F1)",
              border: "none", color: "#fff", fontSize: 14, fontWeight: 600,
              cursor: isPending ? "not-allowed" : "pointer", marginTop: 4,
            }}
          >
            {isPending ? "Збереження…" : (isEdit ? "Зберегти зміни" : "Створити завдання")}
          </button>
        </form>
      </div>
    </div>
  );
}

const inputStyle: React.CSSProperties = {
  background: "#0D1117",
  border: "1px solid #1F2937",
  borderRadius: 8,
  padding: "10px 14px",
  color: "#E8EDF5",
  fontSize: 13,
  outline: "none",
  width: "100%",
  boxSizing: "border-box",
  fontFamily: "inherit",
};

const filterInputStyle: React.CSSProperties = {
  background: "#0D1117",
  border: "1px solid #1F2937",
  borderRadius: 8,
  padding: "7px 12px",
  color: "#E8EDF5",
  fontSize: 12,
  outline: "none",
  boxSizing: "border-box",
};
