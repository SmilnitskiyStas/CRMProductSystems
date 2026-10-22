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

const STATUSES: ProductionRequestStatus[] = [
  "draft",
  "submitted",
  "accepted",
  "partial",
  "rejected",
  "fulfilled",
  "cancelled",
];

// Requests that can feed a production order.
const ORDERABLE = new Set<string>(["accepted", "partial"]);

export function ProductionRequestsPage() {
  const t = useTranslations("Dashboard.production.requests");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";
  const { data: modulesData } = useModules();
  // Review / ship / summary / order creation are production-module endpoints (403 otherwise).
  const canProduce = !!modulesData?.modules.includes("production");

  const [tab, setTab] = useState<"requests" | "summary">("requests");
  const [status, setStatus] = useState("");
  const [storeId, setStoreId] = useState("");
  const [formOpen, setFormOpen] = useState(false);
  const [openId, setOpenId] = useState<string | null>(null);
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [plannedDate, setPlannedDate] = useState("");
  const [feedback, setFeedback] = useState<{ ok: boolean; text: string } | null>(null);

  const { data: locations = [] } = useLocations();
  const { data: rows = [], isLoading, isError } = useProductionRequests({
    status: status || undefined,
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
      header: t("colStore"),
      align: "left",
      cellStyle: { color: "#E8EDF5", fontWeight: 500 },
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
        padding: "8px 16px",
        background: "transparent",
        border: "none",
        borderBottom: tab === key ? "2px solid #3B82F6" : "2px solid transparent",
        color: tab === key ? "#E8EDF5" : "#6B7280",
        fontSize: 13,
        fontWeight: 600,
        cursor: "pointer",
      }}
    >
      {label}
    </button>
  );

  return (
    <div>
      <div
        style={{
          display: "flex",
          alignItems: "center",
          justifyContent: "space-between",
          marginBottom: 16,
          gap: 16,
          flexWrap: "wrap",
        }}
      >
        <h1 style={{ color: "#E8EDF5", fontSize: 20, fontWeight: 700, margin: 0 }}>{t("title")}</h1>
        <div style={{ display: "flex", gap: 10, flexWrap: "wrap" }}>
          <Btn variant="ghost" onClick={handleAutoGenerate} disabled={autoGen.isPending}>
            {autoGen.isPending ? t("calcRunning") : t("calcNeeds")}
          </Btn>
          <Btn icon={<Plus size={15} />} onClick={() => setFormOpen(true)}>
            {t("newRequest")}
          </Btn>
        </div>
      </div>

      {canProduce && (
        <div style={{ display: "flex", borderBottom: "1px solid #1F2937", marginBottom: 16 }}>
          {tabBtn("requests", t("tabRequests"))}
          {tabBtn("summary", t("tabSummary"))}
        </div>
      )}

      {tab === "summary" && canProduce ? (
        <ProductionRequestSummary />
      ) : (
        <>
          <div style={{ display: "flex", gap: 10, marginBottom: 14, flexWrap: "wrap", alignItems: "center" }}>
            <select
              value={status}
              onChange={(e) => setStatus(e.target.value)}
              style={{ ...reqInputStyle, width: "auto" }}
            >
              <option value="">{t("filterAllStatuses")}</option>
              {STATUSES.map((s) => (
                <option key={s} value={s}>{t(`status.${s}`)}</option>
              ))}
            </select>
            <select
              value={storeId}
              onChange={(e) => setStoreId(e.target.value)}
              style={{ ...reqInputStyle, width: "auto" }}
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
                  style={{ ...reqInputStyle, width: "auto" }}
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
                marginBottom: 14,
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
              emptyMessage={t("empty")}
            />
          )}
        </>
      )}

      {formOpen && <ProductionRequestForm onClose={() => setFormOpen(false)} />}
      {openId && (
        <ProductionRequestDetail id={openId} canProduce={canProduce} onClose={() => setOpenId(null)} />
      )}
    </div>
  );
}
