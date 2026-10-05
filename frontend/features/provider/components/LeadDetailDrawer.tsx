"use client";

import { useState } from "react";
import { useTranslations } from "next-intl";
import { toast } from "sonner";
import { Phone } from "lucide-react";
import { Btn } from "@/components/ui/Btn";
import { DetailDrawer } from "@/components/ui/DetailDrawer";
import { useUpdateLead } from "../hooks/useProviderLeads";
import type { LandingLeadDto } from "../types";

export function formatLeadDate(iso: string): string {
  return new Date(iso).toLocaleString("uk-UA", {
    day: "2-digit",
    month: "2-digit",
    year: "numeric",
    hour: "2-digit",
    minute: "2-digit",
  });
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div style={{ display: "grid", gridTemplateColumns: "120px 1fr", gap: 12, padding: "6px 0" }}>
      <span style={{ color: "#6B7280", fontSize: 12 }}>{label}</span>
      <span style={{ color: "#D1D5DB", fontSize: 13, wordBreak: "break-word" }}>{children}</span>
    </div>
  );
}

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <section style={{ marginBottom: 22 }}>
      <h3
        style={{
          color: "#9CA3AF",
          fontSize: 11,
          fontWeight: 700,
          letterSpacing: "0.06em",
          textTransform: "uppercase",
          margin: "0 0 8px",
        }}
      >
        {title}
      </h3>
      {children}
    </section>
  );
}

/** Referrers/page URLs come from the public form — only render http(s) values as links. */
function SafeLink({ value }: { value: string }) {
  if (/^https?:\/\//i.test(value)) {
    return (
      <a href={value} target="_blank" rel="noopener noreferrer" style={{ color: "#93C5FD" }}>
        {value}
      </a>
    );
  }
  return <>{value}</>;
}

export function LeadDetailDrawer({
  lead,
  onClose,
}: {
  lead: LandingLeadDto | null;
  onClose: () => void;
}) {
  const t = useTranslations("Dashboard.providerLeads");
  const update = useUpdateLead();
  const [note, setNote] = useState(lead?.adminNote ?? "");

  if (!lead) return null;

  const onError = () => toast.error(t("saveError"));
  const noteChanged = note.trim() !== (lead.adminNote ?? "");

  const toggleProcessed = () =>
    update.mutate({ id: lead.id, body: { isProcessed: !lead.isProcessed } }, { onError });

  const saveNote = () =>
    update.mutate({ id: lead.id, body: { adminNote: note } }, { onError });

  const utm = [lead.utmSource, lead.utmMedium, lead.utmCampaign].filter(Boolean).join(" / ");
  const dash = <span style={{ color: "#4B5563" }}>—</span>;

  return (
    <DetailDrawer
      isOpen
      onClose={onClose}
      title={lead.name}
      subtitle={`${t("colDate")}: ${formatLeadDate(lead.createdAt)}`}
      actions={
        <Btn
          size="sm"
          variant={lead.isProcessed ? "ghost" : "success"}
          onClick={toggleProcessed}
          disabled={update.isPending}
        >
          {lead.isProcessed ? t("markUnprocessed") : t("markProcessed")}
        </Btn>
      }
    >
      <Section title={t("sectionContact")}>
        <Field label={t("fieldStatus")}>
          <span style={{ color: lead.isProcessed ? "#4ADE80" : "#93C5FD", fontWeight: 600 }}>
            {lead.isProcessed ? t("statusProcessed") : t("statusNew")}
          </span>
        </Field>
        <Field label={t("fieldPhone")}>
          <a
            href={`tel:${lead.phone.replace(/[^\d+]/g, "")}`}
            style={{ color: "#93C5FD", display: "inline-flex", alignItems: "center", gap: 6 }}
          >
            <Phone size={13} />
            {lead.phone}
          </a>
        </Field>
        <Field label={t("colCompany")}>{lead.company ?? dash}</Field>
      </Section>

      <Section title={t("sectionMessage")}>
        {lead.message ? (
          <p style={{ color: "#D1D5DB", fontSize: 14, margin: 0, whiteSpace: "pre-wrap", wordBreak: "break-word" }}>
            {lead.message}
          </p>
        ) : (
          <span style={{ color: "#6B7280", fontSize: 13 }}>{t("noMessage")}</span>
        )}
      </Section>

      <Section title={t("sectionOrigin")}>
        <Field label={t("colSource")}>{lead.source}</Field>
        <Field label={t("fieldPage")}>{lead.pageUrl ? <SafeLink value={lead.pageUrl} /> : dash}</Field>
        <Field label={t("fieldLocale")}>{lead.locale ?? dash}</Field>
        <Field label={t("fieldReferrer")}>{lead.referrer ? <SafeLink value={lead.referrer} /> : dash}</Field>
        <Field label="UTM">{utm || dash}</Field>
      </Section>

      <Section title={t("sectionHistory")}>
        <Field label={t("fieldCreated")}>{formatLeadDate(lead.createdAt)}</Field>
        <Field label={t("fieldProcessed")}>{lead.processedAt ? formatLeadDate(lead.processedAt) : dash}</Field>
      </Section>

      <Section title={t("note")}>
        <textarea
          value={note}
          onChange={(e) => setNote(e.target.value)}
          maxLength={1000}
          rows={4}
          placeholder={t("notePlaceholder")}
          style={{
            width: "100%",
            background: "#111827",
            border: "1px solid #374151",
            borderRadius: 8,
            color: "#E8EDF5",
            fontSize: 13,
            padding: 8,
            resize: "vertical",
          }}
        />
        <div style={{ marginTop: 8 }}>
          <Btn size="sm" onClick={saveNote} disabled={update.isPending || !noteChanged}>
            {t("noteSave")}
          </Btn>
        </div>
      </Section>
    </DetailDrawer>
  );
}
