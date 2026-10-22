"use client";

import { useState } from "react";
import { useTranslations } from "next-intl";
import { useCreateWorkOrder, useVehicles } from "../hooks/useAutoService";
import { useUsers } from "@/features/users/hooks/useUsers";
import { Modal } from "@/components/ui/Modal";
import { Btn } from "@/components/ui/Btn";
import { labelStyle, selectStyle, textareaStyle } from "@/components/ui/fieldStyles";
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
    <Modal title={t("title")} onClose={onClose} width={440}>
        <form onSubmit={handleSubmit} style={{ display: "flex", flexDirection: "column", gap: 14 }}>
          {/* Vehicle */}
          <div>
            <label style={labelStyle}>
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
            <label style={labelStyle}>
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
            <label style={labelStyle}>
              {t("notesLabel")}
            </label>
            <textarea
              value={notes}
              onChange={(e) => setNotes(e.target.value)}
              rows={3}
              placeholder={t("notesPlaceholder")}
              style={{
                ...textareaStyle,
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
            <Btn variant="ghost" onClick={onClose}>
              {t("cancel")}
            </Btn>
            <Btn type="submit" disabled={createWorkOrder.isPending}>
              {createWorkOrder.isPending ? t("creating") : t("create")}
            </Btn>
          </div>
        </form>
    </Modal>
  );
}
