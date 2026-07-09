"use client";

import { useEffect } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { Btn } from "@/components/ui/Btn";
import type { LegalEntityDto } from "../types";

// ── Schema ─────────────────────────────────────────────────────────────────────

const schema = z.object({
  legalName: z.string().min(1, "Юридична назва обов'язкова").max(255),
  edrpou: z
    .string()
    .max(10)
    .optional()
    .refine((v) => !v || v.length === 8 || v.length === 10, {
      message: "ЄДРПОУ повинен містити 8 або 10 цифр",
    })
    .refine((v) => !v || /^\d+$/.test(v), {
      message: "ЄДРПОУ повинен містити лише цифри",
    }),
  legalAddress: z.string().max(500).optional(),
  directorName: z.string().max(255).optional(),
  phone: z.string().max(30).optional(),
  email: z.string().max(255).optional().refine((v) => !v || z.string().email().safeParse(v).success, {
    message: "Некоректний email",
  }),
  iban: z.string().max(34).optional(),
  bankName: z.string().max(255).optional(),
  isVatPayer: z.boolean(),
  isActive: z.boolean(),
});

type FormValues = z.infer<typeof schema>;

// ── Props ──────────────────────────────────────────────────────────────────────

interface Props {
  /** null = create mode */
  entity: LegalEntityDto | null;
  isPending: boolean;
  onClose: () => void;
  onSubmit: (values: {
    legalName: string;
    edrpou: string | null;
    legalAddress: string | null;
    directorName: string | null;
    phone: string | null;
    email: string | null;
    iban: string | null;
    bankName: string | null;
    isVatPayer: boolean;
    isActive: boolean;
  }) => void;
}

const EMPTY_VALUES: FormValues = {
  legalName: "",
  edrpou: "",
  legalAddress: "",
  directorName: "",
  phone: "",
  email: "",
  iban: "",
  bankName: "",
  isVatPayer: false,
  isActive: true,
};

// ── Component ──────────────────────────────────────────────────────────────────

