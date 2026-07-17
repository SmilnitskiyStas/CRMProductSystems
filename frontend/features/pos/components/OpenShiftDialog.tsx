"use client";

import { useState } from "react";
import { useTranslations } from "next-intl";
import { Modal } from "@/components/ui/Modal";
import { Btn } from "@/components/ui/Btn";
import { useStores } from "@/features/stores/hooks/useStores";

interface Props {
  isOpen: boolean;
  onClose: () => void;
  onConfirm: (storeId: string, openingCash?: number) => void;
  isLoading: boolean;
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

export function OpenShiftDialog({ isOpen, onClose, onConfirm, isLoading }: Props) {
  const t = useTranslations("Dashboard.pos.openShiftDialog");
  const tCommon = useTranslations("Common");
  const { data: stores, isLoading: storesLoading } = useStores();
  const [storeId, setStoreId] = useState("");
  const [openingCash, setOpeningCash] = useState("");

  if (!isOpen) return null;

  function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!storeId) return;
    const cash = openingCash ? parseFloat(openingCash) : undefined;
    onConfirm(storeId, cash);
  }

  return (
    <Modal title={t("title")} onClose={onClose} width={420}>
      <form onSubmit={handleSubmit} style={{ display: "flex", flexDirection: "column", gap: 18 }}>
        {/* Store selector */}
        <div>
          <label style={LABEL}>{t("storeLabel")}</label>
          {storesLoading ? (
            <div style={{ color: "#4B5563", fontSize: 13 }}>{t("loadingStores")}</div>
          ) : (
            <select
              value={storeId}
              onChange={(e) => setStoreId(e.target.value)}
              required
              style={{ ...INPUT, appearance: "none", cursor: "pointer" }}
            >
              <option value="">{t("selectStore")}</option>
              {stores?.map((s) => (
                <option key={s.id} value={s.id}>
                  {s.name}
                  {s.address ? ` — ${s.address}` : ""}
                </option>
              ))}
            </select>
          )}
        </div>

        {/* Opening cash */}
        <div>
          <label style={LABEL}>{t("openingCashLabel")}</label>
          <input
            type="number"
            min={0}
            step="0.01"
            placeholder="0.00"
            value={openingCash}
            onChange={(e) => setOpeningCash(e.target.value)}
            style={INPUT}
          />
        </div>

        {/* Actions */}
        <div style={{ display: "flex", justifyContent: "flex-end", gap: 10, paddingTop: 4 }}>
          <Btn variant="ghost" onClick={onClose} disabled={isLoading}>
            {tCommon("cancel")}
          </Btn>
          <Btn type="submit" variant="success" disabled={!storeId || isLoading}>
            {isLoading ? t("opening") : t("confirm")}
          </Btn>
        </div>
      </form>
    </Modal>
  );
}
