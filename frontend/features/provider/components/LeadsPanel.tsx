"use client";

import { useEffect, useState } from "react";
import { useTranslations } from "next-intl";
import { toast } from "sonner";
import { Phone, Search } from "lucide-react";
import { Btn } from "@/components/ui/Btn";
import { useProviderLeads, useUpdateLead } from "../hooks/useProviderLeads";
import type { LandingLeadDto, LeadStatusFilter } from "../types";

const PAGE_SIZE = 20;
const TABS: LeadStatusFilter[] = ["new", "processed", "all"];

function formatDate(iso: string): string {
  return new Date(iso).toLocaleString("uk-UA", {
    day: "2-digit",
    month: "2-digit",
    year: "numeric",
    hour: "2-digit",
    minute: "2-digit",
  });
}

function utmLine(l: LandingLeadDto): string {
  return [l.utmSource, l.utmMedium, l.utmCampaign].filter(Boolean).join(" / ");
}

function LeadCard({ lead }: { lead: LandingLeadDto }) {
  const t = useTranslations("Dashboard.providerLeads");
  const update = useUpdateLead();
  const [editing, setEditing] = useState(false);
  const [note, setNote] = useState(lead.adminNote ?? "");

  const onError = () => toast.error(t("saveError"));

  const toggleProcessed = () =>
    update.mutate({ id: lead.id, body: { isProcessed: !lead.isProcessed } }, { onError });

  const saveNote = () =>
    update.mutate(
      { id: lead.id, body: { adminNote: note } },
      { onSuccess: () => setEditing(false), onError },
    );

  const utm = utmLine(lead);
  const muted = { color: "#6B7280", fontSize: 12 } as const;

  return (
    <div
      style={{
        background: "#0D1117",
        border: `1px solid ${lead.isProcessed ? "#1F2937" : "#1D3461"}`,
        borderRadius: 12,
        padding: 16,
        display: "flex",
        flexDirection: "column",
        gap: 10,
      }}
    >
      <div style={{ display: "flex", justifyContent: "space-between", gap: 12, flexWrap: "wrap" }}>
        <div>
          <div style={{ color: "#E8EDF5", fontSize: 15, fontWeight: 600 }}>{lead.name}</div>
          <a
            href={`tel:${lead.phone.replace(/[^\d+]/g, "")}`}
            style={{ color: "#93C5FD", fontSize: 14, display: "inline-flex", alignItems: "center", gap: 6, marginTop: 2 }}
          >
            <Phone size={13} />
            {lead.phone}
          </a>
          {lead.company && <div style={{ color: "#9CA3AF", fontSize: 13, marginTop: 2 }}>{lead.company}</div>}
        </div>
        <div style={{ textAlign: "right" }}>
          <span
            style={{
              display: "inline-block",
              padding: "2px 10px",
              borderRadius: 999,
              fontSize: 11,
              fontWeight: 600,
              background: lead.isProcessed ? "#1a3a2e" : "#1D3461",
              color: lead.isProcessed ? "#4ADE80" : "#93C5FD",
            }}
          >
            {lead.isProcessed ? t("statusProcessed") : t("statusNew")}
          </span>
          <div style={{ ...muted, marginTop: 4 }}>{formatDate(lead.createdAt)}</div>
          {lead.isProcessed && lead.processedAt && (
            <div style={muted}>{t("processedAt", { date: formatDate(lead.processedAt) })}</div>
          )}
        </div>
      </div>

      {lead.message && (
        <p style={{ color: "#D1D5DB", fontSize: 13, margin: 0, whiteSpace: "pre-wrap", wordBreak: "break-word" }}>
          {lead.message}
        </p>
      )}

      <div style={{ ...muted, display: "flex", flexWrap: "wrap", gap: "2px 14px" }}>
        <span>
          {t("colSource")}: {lead.source}
          {lead.pageUrl ? ` (${lead.pageUrl})` : ""}
          {lead.locale ? ` · ${lead.locale}` : ""}
        </span>
        {utm && <span>UTM: {utm}</span>}
        {lead.referrer && <span style={{ wordBreak: "break-all" }}>ref: {lead.referrer}</span>}
      </div>

      {editing ? (
        <div style={{ display: "flex", flexDirection: "column", gap: 8 }}>
          <textarea
            value={note}
            onChange={(e) => setNote(e.target.value)}
            maxLength={1000}
            rows={3}
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
          <div style={{ display: "flex", gap: 8 }}>
            <Btn size="sm" onClick={saveNote} disabled={update.isPending}>
              {t("noteSave")}
            </Btn>
            <Btn
              size="sm"
              variant="ghost"
              onClick={() => {
                setNote(lead.adminNote ?? "");
                setEditing(false);
              }}
            >
              {t("noteCancel")}
            </Btn>
          </div>
        </div>
      ) : (
        lead.adminNote && (
          <div
            style={{
              background: "#111827",
              borderLeft: "3px solid #3B82F6",
              borderRadius: 6,
              padding: "6px 10px",
              color: "#D1D5DB",
              fontSize: 13,
              whiteSpace: "pre-wrap",
              wordBreak: "break-word",
            }}
          >
            {lead.adminNote}
          </div>
        )
      )}

      <div style={{ display: "flex", gap: 8, flexWrap: "wrap" }}>
        <Btn
          size="sm"
          variant={lead.isProcessed ? "ghost" : "success"}
          onClick={toggleProcessed}
          disabled={update.isPending}
        >
          {lead.isProcessed ? t("markUnprocessed") : t("markProcessed")}
        </Btn>
        {!editing && (
          <Btn size="sm" variant="ghost" onClick={() => setEditing(true)}>
            {lead.adminNote ? t("noteEdit") : t("noteAdd")}
          </Btn>
        )}
      </div>
    </div>
  );
}

