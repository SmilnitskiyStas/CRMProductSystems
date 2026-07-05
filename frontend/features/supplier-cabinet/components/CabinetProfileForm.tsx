"use client";

import { useEffect, useState } from "react";
import { Eye, EyeOff } from "lucide-react";
import { Btn } from "@/components/ui/Btn";
import { PlanBadge } from "@/features/marketplace/components/PlanBadge";
import {
  useCabinetProfile,
  useUpdateCabinetProfile,
  useTogglePublish,
} from "../hooks/useSupplierCabinet";

const INPUT_STYLE: React.CSSProperties = {
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

const LABEL_STYLE: React.CSSProperties = {
  display: "block",
  color: "#9CA3AF",
  fontSize: 12,
  fontWeight: 500,
  marginBottom: 6,
};

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div>
      <label style={LABEL_STYLE}>{label}</label>
      {children}
    </div>
  );
}

/** Comma-separated list → trimmed string array (empty entries dropped). */
function parseList(raw: string): string[] {
  return raw
    .split(",")
    .map((s) => s.trim())
    .filter(Boolean);
}

export function CabinetProfileForm() {
  const { data: profile, isLoading, isError, error } = useCabinetProfile();
  const update = useUpdateCabinetProfile();
  const publish = useTogglePublish();

  const [region, setRegion] = useState("");
  const [categoriesRaw, setCategoriesRaw] = useState("");
  const [website, setWebsite] = useState("");
  const [deliveryRegionsRaw, setDeliveryRegionsRaw] = useState("");
  const [workingHours, setWorkingHours] = useState("");
  const [paymentTerms, setPaymentTerms] = useState("");
  const [message, setMessage] = useState<{ kind: "ok" | "err"; text: string } | null>(null);

  // Seed the form when the profile arrives
  useEffect(() => {
    if (!profile) return;
    setRegion(profile.region ?? "");
    setCategoriesRaw((profile.categories ?? []).join(", "));
    setWebsite(profile.website ?? "");
    setDeliveryRegionsRaw((profile.deliveryRegions ?? []).join(", "));
    setWorkingHours(profile.workingHours ?? "");
    setPaymentTerms(profile.paymentTerms ?? "");
  }, [profile]);

  if (isLoading) {
    return <div style={{ height: 320, background: "#111827", borderRadius: 12 }} />;
  }

  if (isError || !profile) {
    return (
      <div style={{ color: "#F87171", fontSize: 13 }}>
        Кабінет постачальника недоступний.{" "}
        {error instanceof Error ? error.message : ""}
      </div>
    );
  }

  function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setMessage(null);
    update.mutate(
      {
        region: region.trim() || undefined,
        categories: parseList(categoriesRaw),
        website: website.trim() || undefined,
        deliveryRegions: parseList(deliveryRegionsRaw),
        workingHours: workingHours.trim() || undefined,
        paymentTerms: paymentTerms.trim() || undefined,
      },
      {
        onSuccess: () => setMessage({ kind: "ok", text: "Профіль збережено." }),
        onError: (err) =>
          setMessage({
            kind: "err",
            text: err instanceof Error ? err.message : "Помилка збереження профілю.",
          }),
      }
    );
  }

  function handlePublishToggle() {
    setMessage(null);
    publish.mutate(undefined, {
      onSuccess: (p) =>
        setMessage({
          kind: "ok",
          text: p.isPublic
            ? "Профіль опубліковано — видимий у маркетплейсі."
            : "Профіль приховано з маркетплейсу.",
        }),
      onError: (err) =>
        setMessage({
          kind: "err",
          text: err instanceof Error ? err.message : "Не вдалося змінити видимість.",
        }),
    });
  }

  return (
    <div
      style={{
        background: "#111827",
        border: "1px solid #1F2937",
        borderRadius: 12,
        padding: "24px 28px",
      }}
    >
      {/* Header: name + plan + publish state */}
      <div
        style={{
          display: "flex",
          alignItems: "center",
          justifyContent: "space-between",
          gap: 12,
          flexWrap: "wrap",
          marginBottom: 20,
        }}
      >
        <div style={{ display: "flex", alignItems: "center", gap: 10, flexWrap: "wrap" }}>
          <h2 style={{ color: "#E8EDF5", fontSize: 17, fontWeight: 700, margin: 0 }}>
            {profile.supplierName}
          </h2>
          <PlanBadge plan={profile.plan} />
          <span
            style={{
              padding: "2px 10px",
              borderRadius: 6,
              fontSize: 11,
              fontWeight: 600,
              background: profile.isPublic ? "#052e16" : "#1c1917",
              color: profile.isPublic ? "#4ADE80" : "#6B7280",
            }}
          >
            {profile.isPublic ? "Опубліковано" : "Приховано"}
          </span>
        </div>
        <Btn
          variant={profile.isPublic ? "ghost" : "success"}
          onClick={handlePublishToggle}
          disabled={publish.isPending}
          icon={profile.isPublic ? <EyeOff size={14} /> : <Eye size={14} />}
        >
          {publish.isPending
            ? "Збереження…"
            : profile.isPublic
            ? "Приховати з маркетплейсу"
            : "Опублікувати"}
        </Btn>
      </div>

      <form onSubmit={handleSubmit} style={{ display: "flex", flexDirection: "column", gap: 14 }}>
        <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 14 }}>
          <Field label="Регіон">
            <input
              style={INPUT_STYLE}
              value={region}
              onChange={(e) => setRegion(e.target.value)}
              placeholder="Київська область"
            />
          </Field>
          <Field label="Вебсайт">
            <input
              style={INPUT_STYLE}
              value={website}
              onChange={(e) => setWebsite(e.target.value)}
              placeholder="https://example.com"
            />
          </Field>
        </div>

        <Field label="Категорії (через кому)">
          <input
            style={INPUT_STYLE}
            value={categoriesRaw}
            onChange={(e) => setCategoriesRaw(e.target.value)}
            placeholder="Молочні продукти, Бакалія"
          />
        </Field>

        <Field label="Регіони доставки (через кому)">
          <input
            style={INPUT_STYLE}
            value={deliveryRegionsRaw}
            onChange={(e) => setDeliveryRegionsRaw(e.target.value)}
            placeholder="Київ, Львів, Одеса"
          />
        </Field>

        <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 14 }}>
          <Field label="Графік роботи">
            <input
              style={INPUT_STYLE}
              value={workingHours}
              onChange={(e) => setWorkingHours(e.target.value)}
              placeholder="Пн–Пт 9:00–18:00"
            />
          </Field>
          <Field label="Умови оплати">
            <input
              style={INPUT_STYLE}
              value={paymentTerms}
              onChange={(e) => setPaymentTerms(e.target.value)}
              placeholder="Передоплата / відтермінування 14 днів"
            />
          </Field>
        </div>

        {message && (
          <div
            style={{
              padding: "10px 14px",
              borderRadius: 8,
              fontSize: 13,
              background: message.kind === "ok" ? "#052e16" : "#1c0707",
              border: `1px solid ${message.kind === "ok" ? "#166534" : "#7f1d1d"}`,
              color: message.kind === "ok" ? "#4ADE80" : "#F87171",
            }}
          >
            {message.text}
          </div>
        )}

        <div style={{ display: "flex", justifyContent: "flex-end" }}>
          <Btn type="submit" disabled={update.isPending}>
            {update.isPending ? "Збереження…" : "Зберегти профіль"}
          </Btn>
        </div>
      </form>
    </div>
  );
}
