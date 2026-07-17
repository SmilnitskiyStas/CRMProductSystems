"use client";

import { useState } from "react";
import { X } from "lucide-react";
import { useTranslations } from "next-intl";
import { useCreateCustomer, useUpdateCustomer } from "../hooks/useAutoService";
import type { CustomerDto, CreateCustomerRequest, UpdateCustomerRequest } from "../types";

interface Props {
  customer?: CustomerDto;
  onClose: () => void;
}

export function CustomerForm({ customer, onClose }: Props) {
  const t = useTranslations("Dashboard.autoService.customerForm");
  const createCustomer = useCreateCustomer();
  const updateCustomer = useUpdateCustomer(customer?.id ?? "");

  const [name, setName] = useState(customer?.name ?? "");
  const [phone, setPhone] = useState(customer?.phone ?? "");
  const [email, setEmail] = useState(customer?.email ?? "");
  const [notes, setNotes] = useState(customer?.notes ?? "");
  const [error, setError] = useState<string | null>(null);

  const isEdit = !!customer;

  function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    if (!name.trim()) { setError(t("errorNameRequired")); return; }

    if (isEdit) {
      const body: UpdateCustomerRequest = {
        name: name.trim(),
        phone: phone.trim() || undefined,
        email: email.trim() || undefined,
        notes: notes.trim() || undefined,
      };
      updateCustomer.mutate(body, {
        onSuccess: () => onClose(),
        onError: (err) => setError(err instanceof Error ? err.message : t("errorGeneric")),
      });
    } else {
      const body: CreateCustomerRequest = {
        name: name.trim(),
        phone: phone.trim() || undefined,
        email: email.trim() || undefined,
        notes: notes.trim() || undefined,
      };
      createCustomer.mutate(body, {
        onSuccess: () => onClose(),
        onError: (err) => setError(err instanceof Error ? err.message : t("errorGeneric")),
      });
    }
  }

  const isPending = createCustomer.isPending || updateCustomer.isPending;

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
        <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginBottom: 20 }}>
          <h2 style={{ color: "#E8EDF5", fontSize: 16, fontWeight: 700, margin: 0 }}>
            {isEdit ? t("titleEdit") : t("titleNew")}
          </h2>
          <button onClick={onClose} style={{ background: "transparent", border: "none", color: "#6B7280", cursor: "pointer", padding: 4 }}>
            <X size={18} />
          </button>
        </div>

        <form onSubmit={handleSubmit} style={{ display: "flex", flexDirection: "column", gap: 14 }}>
          <div>
            <label style={labelStyle}>{t("nameLabel")}</label>
            <input type="text" value={name} onChange={(e) => setName(e.target.value)} placeholder={t("namePlaceholder")} style={inputStyle} />
          </div>
          <div>
            <label style={labelStyle}>{t("phoneLabel")}</label>
            <input type="tel" value={phone} onChange={(e) => setPhone(e.target.value)} placeholder="+380XXXXXXXXX" style={inputStyle} />
          </div>
          <div>
            <label style={labelStyle}>{t("emailLabel")}</label>
            <input type="email" value={email} onChange={(e) => setEmail(e.target.value)} placeholder="email@example.com" style={inputStyle} />
          </div>
          <div>
            <label style={labelStyle}>{t("notesLabel")}</label>
            <textarea value={notes} onChange={(e) => setNotes(e.target.value)} rows={3} style={{ ...inputStyle, resize: "vertical", minHeight: 64 }} />
          </div>

          {error && (
            <div style={{ color: "#F87171", fontSize: 12, padding: "8px 12px", background: "#1F1010", borderRadius: 6 }}>
              {error}
            </div>
          )}

          <div style={{ display: "flex", gap: 8, justifyContent: "flex-end", marginTop: 4 }}>
            <button type="button" onClick={onClose} style={btnSecondaryStyle}>{t("cancel")}</button>
            <button type="submit" disabled={isPending} style={btnPrimaryStyle}>
              {isPending ? t("saving") : isEdit ? t("save") : t("add")}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}

const labelStyle: React.CSSProperties = { color: "#9CA3AF", fontSize: 12, marginBottom: 6, display: "block" };
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
const btnPrimaryStyle: React.CSSProperties = { padding: "9px 20px", borderRadius: 8, border: "none", background: "#1D4ED8", color: "#E8EDF5", fontSize: 13, fontWeight: 600, cursor: "pointer" };
const btnSecondaryStyle: React.CSSProperties = { padding: "9px 16px", borderRadius: 8, border: "1px solid #1F2937", background: "transparent", color: "#6B7280", fontSize: 13, cursor: "pointer" };
