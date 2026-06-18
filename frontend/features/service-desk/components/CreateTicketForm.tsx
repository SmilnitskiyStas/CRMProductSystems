"use client";

import { useState } from "react";
import { X } from "lucide-react";
import { toast } from "sonner";
import { useCreateTicket } from "../hooks/useTickets";
import { useLocations } from "@/features/locations/hooks/useLocations";
import type { TicketCategory, TicketPriority } from "../types";
import { TICKET_CATEGORY_LABELS, TICKET_PRIORITY_LABELS } from "../types";

interface Props {
  onClose: () => void;
}

export function CreateTicketForm({ onClose }: Props) {
  const [title, setTitle] = useState("");
  const [description, setDescription] = useState("");
  const [category, setCategory] = useState<TicketCategory>("general");
  const [priority, setPriority] = useState<TicketPriority>("medium");
  const [locationId, setLocationId] = useState("");

  const { data: locations } = useLocations();
  const createMutation = useCreateTicket();

  function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!title.trim() || !description.trim()) return;

    createMutation.mutate(
      {
        title: title.trim(),
        description: description.trim(),
        category,
        priority,
        locationId: locationId || undefined,
      },
      {
        onSuccess: () => {
          toast.success("Тікет створено");
          onClose();
        },
        onError: (err) => toast.error(err.message),
      }
    );
  }

  const inputStyle = {
    width: "100%",
    background: "#111827",
    border: "1px solid #1F2937",
    borderRadius: 6,
    color: "#E8EDF5",
    fontSize: 13,
    padding: "8px 10px",
    outline: "none",
    boxSizing: "border-box" as const,
    fontFamily: "inherit",
  };

  const selectStyle = {
    ...inputStyle,
    cursor: "pointer",
  };

  const labelStyle = {
    display: "block",
    color: "#9CA3AF",
    fontSize: 12,
    fontWeight: 600,
    marginBottom: 6,
  };

  return (
    <>
      {/* Backdrop */}
      <div
        onClick={onClose}
        style={{
          position: "fixed",
          inset: 0,
          background: "rgba(0,0,0,0.6)",
          zIndex: 50,
        }}
      />

      {/* Dialog */}
      <div
        style={{
          position: "fixed",
          top: "50%",
          left: "50%",
          transform: "translate(-50%, -50%)",
          width: "min(520px, calc(100vw - 32px))",
          background: "#0D1117",
          border: "1px solid #1F2937",
          borderRadius: 12,
          zIndex: 51,
          display: "flex",
          flexDirection: "column",
        }}
      >
        {/* Header */}
        <div
          style={{
            padding: "18px 20px",
            borderBottom: "1px solid #1F2937",
            display: "flex",
            alignItems: "center",
            justifyContent: "space-between",
          }}
        >
          <div>
            <div style={{ color: "#E8EDF5", fontSize: 16, fontWeight: 700 }}>
              Новий тікет
            </div>
            <div style={{ color: "#4B5563", fontSize: 12, marginTop: 2 }}>
              Опишіть вашу проблему або запит
            </div>
          </div>
          <button
            onClick={onClose}
            style={{
              background: "transparent",
              border: "none",
              color: "#4B5563",
              cursor: "pointer",
              padding: 4,
            }}
          >
            <X size={18} />
          </button>
        </div>

        {/* Form */}
        <form onSubmit={handleSubmit} style={{ padding: "20px", display: "flex", flexDirection: "column", gap: 16 }}>
          {/* Title */}
          <div>
            <label style={labelStyle}>
              Заголовок <span style={{ color: "#DC2626" }}>*</span>
            </label>
            <input
              value={title}
              onChange={(e) => setTitle(e.target.value)}
              placeholder="Коротко опишіть проблему"
              required
              style={inputStyle}
            />
          </div>

          {/* Description */}
          <div>
            <label style={labelStyle}>
              Опис <span style={{ color: "#DC2626" }}>*</span>
            </label>
            <textarea
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              placeholder="Детально опишіть проблему або запит..."
              required
              rows={4}
              style={{ ...inputStyle, resize: "vertical" }}
            />
          </div>

          {/* Category + Priority */}
          <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 12 }}>
            <div>
              <label style={labelStyle}>Категорія</label>
              <select
                value={category}
                onChange={(e) => setCategory(e.target.value as TicketCategory)}
                style={selectStyle}
              >
                {(Object.keys(TICKET_CATEGORY_LABELS) as TicketCategory[]).map((c) => (
                  <option key={c} value={c}>{TICKET_CATEGORY_LABELS[c]}</option>
                ))}
              </select>
            </div>

            <div>
              <label style={labelStyle}>Пріоритет</label>
              <select
                value={priority}
                onChange={(e) => setPriority(e.target.value as TicketPriority)}
                style={selectStyle}
              >
                {(Object.keys(TICKET_PRIORITY_LABELS) as TicketPriority[]).map((p) => (
                  <option key={p} value={p}>{TICKET_PRIORITY_LABELS[p]}</option>
                ))}
              </select>
            </div>
          </div>

          {/* Location */}
          <div>
            <label style={labelStyle}>Локація (необов&apos;язково)</label>
            <select
              value={locationId}
              onChange={(e) => setLocationId(e.target.value)}
              style={selectStyle}
            >
              <option value="">— Не вказано —</option>
              {locations?.filter((l) => l.isActive).map((loc) => (
                <option key={loc.id} value={loc.id}>{loc.name}</option>
              ))}
            </select>
          </div>

          {/* Actions */}
          <div style={{ display: "flex", gap: 10, justifyContent: "flex-end", marginTop: 4 }}>
            <button
              type="button"
              onClick={onClose}
              style={{
                background: "transparent",
                border: "1px solid #374151",
                borderRadius: 6,
                color: "#9CA3AF",
                fontSize: 13,
                padding: "8px 16px",
                cursor: "pointer",
              }}
            >
              Скасувати
            </button>
            <button
              type="submit"
              disabled={!title.trim() || !description.trim() || createMutation.isPending}
              style={{
                background: title.trim() && description.trim() ? "#1D3461" : "#111827",
                border: `1px solid ${title.trim() && description.trim() ? "#3B82F6" : "#1F2937"}`,
                borderRadius: 6,
                color: title.trim() && description.trim() ? "#93C5FD" : "#374151",
                fontSize: 13,
                fontWeight: 600,
                padding: "8px 20px",
                cursor: title.trim() && description.trim() ? "pointer" : "not-allowed",
              }}
            >
              {createMutation.isPending ? "Створення..." : "Створити тікет"}
            </button>
          </div>
        </form>
      </div>
    </>
  );
}
