"use client";

import { useMemo, useState } from "react";
import { ChevronLeft, ChevronRight, Plus } from "lucide-react";
import type { SupplierTaskDto, SupplierTaskStatus } from "../types";
import { Btn } from "@/components/ui/Btn";

const STATUS_LABELS: Record<SupplierTaskStatus, string> = {
  pending: "Очікує",
  in_progress: "В роботі",
  completed: "Завершено",
  cancelled: "Скасовано",
};

const WEEKDAY_LABELS = ["Пн", "Вт", "Ср", "Чт", "Пт", "Сб", "Нд"];

/** Local YYYY-MM-DD, no timezone conversion (matches <input type="date"> values). */
function toDateKey(d: Date): string {
  const y = d.getFullYear();
  const m = String(d.getMonth() + 1).padStart(2, "0");
  const day = String(d.getDate()).padStart(2, "0");
  return `${y}-${m}-${day}`;
}

/** dueDate from the backend may be an ISO datetime — take just the date part,
 * in UTC (dueDate is stored/serialized as UTC per the backend contract), so
 * grouping matches what the user picked in the date input. */
function dueDateKey(dueDate: string): string {
  return dueDate.slice(0, 10);
}

interface MonthCell {
  date: Date;
  dateKey: string;
  inMonth: boolean;
}

function buildMonthGrid(year: number, month: number): MonthCell[] {
  const firstOfMonth = new Date(year, month, 1);
  // Monday-first grid: JS getDay() is 0=Sun..6=Sat, convert to 0=Mon..6=Sun
  const firstWeekday = (firstOfMonth.getDay() + 6) % 7;
  const gridStart = new Date(year, month, 1 - firstWeekday);

  const cells: MonthCell[] = [];
  for (let i = 0; i < 42; i++) {
    const d = new Date(gridStart);
    d.setDate(gridStart.getDate() + i);
    cells.push({ date: d, dateKey: toDateKey(d), inMonth: d.getMonth() === month });
  }
  return cells;
}

interface Props {
  tasks: SupplierTaskDto[];
  onAddTask: (dueDate: string) => void;
}

