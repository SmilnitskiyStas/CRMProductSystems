"use client";

import { useState } from "react";
import { X } from "lucide-react";
import { useTranslations } from "next-intl";
import { useCreateWorkOrder, useVehicles } from "../hooks/useAutoService";
import { useUsers } from "@/features/users/hooks/useUsers";
import type { CreateWorkOrderRequest } from "../types";

interface Props {
  onClose: () => void;
}

export function CreateWorkOrderModal({ onClose }: Props) {
  const t = useTranslations("Dashboard.autoService.createModal");
  const { data: vehicles } = useVehicles();
  const { data: users } = useUsers();
  const createWorkOrder = useCreateWorkOrder();

  const [vehicleId, setVehicleId] = useState("");
  const [mechanicUserId, setMechanicUserId] = useState("");
  const [notes, setNotes] = useState("");
  const [error, setError] = useState<string | null>(null);

  const mechanics = (users ?? []).filter((u) =>
    ["store_manager", "storekeeper", "merchandiser"].includes(u.role)
  );

  function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    if (!vehicleId) {
      setError(t("errorSelectVehicle"));
      return;
    }
    const body: CreateWorkOrderRequest = {
      vehicleId,
      mechanicUserId: mechanicUserId || undefined,
      notes: notes.trim() || undefined,
    };
    createWorkOrder.mutate(body, {
      onSuccess: () => onClose(),
      onError: (err) => setError(err instanceof Error ? err.message : t("errorGeneric")),
    });
  }

  return (
    <div
      style={{
        position: "fixed",
        inset: 0,
        background: "rgba(0,0,0,0.7)",
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        zIndex: 1000,
      }}
      onClick={(e) => e.target === e.currentTarget && onClose()}
    >
      <div
        style={{
          background: "#111827",
          border: "1px solid #1F2937",
          borderRadius: 12,
          padding: "24px",
          width: "100%",
          maxWidth: 440,
        }}
      >
        {/* Header */}
        <div
          style={{
            display: "flex",
            alignItems: "center",
            justifyContent: "space-between",
            marginBottom: 20,
          }}
        >
          <h2 style={{ color: "#E8EDF5", fontSize: 16, fontWeight: 700, margin: 0 }}>
            {t("title")}
          </h2>
          <button
            onClick={onClose}
            style={{
              background: "transparent",
              border: "none",
              color: "#6B7280",
              cursor: "pointer",
              padding: 4,
            }}
          >
            <X size={18} />
          </button>
        </div>

        <form onSubmit={handleSubmit} style={{ display: "flex", flexDirection: "column", gap: 14 }}>
          {/* Vehicle */}
          <div>
            <label style={{ color: "#9CA3AF", fontSize: 12, marginBottom: 6, display: "block" }}>
              {t("vehicleLabel")}
            </label>
            <select
              value={vehicleId}
              onChange={(e) => setVehicleId(e.target.value)}
              style={selectStyle}
            >
              <option value="">{t("selectVehiclePlaceholder")}</option>
              {(vehicles ?? []).map((v) => (
                <option key={v.id} value={v.id}>
                  {v.brand} {v.model} · {v.licensePlate}
                </option>
              ))}
            </select>
          </div>

          {/* Mechanic */}
          <div>
            <label style={{ color: "#9CA3AF", fontSize: 12, marginBottom: 6, display: "block" }}>
              {t("mechanicLabel")}
            </label>
            <select
              value={mechanicUserId}
              onChange={(e) => setMechanicUserId(e.target.value)}
              style={selectStyle}
            >
              <option value="">{t("notAssigned")}</option>
              {mechanics.map((u) => (
                <option key={u.id} value={u.id}>
                  {u.fullName}
                </option>
              ))}
            </select>
          </div>

          {/* Notes */}
          <div>
            <label style={{ color: "#9CA3AF", fontSize: 12, marginBottom: 6, display: "block" }}>
              {t("notesLabel")}
            </label>
            <textarea
              value={notes}
              onChange={(e) => setNotes(e.target.value)}
              rows={3}
              placeholder={t("notesPlaceholder")}
              style={{
                ...inputStyle,
                resize: "vertical",
                minHeight: 72,
              }}
            />
          </div>

          {error && (
            <div style={{ color: "#F87171", fontSize: 12, padding: "8px 12px", background: "#1F1010", borderRadius: 6 }}>
              {error}
            </div>
          )}

          {/* Actions */}
          <div style={{ display: "flex", gap: 8, justifyContent: "flex-end", marginTop: 4 }}>
            <button type="button" onClick={onClose} style={btnSecondaryStyle}>
              {t("cancel")}
            </button>
            <button
              type="submit"
              disabled={createWorkOrder.isPending}
              style={btnPrimaryStyle}
            >
              {createWorkOrder.isPending ? t("creating") : t("create")}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}

// ─── Shared micro-styles ──────────────────────────────────────────────────────

const inputStyle: React.CSSProperties = {
  width: "100%",
  padding: "9px 12px",
  background: "#0D1117",
  border: "1px solid #1F2937",
  borderRadius: 8,
  color: "#E8EDF5",
  fontSize: 13,
  outline: "none",
  boxSizing: "border-box",
};

const selectStyle: React.CSSProperties = {
  ...inputStyle,
  appearance: "auto",
};

const btnPrimaryStyle: React.CSSProperties = {
  padding: "9px 20px",
  borderRadius: 8,
  border: "none",
  background: "#1D4ED8",
  color: "#E8EDF5",
  fontSize: 13,
  fontWeight: 600,
  cursor: "pointer",
};

const btnSecondaryStyle: React.CSSProperties = {
  padding: "9px 16px",
  borderRadius: 8,
  border: "1px solid #1F2937",
  background: "transparent",
  color: "#6B7280",
  fontSize: 13,
  cursor: "pointer",
};
