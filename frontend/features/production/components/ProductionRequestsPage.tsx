"use client";

import { useState } from "react";
import { Plus } from "lucide-react";
import { useTranslations, useLocale } from "next-intl";
import { Btn } from "@/components/ui/Btn";
import { PageContainer } from "@/components/ui/PageContainer";
import { PageHeader } from "@/components/ui/PageHeader";
import { filterInputStyle } from "@/components/ui/fieldStyles";
import { Table, type TableColumn } from "@/components/ui/Table";
import { useModules } from "@/features/modules/hooks/useModules";
import { useLocations } from "@/features/locations/hooks/useLocations";
import { useProductionRequests, useAutoGenerateRequests } from "../hooks/useProductionRequests";
import type { ProductionRequestListItemDto, ProductionRequestStatus } from "../types";
import { ProductionRequestForm } from "./ProductionRequestForm";
import { ProductionRequestDetail } from "./ProductionRequestDetail";
import {
  RequestStatusBadge,
  RequestSourceBadge,
  RequestStageBadge,
  formatDateOnly,
  errMessage,
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

  const [status, setStatus] = useState(isProd ? PRODUCTION_DEFAULT_STATUS : "");
  const [storeId, setStoreId] = useState("");
  const [formOpen, setFormOpen] = useState(false);
  const [openId, setOpenId] = useState<string | null>(null);
  const [feedback, setFeedback] = useState<{ ok: boolean; text: string } | null>(null);

  const { data: locations = [] } = useLocations();
  const { data: rows = [], isLoading, isError } = useProductionRequests({
    // "All" for production still excludes store drafts / cancelled.
    status: status || (isProd ? PRODUCTION_STATUSES.join(",") : undefined),
    storeLocationId: storeId || undefined,
  });
  const autoGen = useAutoGenerateRequests();

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
          <RequestStageBadge stage={r.productionStage} />
        </span>
      ),
    },
  ];

  return (
    <PageContainer>
      <PageHeader
        title={isProd ? t("title") : t("storeTitle")}
        subtitle={isProd ? t("subtitle") : t("storeSubtitle")}
        actions={
          !isProd ? (
            <>
              <Btn variant="ghost" onClick={handleAutoGenerate} disabled={autoGen.isPending}>
                {autoGen.isPending ? t("calcRunning") : t("calcNeeds")}
              </Btn>
              <Btn icon={<Plus size={15} />} onClick={() => setFormOpen(true)}>
                {t("newRequest")}
              </Btn>
            </>
          ) : undefined
        }
      />

      <>
          <div style={{ display: "flex", gap: 10, flexWrap: "wrap", alignItems: "center" }}>
            <select
              value={status}
              onChange={(e) => setStatus(e.target.value)}
              style={{ ...filterInputStyle, cursor: "pointer" }}
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
              style={{ ...filterInputStyle, cursor: "pointer" }}
            >
              <option value="">{t("filterAllStores")}</option>
              {locations
                .filter((l) => l.locationType !== "production")
                .map((l) => (
                  <option key={l.id} value={l.id}>{l.name}</option>
                ))}
            </select>
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

      {!isProd && formOpen && <ProductionRequestForm onClose={() => setFormOpen(false)} />}
      {openId && (
        <ProductionRequestDetail id={openId} mode={mode} canProduce={canProduce} onClose={() => setOpenId(null)} />
      )}
    </PageContainer>
  );
}
