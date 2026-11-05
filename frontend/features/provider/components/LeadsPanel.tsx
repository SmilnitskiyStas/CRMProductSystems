"use client";

import { useEffect, useState } from "react";
import { useTranslations } from "next-intl";
import { toast } from "sonner";
import { Phone, Search } from "lucide-react";
import { Btn } from "@/components/ui/Btn";
import { Pagination } from "@/components/ui/Pagination";
import { StatusPill } from "@/components/ui/StatusPill";
import { inputStyle } from "@/components/ui/fieldStyles";
import { useProviderLeads, useUpdateLead } from "../hooks/useProviderLeads";
import { LEAD_REQUEST_TYPES } from "../types";
import type { LandingLeadDto, LeadRequestTypeFilter, LeadStatusFilter } from "../types";
import { isVerifiedBeauty, leadRequestType } from "../lib/leadRequest";
import { LeadDetailDrawer, formatLeadDate } from "./LeadDetailDrawer";

const PAGE_SIZE = 20;
const TABS: LeadStatusFilter[] = ["new", "processed", "all"];

function utmLine(l: LandingLeadDto): string {
  return [l.utmSource, l.utmMedium, l.utmCampaign].filter(Boolean).join(" / ");
}

function LeadCard({ lead, onOpen }: { lead: LandingLeadDto; onOpen: () => void }) {
  const t = useTranslations("Dashboard.providerLeads");
  const update = useUpdateLead();

  const toggleProcessed = () =>
    update.mutate(
      { id: lead.id, body: { isProcessed: !lead.isProcessed } },
      { onError: () => toast.error(t("saveError")) },
    );

  const utm = utmLine(lead);
  const requestType = leadRequestType(lead);
  const beauty = isVerifiedBeauty(lead);
  const muted = { color: "#6B7280", fontSize: 12 } as const;

  return (
    <div
      role="button"
      tabIndex={0}
      onClick={onOpen}
      onKeyDown={(e) => {
        if (e.target === e.currentTarget && (e.key === "Enter" || e.key === " ")) {
          e.preventDefault();
          onOpen();
        }
      }}
      style={{
        background: "#0D1117",
        border: `1px solid ${lead.isProcessed ? "#1F2937" : "#1D3461"}`,
        borderRadius: 12,
        padding: 16,
        display: "flex",
        flexDirection: "column",
        gap: 10,
        cursor: "pointer",
      }}
    >
      <div style={{ display: "flex", justifyContent: "space-between", gap: 12, flexWrap: "wrap" }}>
        <div>
          <div style={{ color: "#E8EDF5", fontSize: 15, fontWeight: 600 }}>{lead.name}</div>
          <a
            href={`tel:${lead.phone.replace(/[^\d+]/g, "")}`}
            onClick={(e) => e.stopPropagation()}
            style={{ color: "#93C5FD", fontSize: 14, display: "inline-flex", alignItems: "center", gap: 6, marginTop: 2 }}
          >
            <Phone size={13} />
            {lead.phone}
          </a>
          {lead.company && <div style={{ color: "#9CA3AF", fontSize: 13, marginTop: 2 }}>{lead.company}</div>}
        </div>
        <div style={{ textAlign: "right" }}>
          {requestType && (
            <StatusPill bg="#2D1B69" text="#C4B5FD" style={{ marginRight: 6 }}>
              {t(`requestType.${requestType}`)}
            </StatusPill>
          )}
          {beauty && (
            <StatusPill bg="#1a3a2e" text="#4ADE80" style={{ marginRight: 6, fontWeight: 700 }}>
              {t("badgeVerifiedBeauty")}
            </StatusPill>
          )}
          {lead.isCritical && (
            <StatusPill bg="#4C1D1D" text="#FCA5A5" style={{ marginRight: 6, fontWeight: 700 }}>
              {t("badgeCritical")}
            </StatusPill>
          )}
          <StatusPill bg={lead.isProcessed ? "#1a3a2e" : "#1D3461"} text={lead.isProcessed ? "#4ADE80" : "#93C5FD"}>
            {lead.isProcessed ? t("statusProcessed") : t("statusNew")}
          </StatusPill>
          <div style={{ ...muted, marginTop: 4 }}>{formatLeadDate(lead.createdAt)}</div>
        </div>
      </div>

      {lead.message && (
        <p
          style={{
            color: "#D1D5DB",
            fontSize: 13,
            margin: 0,
            whiteSpace: "pre-wrap",
            wordBreak: "break-word",
            display: "-webkit-box",
            WebkitLineClamp: 3,
            WebkitBoxOrient: "vertical",
            overflow: "hidden",
          }}
        >
          {lead.message}
        </p>
      )}

      <div style={{ ...muted, display: "flex", flexWrap: "wrap", gap: "2px 14px" }}>
        <span>
          {t("colSource")}: {beauty ? t("sourceBeauty") : lead.source}
        </span>
        {beauty && (lead.beautyTenantName || lead.beautyTenantSlug) && (
          <span>
            {t("beautyBusiness")}: {lead.beautyTenantName ?? lead.beautyTenantSlug}
          </span>
        )}
        {utm && <span>UTM: {utm}</span>}
        {lead.adminNote && <span>{t("note")}: ✓</span>}
      </div>

      <div style={{ display: "flex", gap: 8, flexWrap: "wrap" }} onClick={(e) => e.stopPropagation()}>
        <Btn size="sm" onClick={onOpen}>
          {t("open")}
        </Btn>
        <Btn
          size="sm"
          variant={lead.isProcessed ? "ghost" : "success"}
          onClick={toggleProcessed}
          disabled={update.isPending}
        >
          {lead.isProcessed ? t("markUnprocessed") : t("markProcessed")}
        </Btn>
      </div>
    </div>
  );
}

