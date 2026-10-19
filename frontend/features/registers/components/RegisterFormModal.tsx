"use client";

import { useState } from "react";
import { useTranslations } from "next-intl";
import { Modal } from "@/components/ui/Modal";
import { Btn } from "@/components/ui/Btn";
import type { LocationDto } from "@/features/locations/types";
import type { Register, RegisterType } from "../types";
import { inputStyle, labelStyle } from "./shared";

export interface RegisterFormValues {
  name: string;
  storeId: string;
  number?: number;
  type: RegisterType;
}

export function RegisterFormModal({
  register,
  stores,
  defaultStoreId,
  isPending,
  error,
  onClose,
  onSubmit,
}: {
  register?: Register;
  stores: LocationDto[];
  defaultStoreId?: string;
  isPending: boolean;
  error?: string | null;
  onClose: () => void;
  onSubmit: (values: RegisterFormValues) => void;
}) {
  const t = useTranslations("Dashboard.registers.form");
  const tType = useTranslations("Dashboard.registers.type");
  const isEdit = !!register;

  const [name, setName] = useState(register?.name ?? "");
  const [storeId, setStoreId] = useState(register?.storeId ?? defaultStoreId ?? stores[0]?.id ?? "");
  const [number, setNumber] = useState("");
  const [nameError, setNameError] = useState<string | null>(null);

  function submit() {
    const trimmed = name.trim();
    if (!trimmed || trimmed.length > 100) {
      setNameError(t("errorName"));
      return;
    }
    if (!storeId) return;
    const parsed = number.trim() ? Number.parseInt(number, 10) : undefined;
    onSubmit({
      name: trimmed,
      storeId,
      number: parsed && parsed > 0 ? parsed : undefined,
      type: "line",
    });
  }

  return (
    <Modal title={isEdit ? t("editTitle") : t("createTitle")} onClose={onClose} width={460}>
      <div style={{ marginBottom: 14 }}>
        <label style={labelStyle}>{t("name")}</label>
        <input
          style={inputStyle}
          value={name}
          maxLength={100}
          autoFocus
          onChange={(e) => { setName(e.target.value); setNameError(null); }}
          placeholder={t("namePlaceholder")}
        />
        {nameError && <div style={{ color: "#F87171", fontSize: 12, marginTop: 4 }}>{nameError}</div>}
      </div>

      <div style={{ marginBottom: 14 }}>
        <label style={labelStyle}>{t("store")}</label>
        <select style={inputStyle} value={storeId} onChange={(e) => setStoreId(e.target.value)}>
          {stores.map((s) => (
            <option key={s.id} value={s.id}>{s.name}</option>
          ))}
        </select>
      </div>

      {!isEdit && (
        <div style={{ marginBottom: 14 }}>
          <label style={labelStyle}>{t("number")}</label>
          <input
            style={inputStyle}
            type="number"
            min={1}
            value={number}
            onChange={(e) => setNumber(e.target.value)}
            placeholder={t("numberPlaceholder")}
          />
        </div>
      )}

      <div style={{ marginBottom: 14 }}>
        <label style={labelStyle}>{t("type")}</label>
        <select style={inputStyle} value={register?.type ?? "line"} disabled>
          <option value="line">{tType("line")}</option>
          <option value="self_checkout">{tType("self_checkout")} — {t("comingSoon")}</option>
        </select>
      </div>

      {!isEdit && <p style={{ color: "#6B7280", fontSize: 12, margin: "0 0 4px" }}>{t("codeHint")}</p>}
      {error && <div style={{ color: "#F87171", fontSize: 12, marginTop: 8 }}>{error}</div>}

      <div style={{ display: "flex", justifyContent: "flex-end", gap: 10, marginTop: 18 }}>
        <Btn variant="ghost" onClick={onClose}>{t("cancel")}</Btn>
        <Btn disabled={isPending || !storeId} onClick={submit}>{t("save")}</Btn>
      </div>
    </Modal>
  );
}
