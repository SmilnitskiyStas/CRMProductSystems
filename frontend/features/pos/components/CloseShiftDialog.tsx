"use client";

import { useEffect, useState } from "react";
import { useTranslations } from "next-intl";
import { Modal } from "@/components/ui/Modal";
import { Btn } from "@/components/ui/Btn";

interface Props {
  isOpen: boolean;
  onClose: () => void;
  onConfirm: (actualClosingCash?: number) => void;
  isLoading: boolean;
  /** Server-side error message (e.g. "ActualClosingCash cannot be negative.") to surface. */
  error?: string | null;
}

const LABEL: React.CSSProperties = {
  display: "block",
  color: "#6B7280",
  fontSize: 11,
  fontWeight: 600,
  textTransform: "uppercase",
  letterSpacing: "0.05em",
  marginBottom: 6,
};

const INPUT: React.CSSProperties = {
  width: "100%",
  background: "#0D1117",
  border: "1px solid #374151",
  borderRadius: 8,
  color: "#E8EDF5",
  fontSize: 13,
  padding: "9px 12px",
  outline: "none",
  boxSizing: "border-box",
};

export function CloseShiftDialog({ isOpen, onClose, onConfirm, isLoading, error }: Props) {
  const t = useTranslations("Dashboard.pos.closeShiftDialog");
  const tCommon = useTranslations("Common");
  const [actualClosingCash, setActualClosingCash] = useState("");
  const [clientError, setClientError] = useState<string | null>(null);

  // The component stays mounted while closed (isOpen just gates the render below),
  // so state must be reset explicitly each time it opens — otherwise a stale value
  // from a previous close carries into the next one.
  useEffect(() => {
    if (isOpen) {
      setActualClosingCash("");
      setClientError(null);
    }
  }, [isOpen]);

  if (!isOpen) return null;

  function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setClientError(null);

    if (actualClosingCash.trim() === "") {
      // Left blank -> close without reconciliation (backward compatible).
      onConfirm(undefined);
      return;
    }

    const cash = parseFloat(actualClosingCash);
    if (Number.isNaN(cash) || cash < 0) {
      setClientError(t("negativeCashError"));
      return;
    }
    onConfirm(cash);
  }

  return (
    <Modal title={t("title")} onClose={onClose} width={420}>
      <form onSubmit={handleSubmit} style={{ display: "flex", flexDirection: "column", gap: 18 }}>
        <div style={{ color: "#9CA3AF", fontSize: 12, lineHeight: 1.5 }}>
          {t("description")}
        </div>

        {/* Actual closing cash */}
        <div>
          <label style={LABEL}>{t("actualCashLabel")}</label>
          <input
            type="number"
            min={0}
            step="0.01"
            placeholder="0.00"
            value={actualClosingCash}
            onChange={(e) => setActualClosingCash(e.target.value)}
            style={INPUT}
            autoFocus
          />
        </div>

        {(clientError || error) && (
          <div style={{ color: "#F87171", fontSize: 13 }}>{clientError ?? error}</div>
        )}

        {/* Actions */}
        <div style={{ display: "flex", justifyContent: "flex-end", gap: 10, paddingTop: 4 }}>
          <Btn variant="ghost" onClick={onClose} disabled={isLoading}>
            {tCommon("cancel")}
          </Btn>
          <Btn type="submit" variant="danger" disabled={isLoading}>
            {isLoading ? t("closing") : t("confirm")}
          </Btn>
        </div>
      </form>
    </Modal>
  );
}
