"use client";

import { useState } from "react";
import { Calendar, Plus, Trash2, X } from "lucide-react";
import { useProviderSchedule, useCreateScheduleSlot, useDeleteScheduleSlot } from "../hooks/useProviderSchedule";
import { useProviderTeam } from "../hooks/useProviderTeam";
import type { ProviderScheduleSlotDto } from "../api/providerScheduleApi";

const DAY_LABELS = ["Пн", "Вт", "Ср", "Чт", "Пт", "Сб", "Нд"];
const DAY_FULL   = ["Понеділок", "Вівторок", "Середа", "Четвер", "П'ятниця", "Субота", "Неділя"];

const ROLE_COLORS: Record<string, string> = {
  provider:       "#A78BFA",
  provider_admin: "#60A5FA",
  provider_agent: "#4ADE80",
};

interface AddSlotModalProps {
  onClose: () => void;
}

function AddSlotModal({ onClose }: AddSlotModalProps) {
  const { data: members } = useProviderTeam();
  const create = useCreateScheduleSlot();
  const [form, setForm] = useState({
    userId: "",
    dayOfWeek: 0,
    startTime: "09:00",
    endTime: "18:00",
    notes: "",
  });
  const [error, setError] = useState<string | null>(null);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!form.userId) { setError("Оберіть учасника"); return; }
    setError(null);
    try {
      await create.mutateAsync({ ...form, dayOfWeek: Number(form.dayOfWeek) });
      onClose();
    } catch (err: unknown) {
      const e = err as { error?: string };
      setError(e?.error ?? "Помилка створення");
    }
  }

  return (
    <div
      style={{ position: "fixed", inset: 0, zIndex: 999, background: "rgba(0,0,0,0.6)", display: "flex", alignItems: "center", justifyContent: "center" }}
      onClick={onClose}
    >
      <div
        style={{ background: "#111827", border: "1px solid #1F2937", borderRadius: 14, padding: "28px 32px", width: "100%", maxWidth: 440 }}
        onClick={(e) => e.stopPropagation()}
      >
        <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginBottom: 20 }}>
          <h2 style={{ color: "#E8EDF5", fontSize: 17, fontWeight: 700, margin: 0 }}>Додати зміну</h2>
          <button onClick={onClose} style={{ background: "none", border: "none", color: "#6B7280", cursor: "pointer" }}>
            <X size={18} />
          </button>
        </div>

        <form onSubmit={handleSubmit} style={{ display: "flex", flexDirection: "column", gap: 14 }}>
          <select
            value={form.userId}
            onChange={(e) => setForm((f) => ({ ...f, userId: e.target.value }))}
            style={{ ...inputStyle, appearance: "none" }}
          >
            <option value="">Оберіть учасника…</option>
            {(members ?? []).filter((m) => m.isActive).map((m) => (
              <option key={m.id} value={m.id}>{m.fullName}</option>
            ))}
          </select>

          <select
            value={form.dayOfWeek}
            onChange={(e) => setForm((f) => ({ ...f, dayOfWeek: Number(e.target.value) }))}
            style={{ ...inputStyle, appearance: "none" }}
          >
            {DAY_FULL.map((d, i) => (
              <option key={i} value={i}>{d}</option>
            ))}
          </select>

          <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 10 }}>
            <div>
              <div style={{ color: "#6B7280", fontSize: 12, marginBottom: 4 }}>Початок</div>
              <input
                type="time"
                value={form.startTime}
                onChange={(e) => setForm((f) => ({ ...f, startTime: e.target.value }))}
                style={inputStyle}
              />
            </div>
            <div>
              <div style={{ color: "#6B7280", fontSize: 12, marginBottom: 4 }}>Кінець</div>
              <input
                type="time"
                value={form.endTime}
                onChange={(e) => setForm((f) => ({ ...f, endTime: e.target.value }))}
                style={inputStyle}
              />
            </div>
          </div>

          <input
            type="text"
            placeholder="Нотатки (необов'язково)"
            value={form.notes}
            onChange={(e) => setForm((f) => ({ ...f, notes: e.target.value }))}
            style={inputStyle}
          />

          {error && (
            <div style={{ color: "#F87171", fontSize: 13, padding: "8px 12px", background: "#1F0A0A", borderRadius: 8, border: "1px solid #7F1D1D" }}>
              {error}
            </div>
          )}

          <button
            type="submit"
            disabled={create.isPending}
            style={{
              padding: "11px 0", borderRadius: 9,
              background: create.isPending ? "#1D3461" : "linear-gradient(135deg, #3B82F6, #6366F1)",
              border: "none", color: "#fff", fontSize: 14, fontWeight: 600,
              cursor: create.isPending ? "not-allowed" : "pointer", marginTop: 4,
            }}
          >
            {create.isPending ? "Збереження…" : "Додати зміну"}
          </button>
        </form>
      </div>
    </div>
  );
}

