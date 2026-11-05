"use client";

import { useState } from "react";
import { useTranslations, useLocale } from "next-intl";
import { Filter, RefreshCw } from "lucide-react";
import { Btn } from "@/components/ui/Btn";
import { Pagination } from "@/components/ui/Pagination";
import { useProviderLogs } from "../hooks/useProvider";
import { useTenants } from "../hooks/useProvider";
import type { ProviderLogsFilter } from "../types";

// ── Constants ──────────────────────────────────────────────────────────────────

const PAGE_SIZE = 50;

const KNOWN_ACTIONS = [
  "user.login",
  "user.invited",
  "user.updated",
  "user.deactivated",
  "user.profile_updated",
  "user.password_changed",
  "user.telegram_linked",
  "user.permissions_updated",
  "provider.impersonate",
];

const ACTION_COLOR: Record<string, { dot: string; badge: string; text: string }> = {
  "user.login":               { dot: "#6EE7B7", badge: "#052E16", text: "#6EE7B7" },
  "user.invited":             { dot: "#93C5FD", badge: "#0F172A", text: "#93C5FD" },
  "user.updated":             { dot: "#FCD34D", badge: "#1C1500", text: "#FCD34D" },
  "user.deactivated":         { dot: "#F87171", badge: "#2A0A0A", text: "#F87171" },
  "user.profile_updated":     { dot: "#A5F3FC", badge: "#0A1F22", text: "#A5F3FC" },
  "user.password_changed":    { dot: "#FDA4AF", badge: "#2A0A10", text: "#FDA4AF" },
  "user.telegram_linked":     { dot: "#67E8F9", badge: "#051214", text: "#67E8F9" },
  "user.permissions_updated": { dot: "#C4B5FD", badge: "#1A0F2E", text: "#C4B5FD" },
  "provider.impersonate":     { dot: "#F9A8D4", badge: "#2D0A1A", text: "#F9A8D4" },
};

const DEFAULT_COLOR = { dot: "#374151", badge: "#111827", text: "#6B7280" };

function getColor(action: string) {
  return ACTION_COLOR[action] ?? DEFAULT_COLOR;
}

// ── Helpers ────────────────────────────────────────────────────────────────────

function formatDate(iso: string, locale: string) {
  return new Date(iso).toLocaleString(locale, {
    day: "2-digit", month: "2-digit", year: "numeric",
    hour: "2-digit", minute: "2-digit", second: "2-digit",
  });
}

function actionLabel(t: ReturnType<typeof useTranslations>, action: string): string {
  return (KNOWN_ACTIONS as readonly string[]).includes(action) ? t(`actions.${action}`) : action;
}

// ── Sub-components ─────────────────────────────────────────────────────────────

function FilterSelect({
  label, value, onChange, children,
}: {
  label: string;
  value: string;
  onChange: (v: string) => void;
  children: React.ReactNode;
}) {
  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 4 }}>
      <span style={{ color: "#4B5563", fontSize: 11, fontWeight: 600, textTransform: "uppercase", letterSpacing: "0.04em" }}>
        {label}
      </span>
      <select
        value={value}
        onChange={(e) => onChange(e.target.value)}
        style={{
          background: "#0D1117",
          border: "1px solid #1F2937",
          borderRadius: 7,
          color: value ? "#E8EDF5" : "#4B5563",
          fontSize: 12,
          padding: "7px 10px",
          outline: "none",
          cursor: "pointer",
          minWidth: 160,
        }}
      >
        {children}
      </select>
    </div>
  );
}

function DateInput({ label, value, onChange }: { label: string; value: string; onChange: (v: string) => void }) {
  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 4 }}>
      <span style={{ color: "#4B5563", fontSize: 11, fontWeight: 600, textTransform: "uppercase", letterSpacing: "0.04em" }}>
        {label}
      </span>
      <input
        type="date"
        value={value}
        onChange={(e) => onChange(e.target.value)}
        style={{
          background: "#0D1117",
          border: "1px solid #1F2937",
          borderRadius: 7,
          color: value ? "#E8EDF5" : "#4B5563",
          fontSize: 12,
          padding: "7px 10px",
          outline: "none",
          colorScheme: "dark",
        }}
      />
    </div>
  );
}

// ── Main Component ─────────────────────────────────────────────────────────────

