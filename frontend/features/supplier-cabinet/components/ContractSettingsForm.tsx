"use client";

// Реквізити договору постачальника (TASK-318): форма upsert + завантаження
// зображень підпису і мокрої печатки (multipart, потрібні збережені реквізити).

import { useEffect, useRef, useState } from "react";
import { ImageOff, Upload } from "lucide-react";
import { toast } from "sonner";
import { Btn } from "@/components/ui/Btn";
import { API_BASE } from "@/lib/api";
import {
  useContractSettings,
  useUpsertContractSettings,
  useUploadContractImage,
} from "../hooks/useCabinetCooperation";
import type { UpsertContractSettingsRequest } from "../types";

const inputStyle: React.CSSProperties = {
  width: "100%",
  boxSizing: "border-box",
  background: "#1F2937",
  border: "1px solid #374151",
  borderRadius: 8,
  color: "#E8EDF5",
  fontSize: 13,
  padding: "9px 12px",
  outline: "none",
  fontFamily: "inherit",
};

const labelStyle: React.CSSProperties = {
  display: "block",
  color: "#9CA3AF",
  fontSize: 12,
  marginBottom: 6,
};

function Req() {
  return <span style={{ color: "#F87171" }}> *</span>;
}

function resolveImageUrl(url: string): string {
  return url.startsWith("http") ? url : `${API_BASE}${url}`;
}

interface FormState {
  legalName: string;
  edrpou: string;
  iban: string;
  bankName: string;
  legalAddress: string;
  directorName: string;
  phone: string;
  email: string;
  serviceName: string;
  serviceDescription: string;
  isVatPayer: boolean;
}

const EMPTY_FORM: FormState = {
  legalName: "",
  edrpou: "",
  iban: "",
  bankName: "",
  legalAddress: "",
  directorName: "",
  phone: "",
  email: "",
  serviceName: "",
  serviceDescription: "",
  isVatPayer: false,
};

function ImageUploadField({
  label,
  imageUrl,
  disabled,
  disabledHint,
  kind,
}: {
  label: string;
  imageUrl: string | null;
  disabled: boolean;
  disabledHint: string;
  kind: "signature" | "stamp";
}) {
  const upload = useUploadContractImage(kind);
  const fileRef = useRef<HTMLInputElement>(null);

  function handleFile(file: File | undefined) {
    if (!file) return;
    upload.mutate(file, {
      onSuccess: () => toast.success("Зображення завантажено"),
      onError: (err) => toast.error(err.message),
    });
    if (fileRef.current) fileRef.current.value = "";
  }

  return (
    <div>
      <label style={labelStyle}>{label}</label>
      <div style={{ display: "flex", alignItems: "center", gap: 14 }}>
        <div
          style={{
            width: 120,
            height: 72,
            background: "#FFFFFF",
            border: "1px solid #374151",
            borderRadius: 8,
            display: "flex",
            alignItems: "center",
            justifyContent: "center",
            overflow: "hidden",
            flexShrink: 0,
          }}
        >
          {imageUrl ? (
            // eslint-disable-next-line @next/next/no-img-element
            <img
              src={resolveImageUrl(imageUrl)}
              alt={label}
              style={{ maxWidth: "100%", maxHeight: "100%", objectFit: "contain" }}
            />
          ) : (
            <ImageOff size={18} color="#9CA3AF" />
          )}
        </div>
        <div>
          <input
            ref={fileRef}
            type="file"
            accept="image/png,image/jpeg"
            style={{ display: "none" }}
            onChange={(e) => handleFile(e.target.files?.[0])}
          />
          <Btn
            size="sm"
            variant="ghost"
            icon={<Upload size={13} />}
            disabled={disabled || upload.isPending}
            onClick={() => fileRef.current?.click()}
          >
            {upload.isPending ? "Завантаження..." : imageUrl ? "Замінити" : "Завантажити"}
          </Btn>
          <div style={{ color: "#4B5563", fontSize: 11, marginTop: 6 }}>
            {disabled ? disabledHint : "PNG або JPG, до 2 МБ"}
          </div>
        </div>
      </div>
    </div>
  );
}

