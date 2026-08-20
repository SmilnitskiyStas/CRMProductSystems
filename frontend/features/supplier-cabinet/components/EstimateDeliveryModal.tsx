"use client";

// Number-input confirm modal for the supplier "Ship" action (TASK-584): asks
// for the estimated delivery time in whole days before transitioning an
// order to "shipped" — required by the backend
// (MarketplaceOrderService.EstimatedDeliveryDaysRequiredError, > 0). Mirrors
// the wiring of ReasonModal.tsx (components/ui/ReasonModal.tsx) but swaps
// the textarea for a positive-integer number input.

import { useState } from "react";
import { useTranslations } from "next-intl";
import { X } from "lucide-react";
import { Btn } from "@/components/ui/Btn";

interface Props {
  title: string;
  pending?: boolean;
  onConfirm: (days: number) => void;
  onClose: () => void;
}

export function EstimateDeliveryModal({ title, pending = false, onConfirm, onClose }: Props) {
  const t = useTranslations("Dashboard.supplierCabinet.ordersTab");
  const tCommon = useTranslations("Common");
  const [value, setValue] = useState("");

  const parsed = Number(value);
  const isValid = value.trim() !== "" && Number.isInteger(parsed) && parsed > 0;

  return (
    <div
      style={{
        position: "fixed",
        inset: 0,
        background: "rgba(0,0,0,0.6)",
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        zIndex: 999,
        padding: 20,
      }}
      onClick={onClose}
    >
      <div
        style={{
          background: "#0F1623",
          border: "1px solid #1F2937",
          borderRadius: 12,
          width: "100%",
          maxWidth: 440,
          padding: 20,
        }}
        onClick={(e) => e.stopPropagation()}
      >
        <div
          style={{
            display: "flex",
            alignItems: "center",
            justifyContent: "space-between",
            marginBottom: 14,
          }}
        >
          <h3 style={{ color: "#E8EDF5", fontSize: 15, fontWeight: 700, margin: 0 }}>
            {title}
          </h3>
          <button
            onClick={onClose}
            style={{ background: "none", border: "none", cursor: "pointer", color: "#4B5563", padding: 4 }}
          >
            <X size={18} />
          </button>
        </div>

        <label style={{ display: "block", color: "#9CA3AF", fontSize: 12, marginBottom: 6 }}>
          {t("shipModalLabel")}
          <span style={{ color: "#F87171" }}> *</span>
        </label>
        <input
          type="number"
          inputMode="numeric"
          min={1}
          step={1}
          value={value}
          onChange={(e) => setValue(e.target.value)}
          placeholder={t("shipModalPlaceholder")}
          style={{
            width: "100%",
            boxSizing: "border-box",
            background: "#1F2937",
            border: "1px solid #374151",
            borderRadius: 8,
            color: "#E8EDF5",
            fontSize: 13,
            padding: "9px 12px",
            outline: "none",
            fontFamily: "inherit",
          }}
        />

        <div style={{ display: "flex", justifyContent: "flex-end", gap: 10, marginTop: 16 }}>
          <Btn variant="ghost" onClick={onClose}>
            {tCommon("cancel")}
          </Btn>
          <Btn variant="primary" disabled={pending || !isValid} onClick={() => onConfirm(parsed)}>
            {pending ? t("shipModalPending") : t("shipModalConfirm")}
          </Btn>
        </div>
      </div>
    </div>
  );
}
