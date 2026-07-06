"use client";

// Modal: client submits a cooperation request to a supplier (TASK-318).

import { useState } from "react";
import { X } from "lucide-react";
import { toast } from "sonner";
import { Btn } from "@/components/ui/Btn";
import { useCreateCooperationRequest } from "../hooks/useCooperation";

interface Props {
  supplierId: string;
  supplierName: string;
  onClose: () => void;
}

export function CooperationRequestModal({ supplierId, supplierName, onClose }: Props) {
  const [message, setMessage] = useState("");
  const createRequest = useCreateCooperationRequest(supplierId);

  function handleSubmit() {
    createRequest.mutate(
      { message: message.trim() || undefined },
      {
        onSuccess: () => {
          toast.success("Заявку на співпрацю подано");
          onClose();
        },
        // 409 — уже є жива угода; бекенд повертає статус у повідомленні
        onError: (err) => toast.error(err.message),
      }
    );
  }

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
          maxWidth: 480,
          padding: 20,
        }}
        onClick={(e) => e.stopPropagation()}
      >
        <div
          style={{
            display: "flex",
            alignItems: "center",
            justifyContent: "space-between",
            marginBottom: 6,
          }}
        >
          <h3 style={{ color: "#E8EDF5", fontSize: 15, fontWeight: 700, margin: 0 }}>
            Заявка на співпрацю
          </h3>
          <button
            onClick={onClose}
            style={{ background: "none", border: "none", cursor: "pointer", color: "#4B5563", padding: 4 }}
          >
            <X size={18} />
          </button>
        </div>
        <p style={{ color: "#6B7280", fontSize: 13, margin: "0 0 14px" }}>
          Постачальник: <span style={{ color: "#9CA3AF" }}>{supplierName}</span>
        </p>

        <label style={{ display: "block", color: "#9CA3AF", fontSize: 12, marginBottom: 6 }}>
          Повідомлення постачальнику (необовʼязково)
        </label>
        <textarea
          value={message}
          onChange={(e) => setMessage(e.target.value)}
          placeholder="Коротко опишіть, що плануєте замовляти..."
          rows={4}
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
            resize: "vertical",
            fontFamily: "inherit",
          }}
        />

        <div style={{ display: "flex", justifyContent: "flex-end", gap: 10, marginTop: 16 }}>
          <Btn variant="ghost" onClick={onClose}>
            Скасувати
          </Btn>
          <Btn disabled={createRequest.isPending} onClick={handleSubmit}>
            {createRequest.isPending ? "Надсилання..." : "Подати заявку"}
          </Btn>
        </div>
      </div>
    </div>
  );
}
