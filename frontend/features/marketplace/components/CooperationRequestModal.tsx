"use client";

// Modal: client submits a cooperation request to a supplier (TASK-318).

import { useState } from "react";
import { toast } from "sonner";
import { useTranslations } from "next-intl";
import { Btn } from "@/components/ui/Btn";
import { Modal } from "@/components/ui/Modal";
import { selectStyle, textareaStyle, labelStyle } from "@/components/ui/fieldStyles";
import { useCreateCooperationRequest } from "../hooks/useCooperation";
import { useLegalEntities } from "@/features/legal-entities/hooks/useLegalEntities";
import { CooperationCoveragePanel } from "./CooperationCoveragePanel";

interface Props {
  supplierId: string;
  supplierName: string;
  onClose: () => void;
}

export function CooperationRequestModal({ supplierId, supplierName, onClose }: Props) {
  const t = useTranslations("Dashboard.marketplace.cooperationRequestModal");
  const [message, setMessage] = useState("");
  const [clientLegalEntityId, setClientLegalEntityId] = useState<string>("");
  const createRequest = useCreateCooperationRequest(supplierId);
  const { data: legalEntities } = useLegalEntities();
  const activeLegalEntities = (legalEntities ?? []).filter((e) => e.isActive);

  function handleSubmit() {
    createRequest.mutate(
      {
        message: message.trim() || undefined,
        clientLegalEntityId: clientLegalEntityId || undefined,
      },
      {
        onSuccess: () => {
          toast.success(t("toastSubmitted"));
          onClose();
        },
        // 409 — уже є жива угода; бекенд повертає статус у повідомленні
        onError: (err) => toast.error(err.message),
      }
    );
  }

  return (
    <Modal title={t("title")} onClose={onClose} width={480}>
        <p style={{ color: "#6B7280", fontSize: 13, margin: "0 0 14px" }}>
          {t("supplierLabel")} <span style={{ color: "#9CA3AF" }}>{supplierName}</span>
        </p>

        {/* Advisory delivery-coverage panel (TASK-657) — never blocks submit. */}
        <CooperationCoveragePanel supplierId={supplierId} />

        <label style={{ ...labelStyle, margin: "14px 0 6px" }}>
          {t("messageLabel")}
        </label>
        <textarea
          value={message}
          onChange={(e) => setMessage(e.target.value)}
          placeholder={t("messagePlaceholder")}
          rows={4}
          style={textareaStyle}
        />

        {activeLegalEntities.length > 0 ? (
          <div style={{ marginTop: 14 }}>
            <label style={labelStyle}>
              {t("legalEntityLabel")}
            </label>
            <select
              value={clientLegalEntityId}
              onChange={(e) => setClientLegalEntityId(e.target.value)}
              style={selectStyle}
            >
              <option value="">{t("legalEntityNoneOption")}</option>
              {activeLegalEntities.map((entity) => (
                <option key={entity.id} value={entity.id}>
                  {entity.legalName}
                  {entity.edrpou ? ` (${entity.edrpou})` : ""}
                </option>
              ))}
            </select>
          </div>
        ) : (
          <p style={{ color: "#4B5563", fontSize: 11.5, margin: "10px 0 0" }}>
            {t("noLegalEntitiesHint")}
          </p>
        )}

        <div style={{ display: "flex", justifyContent: "flex-end", gap: 10, marginTop: 16 }}>
          <Btn variant="ghost" onClick={onClose}>
            {t("cancel")}
          </Btn>
          <Btn disabled={createRequest.isPending} onClick={handleSubmit}>
            {createRequest.isPending ? t("submitting") : t("submit")}
          </Btn>
        </div>
    </Modal>
  );
}