export function LeadsPanel() {
  const t = useTranslations("Dashboard.providerLeads");
  const [status, setStatus] = useState<LeadStatusFilter>("new");
  const [searchInput, setSearchInput] = useState("");
  const [search, setSearch] = useState("");
  const [page, setPage] = useState(1);

  // Debounce the search box; reset to the first page when the query changes.
  useEffect(() => {
    const id = setTimeout(() => {
      setSearch(searchInput);
      setPage(1);
    }, 300);
    return () => clearTimeout(id);
  }, [searchInput]);

  const { data, isLoading, isError } = useProviderLeads({ status, search, page, pageSize: PAGE_SIZE });
  const items = data?.items ?? [];
  const total = data?.total ?? 0;
  const pages = Math.max(1, Math.ceil(total / PAGE_SIZE));

  const tabLabel: Record<LeadStatusFilter, string> = {
    new: t("tabNew"),
    processed: t("tabProcessed"),
    all: t("tabAll"),
  };

  return (
    <div>
      <div style={{ display: "flex", gap: 12, flexWrap: "wrap", alignItems: "center", marginBottom: 18 }}>
        <div style={{ display: "flex", gap: 6 }}>
          {TABS.map((tab) => (
            <Btn
              key={tab}
              size="sm"
              variant={status === tab ? "primary" : "ghost"}
              onClick={() => {
                setStatus(tab);
                setPage(1);
              }}
            >
              {tabLabel[tab]}
            </Btn>
          ))}
        </div>
        <div style={{ position: "relative", flex: "1 1 240px", maxWidth: 360 }}>
          <Search size={14} color="#6B7280" style={{ position: "absolute", left: 10, top: 10 }} />
          <input
            value={searchInput}
            onChange={(e) => setSearchInput(e.target.value)}
            placeholder={t("searchPlaceholder")}
            style={{
              width: "100%",
              background: "#111827",
              border: "1px solid #374151",
              borderRadius: 8,
              color: "#E8EDF5",
              fontSize: 13,
              padding: "7px 10px 7px 30px",
            }}
          />
        </div>
        <span style={{ color: "#6B7280", fontSize: 12 }}>{t("total", { count: total })}</span>
      </div>

      {isLoading ? (
        <p style={{ color: "#4B5563", fontSize: 13 }}>{t("loading")}</p>
      ) : isError ? (
        <p style={{ color: "#F87171", fontSize: 13 }}>{t("loadError")}</p>
      ) : items.length === 0 ? (
        <p style={{ color: "#4B5563", fontSize: 14 }}>{t("empty")}</p>
      ) : (
        <div style={{ display: "grid", gap: 12, gridTemplateColumns: "repeat(auto-fill, minmax(340px, 1fr))" }}>
          {items.map((lead) => (
            <LeadCard key={`${lead.id}:${lead.isProcessed}:${lead.adminNote ?? ""}`} lead={lead} />
          ))}
        </div>
      )}

      {pages > 1 && (
        <div style={{ display: "flex", alignItems: "center", gap: 12, marginTop: 20 }}>
          <Btn size="sm" variant="ghost" disabled={page <= 1} onClick={() => setPage((p) => p - 1)}>
            {t("prev")}
          </Btn>
          <span style={{ color: "#9CA3AF", fontSize: 12 }}>{t("page", { page, pages })}</span>
          <Btn size="sm" variant="ghost" disabled={page >= pages} onClick={() => setPage((p) => p + 1)}>
            {t("next")}
          </Btn>
        </div>
      )}
    </div>
  );
}