export function ContractSettingsForm() {
  const { data: settings, isLoading, error } = useContractSettings();
  const upsert = useUpsertContractSettings();
  const [form, setForm] = useState<FormState>(EMPTY_FORM);

  // 404 = ще не заповнено (порожня форма); інші помилки показуємо.
  const notFilledYet = (error as { status?: number } | null)?.status === 404;
  const settingsExist = Boolean(settings);

  useEffect(() => {
    if (settings) {
      setForm({
        legalName: settings.legalName ?? "",
        edrpou: settings.edrpou ?? "",
        iban: settings.iban ?? "",
        bankName: settings.bankName ?? "",
        legalAddress: settings.legalAddress ?? "",
        directorName: settings.directorName ?? "",
        phone: settings.phone ?? "",
        email: settings.email ?? "",
        serviceName: settings.serviceName ?? "",
        serviceDescription: settings.serviceDescription ?? "",
        isVatPayer: settings.isVatPayer,
      });
    }
  }, [settings]);

  function set<K extends keyof FormState>(key: K, value: FormState[K]) {
    setForm((prev) => ({ ...prev, [key]: value }));
  }

  function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!form.legalName.trim()) {
      toast.error("Вкажіть юридичну назву компанії");
      return;
    }
    const body: UpsertContractSettingsRequest = {
      legalName: form.legalName.trim(),
      edrpou: form.edrpou.trim() || null,
      iban: form.iban.trim() || null,
      bankName: form.bankName.trim() || null,
      legalAddress: form.legalAddress.trim() || null,
      directorName: form.directorName.trim() || null,
      phone: form.phone.trim() || null,
      email: form.email.trim() || null,
      serviceName: form.serviceName.trim() || null,
      serviceDescription: form.serviceDescription.trim() || null,
      isVatPayer: form.isVatPayer,
    };
    upsert.mutate(body, {
      onSuccess: () => toast.success("Реквізити збережено"),
      onError: (err) => toast.error(err.message),
    });
  }

  if (isLoading) {
    return <div style={{ color: "#4B5563", fontSize: 13 }}>Завантаження...</div>;
  }

  if (error && !notFilledYet) {
    return (
      <div style={{ color: "#F87171", fontSize: 13 }}>
        Не вдалося завантажити реквізити: {(error as Error).message}
      </div>
    );
  }

  return (
    <form onSubmit={handleSubmit}>
      <div
        style={{
          background: "#111827",
          border: "1px solid #1F2937",
          borderRadius: 12,
          padding: 24,
          maxWidth: 760,
        }}
      >
        <p style={{ color: "#6B7280", fontSize: 13, margin: "0 0 18px" }}>
          Ці дані підставляються в договір про співпрацю. Для схвалення заявок
          обовʼязкові: юридична назва, IBAN і назва послуги/товару.
        </p>

        <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 14 }}>
          <div style={{ gridColumn: "1 / -1" }}>
            <label style={labelStyle}>
              Юридична назва
              <Req />
            </label>
            <input
              value={form.legalName}
              onChange={(e) => set("legalName", e.target.value)}
              placeholder="ТОВ «Приклад»"
              style={inputStyle}
            />
          </div>

          <div>
            <label style={labelStyle}>ЄДРПОУ</label>
            <input
              value={form.edrpou}
              onChange={(e) => set("edrpou", e.target.value)}
              placeholder="12345678"
              style={inputStyle}
            />
          </div>

          <div>
            <label style={labelStyle}>
              IBAN
              <Req />
            </label>
            <input
              value={form.iban}
              onChange={(e) => set("iban", e.target.value)}
              placeholder="UA00 0000 0000 0000 0000 0000 0000 0"
              style={inputStyle}
            />
          </div>

          <div>
            <label style={labelStyle}>Банк</label>
            <input
              value={form.bankName}
              onChange={(e) => set("bankName", e.target.value)}
              placeholder="АТ «Банк»"
              style={inputStyle}
            />
          </div>

          <div>
            <label style={labelStyle}>Директор</label>
            <input
              value={form.directorName}
              onChange={(e) => set("directorName", e.target.value)}
              placeholder="Прізвище Імʼя По батькові"
              style={inputStyle}
            />
          </div>

          <div style={{ gridColumn: "1 / -1" }}>
            <label style={labelStyle}>Юридична адреса</label>
            <input
              value={form.legalAddress}
              onChange={(e) => set("legalAddress", e.target.value)}
              placeholder="м. Київ, вул. ..."
              style={inputStyle}
            />
          </div>

          <div>
            <label style={labelStyle}>Телефон</label>
            <input
              value={form.phone}
              onChange={(e) => set("phone", e.target.value)}
              placeholder="+380..."
              style={inputStyle}
            />
          </div>

          <div>
            <label style={labelStyle}>Email</label>
            <input
              type="email"
              value={form.email}
              onChange={(e) => set("email", e.target.value)}
              placeholder="office@company.ua"
              style={inputStyle}
            />
          </div>

          <div style={{ gridColumn: "1 / -1" }}>
            <label style={labelStyle}>
              Назва послуги/товару
              <Req />
            </label>
            <input
              value={form.serviceName}
              onChange={(e) => set("serviceName", e.target.value)}
              placeholder="Постачання продуктів харчування"
              style={inputStyle}
            />
          </div>

          <div style={{ gridColumn: "1 / -1" }}>
            <label style={labelStyle}>Опис послуги</label>
            <textarea
              value={form.serviceDescription}
              onChange={(e) => set("serviceDescription", e.target.value)}
              placeholder="Деталі предмета договору..."
              rows={3}
              style={{ ...inputStyle, resize: "vertical" }}
            />
          </div>

          <div style={{ gridColumn: "1 / -1" }}>
            <label
              style={{
                display: "inline-flex",
                alignItems: "center",
                gap: 8,
                color: "#9CA3AF",
                fontSize: 13,
                cursor: "pointer",
              }}
            >
              <input
                type="checkbox"
                checked={form.isVatPayer}
                onChange={(e) => set("isVatPayer", e.target.checked)}
                style={{ accentColor: "#3B82F6", width: 15, height: 15 }}
              />
              Платник ПДВ
            </label>
          </div>
        </div>

        <div style={{ display: "flex", justifyContent: "flex-end", marginTop: 20 }}>
          <Btn type="submit" disabled={upsert.isPending}>
            {upsert.isPending ? "Збереження..." : "Зберегти реквізити"}
          </Btn>
        </div>
      </div>

      {/* Images: upload вимагає, щоб реквізити вже існували (спершу PUT) */}
      <div
        style={{
          background: "#111827",
          border: "1px solid #1F2937",
          borderRadius: 12,
          padding: 24,
          maxWidth: 760,
          marginTop: 20,
          display: "grid",
          gridTemplateColumns: "1fr 1fr",
          gap: 20,
        }}
      >
        <ImageUploadField
          label="Підпис директора"
          imageUrl={settings?.signatureImageUrl ?? null}
          disabled={!settingsExist}
          disabledHint="Спершу збережіть реквізити"
          kind="signature"
        />
        <ImageUploadField
          label="Мокра печатка"
          imageUrl={settings?.stampImageUrl ?? null}
          disabled={!settingsExist}
          disabledHint="Спершу збережіть реквізити"
          kind="stamp"
        />
      </div>
    </form>
  );
}
