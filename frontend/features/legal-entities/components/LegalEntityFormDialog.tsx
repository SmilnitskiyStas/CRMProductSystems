"use client";

import { useEffect, useMemo } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { useTranslations } from "next-intl";
import { Btn } from "@/components/ui/Btn";
import { Modal } from "@/components/ui/Modal";
import { inputStyle } from "@/components/ui/fieldStyles";
import type { LegalEntityDto } from "../types";

// ── Schema ─────────────────────────────────────────────────────────────────────
// Zod .min(1, message) / .refine(..., message) need translated messages, so the schema
// is built once per render inside the component (see `useMemo(() => buildSchema(t), [t])`
// below) — mirrors `buildSchema(t)` in features/locations/components/LocationFormDialog.tsx
// (i18n Block 2).

function buildSchema(t: ReturnType<typeof useTranslations>) {
  return z.object({
    legalName: z.string().min(1, t("validationLegalNameRequired")).max(255),
    edrpou: z
      .string()
      .max(10)
      .optional()
      .refine((v) => !v || v.length === 8 || v.length === 10, {
        message: t("validationEdrpouLength"),
      })
      .refine((v) => !v || /^\d+$/.test(v), {
        message: t("validationEdrpouDigitsOnly"),
      }),
    legalAddress: z.string().max(500).optional(),
    directorName: z.string().max(255).optional(),
    phone: z.string().max(30).optional(),
    email: z.string().max(255).optional().refine((v) => !v || z.string().email().safeParse(v).success, {
      message: t("validationEmailInvalid"),
    }),
    iban: z.string().max(34).optional(),
    bankName: z.string().max(255).optional(),
    isVatPayer: z.boolean(),
    isActive: z.boolean(),
  });
}

type FormValues = z.infer<ReturnType<typeof buildSchema>>;

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
  const t = useTranslations("Dashboard.legalEntities.formDialog");
  const isEdit = entity !== null;

  const schema = useMemo(() => buildSchema(t), [t]);

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
    <Modal title={isEdit ? t("editTitle") : t("createTitle")} onClose={onClose} width={520}>
        <form
          onSubmit={handleSubmit(onValid)}
          style={{ display: "flex", flexDirection: "column", gap: 14 }}
        >
          {/* Legal name */}
          <Field label={t("legalNameLabel")} error={errors.legalName?.message}>
            <input
              {...register("legalName")}
              placeholder={t("legalNamePlaceholder")}
              style={inputStyle}
            />
          </Field>

          {/* EDRPOU */}
          <Field label={t("edrpouLabel")} error={errors.edrpou?.message}>
            <input
              {...register("edrpou")}
              placeholder={t("edrpouPlaceholder")}
              style={inputStyle}
            />
          </Field>

          {/* Legal address */}
          <Field label={t("legalAddressLabel")} error={errors.legalAddress?.message}>
            <input
              {...register("legalAddress")}
              placeholder={t("legalAddressPlaceholder")}
              style={inputStyle}
            />
          </Field>

          {/* Director name */}
          <Field label={t("directorNameLabel")} error={errors.directorName?.message}>
            <input
              {...register("directorName")}
              placeholder={t("directorNamePlaceholder")}
              style={inputStyle}
            />
          </Field>

          {/* Phone + Email */}
          <div style={{ display: "flex", gap: 12 }}>
            <div style={{ flex: 1 }}>
              <Field label={t("phoneLabel")} error={errors.phone?.message}>
                <input
                  {...register("phone")}
                  placeholder={t("phonePlaceholder")}
                  style={inputStyle}
                />
              </Field>
            </div>
            <div style={{ flex: 1 }}>
              <Field label={t("emailLabel")} error={errors.email?.message}>
                <input
                  {...register("email")}
                  placeholder={t("emailPlaceholder")}
                  style={inputStyle}
                />
              </Field>
            </div>
          </div>

          {/* IBAN + Bank name */}
          <div style={{ display: "flex", gap: 12 }}>
            <div style={{ flex: 1 }}>
              <Field label={t("ibanLabel")} error={errors.iban?.message}>
                <input
                  {...register("iban")}
                  placeholder={t("ibanPlaceholder")}
                  style={inputStyle}
                />
              </Field>
            </div>
            <div style={{ flex: 1 }}>
              <Field label={t("bankNameLabel")} error={errors.bankName?.message}>
                <input
                  {...register("bankName")}
                  placeholder={t("bankNamePlaceholder")}
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
            {t("vatPayerLabel")}
          </label>

          {/* Active */}
          {isEdit && (
            <label
              style={{ display: "flex", alignItems: "center", gap: 8, cursor: "pointer", color: "#9CA3AF", fontSize: 13 }}
            >
              <input type="checkbox" {...register("isActive")} />
              {t("activeLabel")}
            </label>
          )}

          {/* Buttons */}
          <div style={{ display: "flex", gap: 10, justifyContent: "flex-end", marginTop: 4 }}>
            <Btn variant="ghost" type="button" onClick={onClose}>
              {t("cancelButton")}
            </Btn>
            <Btn type="submit" disabled={isPending}>
              {isPending ? t("savingButton") : isEdit ? t("saveButton") : t("createButton")}
            </Btn>
          </div>
        </form>
    </Modal>
  );
}

// ── Field wrapper ──────────────────────────────────────────────────────────────

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