export function LeadsPanel() {
  const t = useTranslations("Dashboard.providerLeads");
  const [status, setStatus] = useState<LeadStatusFilter>("new");
  const [requestType, setRequestType] = useState<LeadRequestTypeFilter>("all");
  const [searchInput, setSearchInput] = useState("");
  const [search, setSearch] = useState("");
  const [page, setPage] = useState(1);
  const [openId, setOpenId] = useState<string | null>(null);

  // Debounce the search box; reset to the first page when the query changes.
  useEffect(() => {
    const id = setTimeout(() => {
      setSearch(searchInput);
      setPage(1);
    }, 300);
    return () => clearTimeout(id);
  }, [searchInput]);

  const { data, isLoading, isError } = useProviderLeads({ status, search, page, pageSize: PAGE_SIZE, requestType });
  // The server filters by requestType; re-checking here keeps the list correct if it ignores the param.
  const items = (data?.items ?? []).filter((l) => requestType === "all" || leadRequestType(l) === requestType);
  const openLead = items.find((l) => l.id === openId) ?? null;
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
        <select
          aria-label={t("filterRequestType")}
          value={requestType}
          onChange={(e) => {
            setRequestType(e.target.value as LeadRequestTypeFilter);
            setPage(1);
          }}
          style={{
            background: "#111827",
            border: "1px solid #374151",
            borderRadius: 8,
            color: "#E8EDF5",
            fontSize: 13,
            padding: "7px 10px",
          }}
        >
          <option value="all">{t("requestTypeAll")}</option>
          {LEAD_REQUEST_TYPES.map((rt) => (
            <option key={rt} value={rt}>
              {t(`requestType.${rt}`)}
            </option>
          ))}
        </select>
        <div style={{ position: "relative", flex: "1 1 240px", maxWidth: 360 }}>
          <Search size={14} color="#6B7280" style={{ position: "absolute", left: 10, top: 10 }} />
          <input
            value={searchInput}
            onChange={(e) => setSearchInput(e.target.value)}
            placeholder={t("searchPlaceholder")}
            style={{ ...inputStyle, padding: "7px 10px 7px 30px" }}
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
            <LeadCard key={lead.id} lead={lead} onOpen={() => setOpenId(lead.id)} />
          ))}
        </div>
      )}

      <Pagination page={page} totalPages={pages} totalCount={total} onPageChange={setPage} />

      <LeadDetailDrawer key={openLead?.id ?? "none"} lead={openLead} onClose={() => setOpenId(null)} />
    </div>
  );
}
