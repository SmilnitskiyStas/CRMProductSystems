"use client";

import { useState } from "react";
import { Plus } from "lucide-react";
import { useTranslations, useLocale } from "next-intl";
import { Btn } from "@/components/ui/Btn";
import { Table, type TableColumn } from "@/components/ui/Table";
import { useModules } from "@/features/modules/hooks/useModules";
import { useLocations } from "@/features/locations/hooks/useLocations";
import {
  useProductionRequests,
  useCreateOrdersFromRequests,
  useAutoGenerateRequests,
} from "../hooks/useProductionRequests";
import type { ProductionRequestListItemDto, ProductionRequestStatus } from "../types";
import { ProductionRequestForm } from "./ProductionRequestForm";
import { ProductionRequestDetail } from "./ProductionRequestDetail";
import { ProductionRequestSummary } from "./ProductionRequestSummary";
import {
  RequestStatusBadge,
  RequestSourceBadge,
  formatDateOnly,
  errMessage,
  reqInputStyle,
  reqErrorStyle,
} from "./ProductionRequestStatusBadge";

const STORE_STATUSES: ProductionRequestStatus[] = [
  "draft",
  "submitted",
  "accepted",
  "partial",
  "rejected",
  "fulfilled",
  "cancelled",
];
// Production never sees store drafts / cancelled requests.
const PRODUCTION_STATUSES: ProductionRequestStatus[] = [
  "submitted",
  "accepted",
  "partial",
  "rejected",
  "fulfilled",
];
// Default inbox: what still needs production attention.
const PRODUCTION_DEFAULT_STATUS = "submitted,accepted,partial";

// Requests that can feed a production order.
const ORDERABLE = new Set<string>(["accepted", "partial"]);

interface PageProps {
  /** "store": requesting side (create/calc/edit own requests). "production": inbox side. */
  mode: "store" | "production";
}

