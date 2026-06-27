"use client";

import { useState } from "react";
import type { Customer, CreateCustomerPayload, UpdateCustomerPayload } from "../types";
import { Btn } from "@/components/ui/Btn";

const inputStyle: React.CSSProperties = {
  width: "100%",
  background: "#0D1117",
  border: "1px solid #374151",
  borderRadius: 8,
  padding: "9px 12px",
  color: "#E8EDF5",
  fontSize: 13,
  outline: "none",
  boxSizing: "border-box",
};

const labelStyle: React.CSSProperties = {
  display: "block",
  color: "#9CA3AF",
  fontSize: 12,
  fontWeight: 500,
  marginBottom: 6,
};

function validate(name: string, email: string, phone: string): Record<string, string> {
  const errors: Record<string, string> = {};
  if (!name.trim()) {
    errors.name = "Ім'я обов'язкове";
  }
  if (email.trim() && !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email.trim())) {
    errors.email = "Невірний формат email";
  }
  if (phone.trim() && !/^[+\d\s\-()]{7,20}$/.test(phone.trim())) {
    errors.phone = "Невірний формат телефону";
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
    const errs = validate(name, email, phone);
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
    <>
      {/* Backdrop */}
      <div
        onClick={onClose}
        style={{
          position: "fixed",
          inset: 0,
          background: "rgba(0,0,0,0.6)",
          zIndex: 300,
          backdropFilter: "blur(2px)",
        }}
      />

      {/* Modal */}
      <div
        style={{
          position: "fixed",
          top: "50%",
          left: "50%",
          transform: "translate(-50%, -50%)",
          width: "min(520px, 95vw)",
          background: "#0D1117",
          border: "1px solid #1F2937",
          borderRadius: 14,
          zIndex: 301,
          display: "flex",
          flexDirection: "column",
          maxHeight: "90vh",
          overflow: "hidden",
        }}
      >
        {/* Header */}
        <div
          style={{
            display: "flex",
            alignItems: "center",
            justifyContent: "space-between",
            padding: "18px 22px",
            borderBottom: "1px solid #1F2937",
            flexShrink: 0,
          }}
        >
          <div>
            <h2 style={{ color: "#E8EDF5", fontSize: 15, fontWeight: 700, margin: 0 }}>
              {isEdit ? "Редагувати клієнта" : "Новий клієнт"}
            </h2>
            <p style={{ color: "#4B5563", fontSize: 12, margin: "3px 0 0" }}>
              {isEdit ? "Оновіть інформацію про клієнта" : "Додайте нового клієнта до системи"}
            </p>
          </div>
          <button
            onClick={onClose}
            style={{
              background: "transparent",
              border: "1px solid #1F2937",
              borderRadius: 8,
              padding: "5px 9px",
              color: "#4B5563",
              fontSize: 16,
              cursor: "pointer",
            }}
          >
            ✕
          </button>
        </div>

        {/* Form */}
        <form
          onSubmit={handleSubmit}
          style={{ padding: 22, overflowY: "auto", display: "flex", flexDirection: "column", gap: 14 }}
        >
          {/* Name */}
          <div>
            <label style={labelStyle}>Ім'я *</label>
            <input
              value={name}
              onChange={(e) => setName(e.target.value)}
              placeholder="Іван Петренко"
              style={{ ...inputStyle, borderColor: errors.name ? "#EF4444" : "#374151" }}
            />
            {errors.name && (
              <p style={{ color: "#EF4444", fontSize: 11, marginTop: 4 }}>{errors.name}</p>
            )}
          </div>

          {/* Phone */}
          <div>
            <label style={labelStyle}>Телефон</label>
            <input
              value={phone}
              onChange={(e) => setPhone(e.target.value)}
              placeholder="+380 50 000 00 00"
              style={{ ...inputStyle, borderColor: errors.phone ? "#EF4444" : "#374151" }}
            />
            {errors.phone && (
              <p style={{ color: "#EF4444", fontSize: 11, marginTop: 4 }}>{errors.phone}</p>
            )}
          </div>

          {/* Email */}
          <div>
            <label style={labelStyle}>Email</label>
            <input
              type="email"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              placeholder="ivan@example.com"
              style={{ ...inputStyle, borderColor: errors.email ? "#EF4444" : "#374151" }}
            />
            {errors.email && (
              <p style={{ color: "#EF4444", fontSize: 11, marginTop: 4 }}>{errors.email}</p>
            )}
          </div>

          {/* Notes */}
          <div>
            <label style={labelStyle}>Нотатки</label>
            <textarea
              value={notes}
              onChange={(e) => setNotes(e.target.value)}
              placeholder="Довільні нотатки про клієнта…"
              rows={3}
              style={{
                ...inputStyle,
                resize: "vertical",
                fontFamily: "inherit",
              }}
            />
          </div>

          {/* Tags */}
          <div>
            <label style={labelStyle}>Теги</label>
            <div style={{ display: "flex", gap: 8, marginBottom: 8 }}>
              <input
                value={tagInput}
                onChange={(e) => setTagInput(e.target.value)}
                onKeyDown={handleTagKeyDown}
                placeholder="Введіть тег і натисніть Enter"
                style={{ ...inputStyle, flex: 1 }}
              />
              <Btn type="button" size="sm" onClick={addTag}>
                + Додати
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
              {isPending ? "Збереження…" : isEdit ? "Зберегти" : "Створити"}
            </Btn>
            <Btn type="button" variant="ghost" onClick={onClose}>
              Скасувати
            </Btn>
          </div>
        </form>
      </div>
    </>
  );
}
