"use client";

import { useEffect, useState } from "react";
import { useTranslations } from "next-intl";
import { Eye, EyeOff } from "lucide-react";
import { Btn } from "@/components/ui/Btn";
import { PlanBadge } from "@/features/marketplace/components/PlanBadge";
import { RegionSelect } from "@/features/geo/components/RegionSelect";
import { DeliveryCoverageEditor } from "@/features/geo/components/DeliveryCoverageEditor";
import type { DeliveryCoverage } from "@/features/geo/types";
import {
  useCabinetProfile,
  useUpdateCabinetProfile,
  useTogglePublish,
} from "../hooks/useSupplierCabinet";
import { useItemCategories } from "@/features/marketplace/hooks/useMarketplace";

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

export function CabinetProfileForm() {
  const t = useTranslations("Dashboard.supplierCabinet.profileForm");
  const { data: profile, isLoading, isError, error } = useCabinetProfile();
  const { data: itemCategories = [] } = useItemCategories();
  const update = useUpdateCabinetProfile();
  const publish = useTogglePublish();

  const [region, setRegion] = useState("");
  const [categories, setCategories] = useState<Set<string>>(new Set());
  const [website, setWebsite] = useState("");
  const [deliveryCoverage, setDeliveryCoverage] = useState<DeliveryCoverage | null>(null);
  const [workingHours, setWorkingHours] = useState("");
  const [paymentTerms, setPaymentTerms] = useState("");
  const [message, setMessage] = useState<{ kind: "ok" | "err"; text: string } | null>(null);

  // Seed the form when the profile arrives
  useEffect(() => {
    if (!profile) return;
    setRegion(profile.region ?? "");
    setCategories(new Set(profile.categories ?? []));
    setWebsite(profile.website ?? "");
    setDeliveryCoverage(profile.deliveryCoverage ?? null);
    setWorkingHours(profile.workingHours ?? "");
    setPaymentTerms(profile.paymentTerms ?? "");
  }, [profile]);

  if (isLoading) {
    return <div style={{ height: 320, background: "#111827", borderRadius: 12 }} />;
  }

  if (isError || !profile) {
    return (
      <div style={{ color: "#F87171", fontSize: 13 }}>
        {t("errorLoad")}{" "}
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
        categories: Array.from(categories),
        website: website.trim() || undefined,
        deliveryCoverage: deliveryCoverage ?? undefined,
        workingHours: workingHours.trim() || undefined,
        paymentTerms: paymentTerms.trim() || undefined,
      },
      {
        onSuccess: () => setMessage({ kind: "ok", text: t("savedProfile") }),
        onError: (err) =>
          setMessage({
            kind: "err",
            text: err instanceof Error ? err.message : t("errorSaveDefault"),
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
            ? t("publishedBody")
            : t("hiddenBody"),
        }),
      onError: (err) =>
        setMessage({
          kind: "err",
          text: err instanceof Error ? err.message : t("errorPublishToggleDefault"),
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
            {profile.isPublic ? t("publishedBadge") : t("hiddenBadge")}
          </span>
        </div>
        <Btn
          variant={profile.isPublic ? "ghost" : "success"}
          onClick={handlePublishToggle}
          disabled={publish.isPending}
          icon={profile.isPublic ? <EyeOff size={14} /> : <Eye size={14} />}
        >
          {publish.isPending
            ? t("savingButton")
            : profile.isPublic
            ? t("hideButton")
            : t("publishButton")}
        </Btn>
      </div>

      <form onSubmit={handleSubmit} style={{ display: "flex", flexDirection: "column", gap: 14 }}>
        <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 14 }}>
          <Field label={t("regionLabel")}>
            <RegionSelect
              value={region || null}
              onChange={(code) => setRegion(code ?? "")}
              allowEmpty
              placeholder={t("regionSelectPlaceholder")}
            />
          </Field>
          <Field label={t("websiteLabel")}>
            <input
              style={INPUT_STYLE}
              value={website}
              onChange={(e) => setWebsite(e.target.value)}
              placeholder="https://example.com"
            />
          </Field>
        </div>

        <Field label={t("categoriesLabel")}>
          <div style={{ display: "flex", flexWrap: "wrap", gap: "10px 20px" }}>
            {itemCategories.map((cat) => (
              <label
                key={cat.key}
                style={{
                  display: "flex",
                  alignItems: "center",
                  gap: 6,
                  color: "#E8EDF5",
                  fontSize: 13,
                  cursor: "pointer",
                }}
              >
                <input
                  type="checkbox"
                  checked={categories.has(cat.key)}
                  onChange={(e) => {
                    setCategories((prev) => {
                      const next = new Set(prev);
                      if (e.target.checked) {
                        next.add(cat.key);
                      } else {
                        next.delete(cat.key);
                      }
                      return next;
                    });
                  }}
                />
                {cat.labelUa}
              </label>
            ))}
          </div>
        </Field>

        <Field label={t("deliveryCoverageLabel")}>
          <DeliveryCoverageEditor
            value={deliveryCoverage}
            onChange={setDeliveryCoverage}
          />
        </Field>

        <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 14 }}>
          <Field label={t("workingHoursLabel")}>
            <input
              style={INPUT_STYLE}
              value={workingHours}
              onChange={(e) => setWorkingHours(e.target.value)}
              placeholder={t("workingHoursPlaceholder")}
            />
          </Field>
          <Field label={t("paymentTermsLabel")}>
            <input
              style={INPUT_STYLE}
              value={paymentTerms}
              onChange={(e) => setPaymentTerms(e.target.value)}
              placeholder={t("paymentTermsPlaceholder")}
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
            {update.isPending ? t("savingButton") : t("saveButton")}
          </Btn>
        </div>
      </form>
    </div>
  );
}
