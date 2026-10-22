"use client";

import { useState } from "react";
import { useTranslations } from "next-intl";
import { useCreateCustomer, useUpdateCustomer } from "../hooks/useAutoService";
import { Modal } from "@/components/ui/Modal";
import { Btn } from "@/components/ui/Btn";
import { inputStyle, labelStyle, textareaStyle } from "@/components/ui/fieldStyles";
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
    <Modal title={isEdit ? t("titleEdit") : t("titleNew")} onClose={onClose} width={440}>
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
            <textarea value={notes} onChange={(e) => setNotes(e.target.value)} rows={3} style={{ ...textareaStyle, minHeight: 64 }} />
          </div>

          {error && (
            <div style={{ color: "#F87171", fontSize: 12, padding: "8px 12px", background: "#1F1010", borderRadius: 6 }}>
              {error}
            </div>
          )}

          <div style={{ display: "flex", gap: 8, justifyContent: "flex-end", marginTop: 4 }}>
            <Btn variant="ghost" onClick={onClose}>{t("cancel")}</Btn>
            <Btn type="submit" disabled={isPending}>
              {isPending ? t("saving") : isEdit ? t("save") : t("add")}
            </Btn>
          </div>
        </form>
    </Modal>
  );
}
