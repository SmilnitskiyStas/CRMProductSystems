"use client";

import { useState } from "react";
import { useTranslations } from "next-intl";
import type { Customer, CreateCustomerPayload, UpdateCustomerPayload } from "../types";
import { Btn } from "@/components/ui/Btn";
import { Modal } from "@/components/ui/Modal";
import { inputStyle, labelStyle, textareaStyle } from "@/components/ui/fieldStyles";

function validate(
  name: string,
  email: string,
  phone: string,
  t: ReturnType<typeof useTranslations>
): Record<string, string> {
  const errors: Record<string, string> = {};
  if (!name.trim()) {
    errors.name = t("errorNameRequired");
  }
  if (email.trim() && !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email.trim())) {
    errors.email = t("errorEmailInvalid");
  }
  if (phone.trim() && !/^[+\d\s\-()]{7,20}$/.test(phone.trim())) {
    errors.phone = t("errorPhoneInvalid");
  }
  return errors;
}

interface Props {
  customer?: Customer;
  isPending: boolean;
  error?: string | null;
  onClose: () => void;
  onSubmit: (data: CreateCustomerPayload | UpdateCustomerPayload) => void;
}

export function CustomerForm({ customer, isPending, error, onClose, onSubmit }: Props) {
  const t = useTranslations("Dashboard.customers.form");
  const isEdit = !!customer;

  const [name,   setName]   = useState(customer?.name   ?? "");
  const [phone,  setPhone]  = useState(customer?.phone  ?? "");
  const [email,  setEmail]  = useState(customer?.email  ?? "");
  const [notes,  setNotes]  = useState(customer?.notes  ?? "");
  const [tags,   setTags]   = useState<string[]>(customer?.tags ?? []);
  const [tagInput, setTagInput] = useState("");
  const [errors, setErrors] = useState<Record<string, string>>({});

  function addTag() {
    const t = tagInput.trim();
    if (t && !tags.includes(t)) {
      setTags((prev) => [...prev, t]);
    }
    setTagInput("");
  }

  function removeTag(tag: string) {
    setTags((prev) => prev.filter((t) => t !== tag));
  }

  function handleTagKeyDown(e: React.KeyboardEvent<HTMLInputElement>) {
    if (e.key === "Enter") {
      e.preventDefault();
      addTag();
    }
  }

  function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    const errs = validate(name, email, phone, t);
    setErrors(errs);
    if (Object.keys(errs).length > 0) return;

    onSubmit({
      name:  name.trim(),
      phone: phone.trim() || undefined,
      email: email.trim() || undefined,
      notes: notes.trim() || undefined,
      tags,
    });
  }

  return (
    <Modal title={isEdit ? t("titleEdit") : t("titleNew")} onClose={onClose} width={520}>
      <p style={{ color: "#4B5563", fontSize: 12, margin: "-14px 0 16px" }}>
        {isEdit ? t("subtitleEdit") : t("subtitleNew")}
      </p>

        {/* Form */}
        <form
          onSubmit={handleSubmit}
          style={{ display: "flex", flexDirection: "column", gap: 14 }}
        >
          {/* Name */}
          <div>
            <label style={labelStyle}>{t("nameLabel")}</label>
            <input
              value={name}
              onChange={(e) => setName(e.target.value)}
              placeholder={t("namePlaceholder")}
              style={{ ...inputStyle, borderColor: errors.name ? "#EF4444" : "#374151" }}
            />
            {errors.name && (
              <p style={{ color: "#EF4444", fontSize: 11, marginTop: 4 }}>{errors.name}</p>
            )}
          </div>

          {/* Phone */}
          <div>
            <label style={labelStyle}>{t("phoneLabel")}</label>
            <input
              value={phone}
              onChange={(e) => setPhone(e.target.value)}
              placeholder={t("phonePlaceholder")}
              style={{ ...inputStyle, borderColor: errors.phone ? "#EF4444" : "#374151" }}
            />
            {errors.phone && (
              <p style={{ color: "#EF4444", fontSize: 11, marginTop: 4 }}>{errors.phone}</p>
            )}
          </div>

          {/* Email */}
          <div>
            <label style={labelStyle}>{t("emailLabel")}</label>
            <input
              type="email"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              placeholder={t("emailPlaceholder")}
              style={{ ...inputStyle, borderColor: errors.email ? "#EF4444" : "#374151" }}
            />
            {errors.email && (
              <p style={{ color: "#EF4444", fontSize: 11, marginTop: 4 }}>{errors.email}</p>
            )}
          </div>

          {/* Notes */}
          <div>
            <label style={labelStyle}>{t("notesLabel")}</label>
            <textarea
              value={notes}
              onChange={(e) => setNotes(e.target.value)}
              placeholder={t("notesPlaceholder")}
              rows={3}
              style={textareaStyle}
            />
          </div>

          {/* Tags */}
          <div>
            <label style={labelStyle}>{t("tagsLabel")}</label>
            <div style={{ display: "flex", gap: 8, marginBottom: 8 }}>
              <input
                value={tagInput}
                onChange={(e) => setTagInput(e.target.value)}
                onKeyDown={handleTagKeyDown}
                placeholder={t("tagInputPlaceholder")}
                style={{ ...inputStyle, flex: 1 }}
              />
              <Btn type="button" size="sm" onClick={addTag}>
                {t("addTag")}
              </Btn>
            </div>
            {tags.length > 0 && (
              <div style={{ display: "flex", flexWrap: "wrap", gap: 6 }}>
                {tags.map((t) => (
                  <span
                    key={t}
                    style={{
                      display: "inline-flex",
                      alignItems: "center",
                      gap: 5,
                      background: "#0a1628",
                      border: "1px solid #1D4ED8",
                      borderRadius: 20,
                      padding: "3px 10px",
                      color: "#60A5FA",
                      fontSize: 12,
                    }}
                  >
                    {t}
                    <button
                      type="button"
                      onClick={() => removeTag(t)}
                      style={{
                        background: "transparent",
                        border: "none",
                        color: "#3B82F6",
                        fontSize: 13,
                        cursor: "pointer",
                        padding: 0,
                        lineHeight: 1,
                      }}
                    >
                      ×
                    </button>
                  </span>
                ))}
              </div>
            )}
          </div>

          {/* Server error */}
          {error && (
            <p style={{ color: "#F87171", fontSize: 12 }}>{error}</p>
          )}

          {/* Actions */}
          <div style={{ display: "flex", gap: 10, paddingTop: 4 }}>
            <Btn type="submit" disabled={isPending} style={{ flex: 1, justifyContent: "center" }}>
              {isPending ? t("saving") : isEdit ? t("save") : t("create")}
            </Btn>
            <Btn type="button" variant="ghost" onClick={onClose}>
              {t("cancel")}
            </Btn>
          </div>
        </form>
    </Modal>
  );
}