export function ProviderLogsPanel({ initialTenantId }: { initialTenantId?: string }) {
  const t = useTranslations("Dashboard.provider.logsPanel");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";
  const { data: tenants } = useTenants();

  const [tenantId, setTenantId] = useState(initialTenantId ?? "");
  const [action,   setAction]   = useState("");
  const [dateFrom, setDateFrom] = useState("");
  const [dateTo,   setDateTo]   = useState("");
  const [page,     setPage]     = useState(1);

  const filter: ProviderLogsFilter = {
    tenantId: tenantId || undefined,
    action:   action   || undefined,
    dateFrom: dateFrom ? `${dateFrom}T00:00:00Z` : undefined,
    dateTo:   dateTo   ? `${dateTo}T23:59:59Z`   : undefined,
    page,
    pageSize: PAGE_SIZE,
  };

  const { data, isLoading, refetch } = useProviderLogs(filter);

  const totalPages = data ? Math.max(1, Math.ceil(data.total / PAGE_SIZE)) : 1;

  function resetFilters() {
    setTenantId("");
    setAction("");
    setDateFrom("");
    setDateTo("");
    setPage(1);
  }

  const hasFilter = tenantId || action || dateFrom || dateTo;

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 16 }}>

      {/* Filter bar */}
      <div
        style={{
          display: "flex",
          alignItems: "flex-end",
          gap: 12,
          flexWrap: "wrap",
          padding: "14px 16px",
          background: "#080D14",
          border: "1px solid #1F2937",
          borderRadius: 10,
        }}
      >
        <div style={{ display: "flex", alignItems: "center", gap: 6, color: "#4B5563", marginBottom: 2 }}>
          <Filter size={13} />
          <span style={{ fontSize: 12, fontWeight: 600 }}>{t("filtersTitle")}</span>
        </div>

        <FilterSelect label={t("tenantLabel")} value={tenantId} onChange={(v) => { setTenantId(v); setPage(1); }}>
          <option value="">{t("allClientsOption")}</option>
          {(tenants ?? []).map((tenantOpt) => (
            <option key={tenantOpt.id} value={tenantOpt.id}>{tenantOpt.name}</option>
          ))}
        </FilterSelect>

        <FilterSelect label={t("actionLabel")} value={action} onChange={(v) => { setAction(v); setPage(1); }}>
          <option value="">{t("allActionsOption")}</option>
          {KNOWN_ACTIONS.map((a) => (
            <option key={a} value={a}>{actionLabel(t, a)}</option>
          ))}
        </FilterSelect>

        <DateInput label={t("fromLabel")} value={dateFrom} onChange={(v) => { setDateFrom(v); setPage(1); }} />
        <DateInput label={t("toLabel")}  value={dateTo}   onChange={(v) => { setDateTo(v);   setPage(1); }} />

        <div style={{ display: "flex", gap: 8, marginLeft: "auto" }}>
          {hasFilter && (
            <Btn variant="ghost" size="sm" onClick={resetFilters}>
              {t("resetButton")}
            </Btn>
          )}
          <Btn variant="ghost" size="sm" icon={<RefreshCw size={12} />} onClick={() => refetch()}>
            {t("refreshButton")}
          </Btn>
        </div>
      </div>

      {/* Stats row */}
      {data && (
        <div style={{ color: "#4B5563", fontSize: 12 }}>
          {t("foundPrefix")} <span style={{ color: "#E8EDF5", fontWeight: 600 }}>{data.total}</span> {t("foundSuffix")}
          {totalPages > 1 && (
            <span>{t("pageIndicator", { page, totalPages })}</span>
          )}
        </div>
      )}

      {/* Log rows */}
      {isLoading ? (
        <div style={{ color: "#4B5563", fontSize: 13, padding: "16px 0" }}>{t("loading")}</div>
      ) : !data?.items.length ? (
        <div style={{ color: "#4B5563", fontSize: 13, padding: "16px 0" }}>
          {hasFilter ? t("emptyFiltered") : t("emptyAll")}
        </div>
      ) : (
        <div
          style={{
            background: "#080D14",
            border: "1px solid #1F2937",
            borderRadius: 10,
            overflow: "hidden",
          }}
        >
          {data.items.map((log, i) => {
            const color = getColor(log.action);
            const tenantName = tenants?.find((tn) => tn.id === log.tenantId)?.name;
            return (
              <div
                key={log.id}
                style={{
                  display: "grid",
                  gridTemplateColumns: "8px 1fr auto",
                  alignItems: "start",
                  gap: 12,
                  padding: "12px 16px",
                  borderBottom: i < data.items.length - 1 ? "1px solid #0D1117" : "none",
                }}
              >
                {/* Dot */}
                <div
                  style={{
                    width: 8, height: 8,
                    borderRadius: "50%",
                    background: color.dot,
                    marginTop: 4,
                    flexShrink: 0,
                  }}
                />

                {/* Content */}
                <div style={{ minWidth: 0 }}>
                  <div style={{ display: "flex", alignItems: "center", gap: 8, flexWrap: "wrap", marginBottom: 3 }}>
                    {/* Action badge */}
                    <span
                      style={{
                        fontSize: 11, fontWeight: 700,
                        padding: "2px 7px", borderRadius: 4,
                        background: color.badge,
                        color: color.text,
                        border: `1px solid ${color.dot}22`,
                        letterSpacing: "0.02em",
                      }}
                    >
                      {actionLabel(t, log.action)}
                    </span>

                    {/* Impersonated badge */}
                    {log.isImpersonated && (
                      <span
                        style={{
                          fontSize: 10, fontWeight: 600,
                          padding: "2px 6px", borderRadius: 4,
                          background: "#2D1B69",
                          color: "#C4B5FD",
                          border: "1px solid #7C3AED44",
                        }}
                      >
                        👁 {t("impersonatedBadge")}
                      </span>
                    )}

                    {/* Tenant name */}
                    {tenantName && (
                      <span style={{ color: "#4B5563", fontSize: 11 }}>
                        {tenantName}
                      </span>
                    )}
                  </div>

                  {/* Meta */}
                  {log.meta && (
                    <div style={{ color: "#6B7280", fontSize: 12, marginTop: 1, wordBreak: "break-word" }}>
                      {log.meta}
                    </div>
                  )}

                  {/* IP */}
                  {log.ipAddress && (
                    <div style={{ color: "#374151", fontSize: 11, marginTop: 2 }}>
                      IP: {log.ipAddress}
                    </div>
                  )}
                </div>

                {/* Timestamp */}
                <div style={{ color: "#374151", fontSize: 11, textAlign: "right", whiteSpace: "nowrap", marginTop: 3 }}>
                  {formatDate(log.createdAt, intlLocale)}
                </div>
              </div>
            );
          })}
        </div>
      )}

      {/* Pagination */}
      {totalPages > 1 && (
        <Pagination page={page} totalPages={totalPages} totalCount={data?.total ?? 0} onPageChange={setPage} />
      )}
    </div>
  );
}