export function TasksCalendar({ tasks, onAddTask }: Props) {
  const today = new Date();
  const [year, setYear] = useState(today.getFullYear());
  const [month, setMonth] = useState(today.getMonth());
  const [selectedDate, setSelectedDate] = useState<string | null>(null);

  const grid = useMemo(() => buildMonthGrid(year, month), [year, month]);

  const tasksByDate = useMemo(() => {
    const map = new Map<string, SupplierTaskDto[]>();
    for (const task of tasks) {
      if (!task.dueDate) continue;
      const key = dueDateKey(task.dueDate);
      if (!map.has(key)) map.set(key, []);
      map.get(key)!.push(task);
    }
    return map;
  }, [tasks]);

  const todayKey = toDateKey(today);
  const monthLabel = new Date(year, month, 1).toLocaleDateString("uk-UA", {
    month: "long",
    year: "numeric",
  });

  function goPrevMonth() {
    if (month === 0) { setYear((y) => y - 1); setMonth(11); }
    else setMonth((m) => m - 1);
  }
  function goNextMonth() {
    if (month === 11) { setYear((y) => y + 1); setMonth(0); }
    else setMonth((m) => m + 1);
  }
  function goToday() {
    setYear(today.getFullYear());
    setMonth(today.getMonth());
    setSelectedDate(todayKey);
  }

  const selectedTasks = selectedDate ? tasksByDate.get(selectedDate) ?? [] : [];

  return (
    <div>
      {/* Header: month nav */}
      <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginBottom: 14 }}>
        <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
          <button onClick={goPrevMonth} style={navBtnStyle}>
            <ChevronLeft size={16} />
          </button>
          <span style={{ color: "#E8EDF5", fontSize: 14, fontWeight: 600, minWidth: 140, textAlign: "center", textTransform: "capitalize" }}>
            {monthLabel}
          </span>
          <button onClick={goNextMonth} style={navBtnStyle}>
            <ChevronRight size={16} />
          </button>
        </div>
        <button onClick={goToday} style={{ ...navBtnStyle, width: "auto", padding: "6px 12px", fontSize: 12 }}>
          Сьогодні
        </button>
      </div>

      {/* Weekday labels */}
      <div style={{ display: "grid", gridTemplateColumns: "repeat(7, 1fr)", gap: 6, marginBottom: 6 }}>
        {WEEKDAY_LABELS.map((label) => (
          <div key={label} style={{ textAlign: "center", color: "#4B5563", fontSize: 11, fontWeight: 600 }}>
            {label}
          </div>
        ))}
      </div>

      {/* Month grid */}
      <div style={{ display: "grid", gridTemplateColumns: "repeat(7, 1fr)", gap: 6, marginBottom: 20 }}>
        {grid.map((cell) => {
          const dayTasks = tasksByDate.get(cell.dateKey) ?? [];
          const isSelected = selectedDate === cell.dateKey;
          const isToday = cell.dateKey === todayKey;
          return (
            <button
              key={cell.dateKey}
              onClick={() => setSelectedDate(cell.dateKey)}
              style={{
                minHeight: 68,
                padding: "6px 8px",
                borderRadius: 8,
                border: isSelected ? "1px solid #3B82F6" : "1px solid #1F2937",
                background: isSelected ? "#1D3461" : "#0D1117",
                cursor: "pointer",
                textAlign: "left",
                display: "flex",
                flexDirection: "column",
                gap: 4,
                opacity: cell.inMonth ? 1 : 0.35,
              }}
            >
              <span
                style={{
                  fontSize: 12,
                  fontWeight: isToday ? 700 : 500,
                  color: isToday ? "#93C5FD" : cell.inMonth ? "#E8EDF5" : "#6B7280",
                }}
              >
                {cell.date.getDate()}
              </span>
              {dayTasks.length > 0 && (
                <span
                  style={{
                    alignSelf: "flex-start",
                    fontSize: 10,
                    fontWeight: 600,
                    padding: "1px 7px",
                    borderRadius: 8,
                    background: "#14532D",
                    color: "#4ADE80",
                  }}
                >
                  {dayTasks.length}
                </span>
              )}
            </button>
          );
        })}
      </div>

      {/* Selected day details */}
      {selectedDate && (
        <div style={{ background: "#0D1117", border: "1px solid #1F2937", borderRadius: 10, padding: "14px 18px" }}>
          <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginBottom: 10 }}>
            <span style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 600 }}>
              {new Date(selectedDate).toLocaleDateString("uk-UA", { day: "numeric", month: "long", year: "numeric" })}
            </span>
            <Btn size="sm" icon={<Plus size={12} />} onClick={() => onAddTask(selectedDate)}>
              Додати завдання
            </Btn>
          </div>

          {selectedTasks.length === 0 ? (
            <div style={{ color: "#4B5563", fontSize: 13, padding: "8px 0" }}>
              Завдань на цей день немає.
            </div>
          ) : (
            <div style={{ display: "flex", flexDirection: "column", gap: 8 }}>
              {selectedTasks.map((task) => (
                <div
                  key={task.id}
                  style={{
                    background: "#111827",
                    border: "1px solid #1F2937",
                    borderRadius: 8,
                    padding: "10px 14px",
                  }}
                >
                  <div style={{ display: "flex", alignItems: "center", gap: 8, marginBottom: 4, flexWrap: "wrap" }}>
                    <span style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 600 }}>{task.title}</span>
                    <span style={{ color: "#6B7280", fontSize: 11 }}>{STATUS_LABELS[task.status]}</span>
                  </div>
                  {task.description && (
                    <div style={{ color: "#6B7280", fontSize: 12, marginBottom: 4 }}>{task.description}</div>
                  )}
                  <div style={{ display: "flex", gap: 12, flexWrap: "wrap", color: "#4B5563", fontSize: 11 }}>
                    {task.clientTenantName && <span>Клієнт: {task.clientTenantName}</span>}
                    {task.assignedToUserName && <span>Відповідальний: {task.assignedToUserName}</span>}
                  </div>
                </div>
              ))}
            </div>
          )}
        </div>
      )}
    </div>
  );
}

const navBtnStyle: React.CSSProperties = {
  width: 30,
  height: 30,
  display: "flex",
  alignItems: "center",
  justifyContent: "center",
  background: "#0D1117",
  border: "1px solid #1F2937",
  borderRadius: 7,
  color: "#9CA3AF",
  cursor: "pointer",
};
