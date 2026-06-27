"use client";

import { useState } from "react";
import { X } from "lucide-react";
import { useCreateTenant } from "../hooks/useAdmin";
import { ALL_PLANS, PLAN_LABELS } from "../types";
import type { CreateTenantRequest } from "../types";
import { Btn } from "@/components/ui/Btn";

interface Props {
  onClose: () => void;
}

function autoSlug(name: string): string {
  return name.toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-|-$/g, "");
}

const INPUT_STYLE: React.CSSProperties = {
  width: "100%",
  background: "#0D1117",
  border: "1px solid #1F2937",
  borderRadius: 8,
  padding: "9px 12px",
  color: "#E8EDF5",
  fontSize: 13,
  outline: "none",
  boxSizing: "border-box",
};

const LABEL_STYLE: React.CSSProperties = {
  display: "block",
  color: "#9CA3AF",
  fontSize: 12,
  fontWeight: 500,
  marginBottom: 6,
};

interface FieldProps {
  label: string;
  children: React.ReactNode;
}

function Field({ label, children }: FieldProps) {
  return (
    <div style={{ display: "flex", flexDirection: "column" }}>
      <label style={LABEL_STYLE}>{label}</label>
      {children}
    </div>
  );
}

export function CreateTenantModal({ onClose }: Props) {
  const createTenant = useCreateTenant();

  const [form, setForm] = useState<CreateTenantRequest>({
    name: "",
    slug: "",
    plan: "trial",
    adminEmail: "",
    adminFullName: "",
    adminPassword: "",
  });
  const [slugManual, setSlugManual] = useState(false);
  const [error, setError] = useState("");

  function setField<K extends keyof CreateTenantRequest>(key: K, value: CreateTenantRequest[K]) {
    setForm((prev) => ({ ...prev, [key]: value }));
  }

  function handleNameChange(name: string) {
    setForm((prev) => ({
      ...prev,
      name,
      slug: slugManual ? prev.slug : autoSlug(name),
    }));
  }

  function handleSlugChange(slug: string) {
    setSlugManual(true);
    setField("slug", slug);
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError("");

    if (!form.name.trim() || !form.slug.trim() || !form.adminEmail.trim() || !form.adminPassword.trim()) {
      setError("Заповніть всі обов\'язкові поля");
      return;
    }

    try {
      await createTenant.mutateAsync(form);
      onClose();
    } catch (err) {
      setError((err as Error)?.message ?? "Помилка створення тенанта");
    }
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
        zIndex: 50,
      }}
      onClick={(e) => { if (e.target === e.currentTarget) onClose(); }}
    >
      <div
        style={{
          background: "#161B26",
          border: "1px solid #1F2937",
          borderRadius: 12,
          width: "100%",
          maxWidth: 500,
          maxHeight: "90vh",
          overflowY: "auto",
          boxShadow: "0 24px 64px rgba(0,0,0,0.6)",
        }}
      >
        {/* Header */}
        <div
          style={{
            display: "flex",
            alignItems: "center",
            justifyContent: "space-between",
            padding: "20px 24px",
            borderBottom: "1px solid #1F2937",
          }}
        >
          <div style={{ color: "#E8EDF5", fontSize: 16, fontWeight: 600 }}>
            Новий тенант
          </div>
          <button
            onClick={onClose}
            style={{ background: "none", border: "none", color: "#6B7280", cursor: "pointer", padding: 4 }}
          >
            <X size={18} />
          </button>
        </div>

        {/* Form */}
        <form onSubmit={handleSubmit}>
          <div style={{ padding: "20px 24px", display: "flex", flexDirection: "column", gap: 16 }}>
            <Field label="Назва *">
              <input
                style={INPUT_STYLE}
                value={form.name}
                onChange={(e) => handleNameChange(e.target.value)}
                placeholder="Мережа Квіточка"
              />
            </Field>

            <Field label="Slug *">
              <input
                style={INPUT_STYLE}
                value={form.slug}
                onChange={(e) => handleSlugChange(e.target.value)}
                placeholder="merezha-kvitochka"
              />
            </Field>

            <Field label="План">
              <select
                style={{ ...INPUT_STYLE, cursor: "pointer" }}
                value={form.plan}
                onChange={(e) => setField("plan", e.target.value)}
              >
                {ALL_PLANS.map((p) => (
                  <option key={p} value={p}>{PLAN_LABELS[p]}</option>
                ))}
              </select>
            </Field>

            <div style={{ borderTop: "1px solid #1F2937", paddingTop: 16 }}>
              <div style={{ color: "#6B7280", fontSize: 12, marginBottom: 14 }}>
                Адміністратор тенанта
              </div>
              <div style={{ display: "flex", flexDirection: "column", gap: 14 }}>
                <Field label="Email *">
                  <input
                    style={INPUT_STYLE}
                    type="email"
                    value={form.adminEmail}
                    onChange={(e) => setField("adminEmail", e.target.value)}
                    placeholder="admin@company.com"
                  />
                </Field>
                <Field label="Повне ім'я">
                  <input
                    style={INPUT_STYLE}
                    value={form.adminFullName}
                    onChange={(e) => setField("adminFullName", e.target.value)}
                    placeholder="Іван Іваненко"
                  />
                </Field>
                <Field label="Пароль *">
                  <input
                    style={INPUT_STYLE}
                    type="password"
                    value={form.adminPassword}
                    onChange={(e) => setField("adminPassword", e.target.value)}
                    placeholder="Мінімум 8 символів"
                  />
                </Field>
              </div>
            </div>

            {error && (
              <div style={{ color: "#F87171", fontSize: 13 }}>{error}</div>
            )}
          </div>

          {/* Footer */}
          <div
            style={{
              display: "flex",
              justifyContent: "flex-end",
              gap: 10,
              padding: "16px 24px",
              borderTop: "1px solid #1F2937",
            }}
          >
            <Btn type="button" variant="ghost" onClick={onClose}>
              Скасувати
            </Btn>
            <Btn type="submit" disabled={createTenant.isPending}>
              {createTenant.isPending ? "Створення…" : "Створити"}
            </Btn>
          </div>
        </form>
      </div>
    </div>
  );
}