export function LegalEntityFormDialog({ entity, isPending, onClose, onSubmit }: Props) {
  const isEdit = entity !== null;

  const {
    register,
    handleSubmit,
    reset,
    formState: { errors },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: EMPTY_VALUES,
  });

  // Populate form when editing
  useEffect(() => {
    if (entity) {
      reset({
        legalName: entity.legalName,
        edrpou: entity.edrpou ?? "",
        legalAddress: entity.legalAddress ?? "",
        directorName: entity.directorName ?? "",
        phone: entity.phone ?? "",
        email: entity.email ?? "",
        iban: entity.iban ?? "",
        bankName: entity.bankName ?? "",
        isVatPayer: entity.isVatPayer,
        isActive: entity.isActive,
      });
    } else {
      reset(EMPTY_VALUES);
    }
  }, [entity, reset]);

  function onValid(values: FormValues) {
    onSubmit({
      legalName: values.legalName,
      edrpou: values.edrpou?.trim() || null,
      legalAddress: values.legalAddress?.trim() || null,
      directorName: values.directorName?.trim() || null,
      phone: values.phone?.trim() || null,
      email: values.email?.trim() || null,
      iban: values.iban?.trim() || null,
      bankName: values.bankName?.trim() || null,
      isVatPayer: values.isVatPayer,
      isActive: values.isActive,
    });
  }

  return (
    <div
      style={{
        position: "fixed",
        inset: 0,
        background: "rgba(0,0,0,0.55)",
        zIndex: 50,
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
      }}
      onClick={onClose}
    >
      <div
        style={{
          background: "#161B26",
          border: "1px solid #1F2937",
          borderRadius: 12,
          padding: "24px 28px",
          width: 520,
          maxWidth: "90vw",
          maxHeight: "88vh",
          overflowY: "auto",
          display: "flex",
          flexDirection: "column",
          gap: 18,
        }}
        onClick={(e) => e.stopPropagation()}
      >
        {/* Title */}
        <h2 style={{ color: "#E8EDF5", fontSize: 18, fontWeight: 700, margin: 0 }}>
          {isEdit ? "Редагувати юридичну особу" : "Нова юридична особа"}
        </h2>

        <form
          onSubmit={handleSubmit(onValid)}
          style={{ display: "flex", flexDirection: "column", gap: 14 }}
        >
          {/* Legal name */}
          <Field label="Юридична назва" error={errors.legalName?.message}>
            <input
              {...register("legalName")}
              placeholder='ТОВ "Приклад"'
              style={inputStyle}
            />
          </Field>

          {/* EDRPOU */}
          <Field label="ЄДРПОУ (необов'язково)" error={errors.edrpou?.message}>
            <input
              {...register("edrpou")}
              placeholder="12345678"
              style={inputStyle}
            />
          </Field>

          {/* Legal address */}
          <Field label="Юридична адреса (необов'язково)" error={errors.legalAddress?.message}>
            <input
              {...register("legalAddress")}
              placeholder="вул. Шевченка, 1, Київ"
              style={inputStyle}
            />
          </Field>

          {/* Director name */}
          <Field label="ПІБ директора (необов'язково)" error={errors.directorName?.message}>
            <input
              {...register("directorName")}
              placeholder="Іваненко Іван Іванович"
              style={inputStyle}
            />
          </Field>

          {/* Phone + Email */}
          <div style={{ display: "flex", gap: 12 }}>
            <div style={{ flex: 1 }}>
              <Field label="Телефон (необов'язково)" error={errors.phone?.message}>
                <input
                  {...register("phone")}
                  placeholder="+380 99 123 45 67"
                  style={inputStyle}
                />
              </Field>
            </div>
            <div style={{ flex: 1 }}>
              <Field label="Email (необов'язково)" error={errors.email?.message}>
                <input
                  {...register("email")}
                  placeholder="office@example.com"
                  style={inputStyle}
                />
              </Field>
            </div>
          </div>

          {/* IBAN + Bank name */}
          <div style={{ display: "flex", gap: 12 }}>
            <div style={{ flex: 1 }}>
              <Field label="IBAN (необов'язково)" error={errors.iban?.message}>
                <input
                  {...register("iban")}
                  placeholder="UA00 0000 0000 0000 0000 0000 000"
                  style={inputStyle}
                />
              </Field>
            </div>
            <div style={{ flex: 1 }}>
              <Field label="Назва банку (необов'язково)" error={errors.bankName?.message}>
                <input
                  {...register("bankName")}
                  placeholder="АТ «Банк»"
                  style={inputStyle}
                />
              </Field>
            </div>
          </div>

          {/* VAT payer */}
          <label
            style={{ display: "flex", alignItems: "center", gap: 8, cursor: "pointer", color: "#9CA3AF", fontSize: 13 }}
          >
            <input type="checkbox" {...register("isVatPayer")} />
            Платник ПДВ
          </label>

          {/* Active */}
          {isEdit && (
            <label
              style={{ display: "flex", alignItems: "center", gap: 8, cursor: "pointer", color: "#9CA3AF", fontSize: 13 }}
            >
              <input type="checkbox" {...register("isActive")} />
              Активна
            </label>
          )}

          {/* Buttons */}
          <div style={{ display: "flex", gap: 10, justifyContent: "flex-end", marginTop: 4 }}>
            <Btn variant="ghost" type="button" onClick={onClose}>
              Скасувати
            </Btn>
            <Btn type="submit" disabled={isPending}>
              {isPending ? "Збереження…" : isEdit ? "Зберегти" : "Створити"}
            </Btn>
          </div>
        </form>
      </div>
    </div>
  );
}

// ── Shared styles ──────────────────────────────────────────────────────────────

function Field({
  label,
  error,
  children,
}: {
  label: string;
  error?: string;
  children: React.ReactNode;
}) {
  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 4 }}>
      <label style={{ color: "#9CA3AF", fontSize: 12 }}>{label}</label>
      {children}
      {error && <span style={{ color: "#ef4444", fontSize: 11 }}>{error}</span>}
    </div>
  );
}

const inputStyle: React.CSSProperties = {
  width: "100%",
  background: "#111827",
  border: "1px solid #1F2937",
  borderRadius: 8,
  color: "#E8EDF5",
  fontSize: 13,
  padding: "8px 12px",
  outline: "none",
  boxSizing: "border-box",
};