export function ProductionRequestsPage({ mode }: PageProps) {
  const isProd = mode === "production";
  const STATUSES = isProd ? PRODUCTION_STATUSES : STORE_STATUSES;
  const t = useTranslations("Dashboard.production.requests");
  const tCommon = useTranslations("Common");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";
  const { data: modulesData } = useModules();
  // Review / ship / summary / order creation are production-module endpoints (403 otherwise).
  const canProduce = isProd && !!modulesData?.modules.includes("production");

  const [tab, setTab] = useState<"requests" | "summary">("requests");
  const [status, setStatus] = useState(isProd ? PRODUCTION_DEFAULT_STATUS : "");
  const [storeId, setStoreId] = useState("");
  const [formOpen, setFormOpen] = useState(false);
  const [openId, setOpenId] = useState<string | null>(null);
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [plannedDate, setPlannedDate] = useState("");
  const [feedback, setFeedback] = useState<{ ok: boolean; text: string } | null>(null);

  const { data: locations = [] } = useLocations();
  const { data: rows = [], isLoading, isError } = useProductionRequests({
    // "All" for production still excludes store drafts / cancelled.
    status: status || (isProd ? PRODUCTION_STATUSES.join(",") : undefined),
    storeLocationId: storeId || undefined,
  });
  const createOrders = useCreateOrdersFromRequests();
  const autoGen = useAutoGenerateRequests();

  function toggle(id: string) {
    setSelected((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }

  function handleCreateOrders() {
    setFeedback(null);
    createOrders.mutate(
      { requestIds: [...selected], plannedDate: plannedDate || undefined },
      {
        onSuccess: (res) => {
          setSelected(new Set());
          setFeedback({ ok: true, text: t("ordersCreated", { count: res.length }) });
        },
        // 422 message lists items without an active recipe.
        onError: (e) => setFeedback({ ok: false, text: errMessage(e, t("ordersError")) }),
      }
    );
  }

  function handleAutoGenerate() {
    setFeedback(null);
    autoGen.mutate(storeId || undefined, {
      onSuccess: (res) =>
        setFeedback({
          ok: true,
          text: res.length > 0 ? t("calcDone", { count: res.length }) : t("calcNone"),
        }),
      onError: (e) => setFeedback({ ok: false, text: errMessage(e, t("calcError")) }),
    });
  }

  const columns: TableColumn<ProductionRequestListItemDto>[] = [
    ...(canProduce
      ? [
          {
            key: "select",
            header: "",
            width: 40,
            render: (r: ProductionRequestListItemDto) =>
              ORDERABLE.has(r.status) ? (
                <input
                  type="checkbox"
                  checked={selected.has(r.id)}
                  onChange={() => toggle(r.id)}
                  onClick={(e) => e.stopPropagation()}
                  aria-label={t("select")}
                />
              ) : null,
          } as TableColumn<ProductionRequestListItemDto>,
        ]
      : []),
    {
      key: "store",
      header: isProd ? t("colStoreFrom") : t("colStore"),
      align: "left",
      cellStyle: { color: "#E8EDF5", fontWeight: isProd ? 700 : 500, fontSize: isProd ? 14 : undefined },
      render: (r) => r.storeLocationName,
    },
    { key: "prod", header: t("colProduction"), render: (r) => r.productionLocationName },
    {
      key: "needed",
      header: t("colNeededBy"),
      render: (r) => formatDateOnly(r.neededBy, intlLocale),
    },
    { key: "lines", header: t("colLines"), render: (r) => r.lineCount },
    {
      key: "qty",
      header: t("colQty"),
      render: (r) =>
        r.totalAcceptedQty !== null && r.totalAcceptedQty !== r.totalRequestedQty
          ? `${r.totalAcceptedQty} / ${r.totalRequestedQty}`
          : r.totalRequestedQty,
    },
    {
      key: "status",
      header: t("colStatus"),
      render: (r) => (
        <span style={{ display: "inline-flex", gap: 6, alignItems: "center", flexWrap: "wrap" }}>
          <RequestStatusBadge status={r.status} />
          <RequestSourceBadge source={r.source} />
        </span>
      ),
    },
  ];

  const tabBtn = (key: "requests" | "summary", label: string) => (
    <button
      key={key}
      onClick={() => setTab(key)}
      style={{
        padding: "8px 14px",
        background: "transparent",
        border: "none",
        borderBottom: tab === key ? "2px solid #3B82F6" : "2px solid transparent",
        color: tab === key ? "#60A5FA" : "#6B7280",
        fontSize: 13,
        fontWeight: tab === key ? 600 : 400,
        cursor: "pointer",
        marginBottom: -1,
        transition: "color 0.1s",
      }}
    >
      {label}
    </button>
  );

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 20 }}>
      <div
        style={{
          display: "flex",
          alignItems: "flex-start",
          justifyContent: "space-between",
          gap: 16,
          flexWrap: "wrap",
        }}
      >
        <div>
          <h1 style={{ color: "#E8EDF5", fontSize: 22, fontWeight: 700, margin: 0 }}>
            {isProd ? t("title") : t("storeTitle")}
          </h1>
          <p style={{ color: "#6B7280", fontSize: 13, margin: "6px 0 0" }}>
            {isProd ? t("subtitle") : t("storeSubtitle")}
          </p>
        </div>
        <div style={{ display: "flex", gap: 10, flexWrap: "wrap" }}>
          {!isProd && (
            <>
              <Btn variant="ghost" onClick={handleAutoGenerate} disabled={autoGen.isPending}>
                {autoGen.isPending ? t("calcRunning") : t("calcNeeds")}
              </Btn>
              <Btn icon={<Plus size={15} />} onClick={() => setFormOpen(true)}>
                {t("newRequest")}
              </Btn>
            </>
          )}
        </div>
      </div>

      {canProduce && (
        <div style={{ display: "flex", gap: 4, borderBottom: "1px solid #1F2937" }}>
          {tabBtn("requests", t("tabRequests"))}
          {tabBtn("summary", t("tabSummary"))}
        </div>
      )}

      {tab === "summary" && canProduce ? (
        <ProductionRequestSummary />
      ) : (
        <>
          <div style={{ display: "flex", gap: 10, flexWrap: "wrap", alignItems: "center" }}>
            <select
              value={status}
              onChange={(e) => setStatus(e.target.value)}
              style={{ ...reqInputStyle, width: "auto", padding: "7px 12px", cursor: "pointer" }}
            >
              <option value="">{t("filterAllStatuses")}</option>
              {isProd && (
                <option value={PRODUCTION_DEFAULT_STATUS}>{t("filterActive")}</option>
              )}
              {STATUSES.map((s) => (
                <option key={s} value={s}>{t(`status.${s}`)}</option>
              ))}
            </select>
            <select
              value={storeId}
              onChange={(e) => setStoreId(e.target.value)}
              style={{ ...reqInputStyle, width: "auto", padding: "7px 12px", cursor: "pointer" }}
            >
              <option value="">{t("filterAllStores")}</option>
              {locations
                .filter((l) => l.locationType !== "production")
                .map((l) => (
                  <option key={l.id} value={l.id}>{l.name}</option>
                ))}
            </select>

            {canProduce && selected.size > 0 && (
              <>
                <input
                  type="date"
                  value={plannedDate}
                  onChange={(e) => setPlannedDate(e.target.value)}
                  title={t("plannedDate")}
                  style={{ ...reqInputStyle, width: "auto", padding: "7px 12px" }}
                />
                <Btn variant="success" onClick={handleCreateOrders} disabled={createOrders.isPending}>
                  {t("createOrders", { count: selected.size })}
                </Btn>
              </>
            )}
          </div>

          {feedback && (
            <div
              style={{
                ...(feedback.ok
                  ? { padding: "10px 14px", background: "#064E3B", border: "1px solid #065F46", borderRadius: 8, color: "#34D399", fontSize: 13 }
                  : reqErrorStyle),
              }}
            >
              {feedback.text}
            </div>
          )}

          {isError ? (
            <div style={{ color: "#F87171", fontSize: 14 }}>{t("loadError")}</div>
          ) : (
            <Table
              columns={columns}
              rows={rows}
              rowKey={(r) => r.id}
              isLoading={isLoading}
              onRowClick={(r) => setOpenId(r.id)}
              emptyMessage={isLoading ? tCommon("loading") : isProd ? t("emptyIncoming") : t("storeEmpty")}
            />
          )}
        </>
      )}

      {!isProd && formOpen && <ProductionRequestForm onClose={() => setFormOpen(false)} />}
      {openId && (
        <ProductionRequestDetail id={openId} mode={mode} canProduce={canProduce} onClose={() => setOpenId(null)} />
      )}
    </div>
  );
}