const inputStyle: React.CSSProperties = {
  background: "#0D1117", border: "1px solid #1F2937", borderRadius: 8,
  padding: "10px 14px", color: "#E8EDF5", fontSize: 13, outline: "none",
  width: "100%", boxSizing: "border-box",
};

function SlotPill({ slot, onDelete }: { slot: ProviderScheduleSlotDto; onDelete: () => void }) {
  return (
    <div style={{
      display: "flex", alignItems: "center", justifyContent: "space-between",
      background: `${ROLE_COLORS[slot.userRole] ?? "#6B7280"}15`,
      border: `1px solid ${ROLE_COLORS[slot.userRole] ?? "#6B7280"}40`,
      borderRadius: 7, padding: "5px 8px", gap: 6,
    }}>
      <div style={{ minWidth: 0 }}>
        <div style={{ color: "#E8EDF5", fontSize: 12, fontWeight: 600, overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>
          {slot.userFullName}
        </div>
        <div style={{ color: "#6B7280", fontSize: 11 }}>
          {slot.startTime} – {slot.endTime}
        </div>
      </div>
      <button
        onClick={onDelete}
        style={{ background: "transparent", border: "none", color: "#4B5563", cursor: "pointer", padding: 2, flexShrink: 0, transition: "color 0.15s" }}
        onMouseEnter={(e) => { (e.currentTarget as HTMLElement).style.color = "#F87171"; }}
        onMouseLeave={(e) => { (e.currentTarget as HTMLElement).style.color = "#4B5563"; }}
        title="Видалити"
      >
        <Trash2 size={12} />
      </button>
    </div>
  );
}

export function ScheduleTab() {
  const { data: slots, isLoading } = useProviderSchedule();
  const deleteSlot = useDeleteScheduleSlot();
  const [showAdd, setShowAdd] = useState(false);

  const slotsByDay = Array.from({ length: 7 }, (_, i) =>
    (slots ?? []).filter((s) => s.dayOfWeek === i)
  );

  return (
    <div style={{ background: "#0D1117", border: "1px solid #1F2937", borderRadius: 12, padding: "20px 24px" }}>
      <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginBottom: 20 }}>
        <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
          <Calendar size={18} color="#60A5FA" />
          <h2 style={{ color: "#E8EDF5", fontSize: 16, fontWeight: 600, margin: 0 }}>
            Розклад команди
          </h2>
          {slots && (
            <span style={{ padding: "2px 8px", borderRadius: 10, background: "#1F2937", color: "#9CA3AF", fontSize: 11 }}>
              {slots.length} змін
            </span>
          )}
        </div>
        <button
          onClick={() => setShowAdd(true)}
          style={{
            display: "flex", alignItems: "center", gap: 6, padding: "8px 14px", borderRadius: 8,
            background: "#1D3461", border: "1px solid #3B82F6", color: "#93C5FD", fontSize: 13, cursor: "pointer",
          }}
        >
          <Plus size={14} />
          Додати зміну
        </button>
      </div>

      {isLoading ? (
        <div style={{ color: "#4B5563", fontSize: 14 }}>Завантаження…</div>
      ) : (
        <div style={{ display: "grid", gridTemplateColumns: "repeat(7, 1fr)", gap: 10 }}>
          {DAY_LABELS.map((day, i) => (
            <div key={i}>
              <div style={{
                color: i >= 5 ? "#F87171" : "#9CA3AF",
                fontSize: 12, fontWeight: 600,
                textAlign: "center", marginBottom: 8,
                padding: "6px 0",
                borderBottom: "1px solid #1F2937",
              }}>
                {day}
              </div>
              <div style={{ display: "flex", flexDirection: "column", gap: 6 }}>
                {slotsByDay[i].length === 0 ? (
                  <div style={{ color: "#374151", fontSize: 11, textAlign: "center", padding: "8px 0" }}>—</div>
                ) : (
                  slotsByDay[i].map((slot) => (
                    <SlotPill
                      key={slot.id}
                      slot={slot}
                      onDelete={() => {
                        if (confirm(`Видалити зміну ${slot.userFullName} (${DAY_FULL[i]})?`)) {
                          deleteSlot.mutate(slot.id);
                        }
                      }}
                    />
                  ))
                )}
              </div>
            </div>
          ))}
        </div>
      )}

      {showAdd && <AddSlotModal onClose={() => setShowAdd(false)} />}
    </div>
  );
}
