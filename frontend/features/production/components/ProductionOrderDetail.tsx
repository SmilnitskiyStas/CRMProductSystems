"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { useTranslations, useLocale } from "next-intl";
import {
  useProductionOrder,
  useStartProductionOrder,
  useCompleteProductionOrder,
  useCancelProductionOrder,
  useShipOrderRequests,
} from "../hooks/useProduction";
import { OrderStatusBadge } from "./ProductionOrderTable";
import { useShipProductionRequest } from "../hooks/useProductionRequests";
import { RequestStatusBadge } from "./ProductionRequestStatusBadge";
import Link from "next/link";
import { ApiError } from "@/lib/api";
import { Table, type TableColumn } from "@/components/ui/Table";
import { Btn } from "@/components/ui/Btn";
import { BackLink } from "@/components/ui/BackLink";
import { PageContainer } from "@/components/ui/PageContainer";
import { PageHeader } from "@/components/ui/PageHeader";
import type {
  ProductionConsumptionDto,
  ProductionOrderRequestRefDto,
  ShipRequestResultDto,
} from "../types";
import { formatDateOnly } from "./ProductionRequestStatusBadge";

interface Props {
  id: string;
}

interface CompleteSuccess {
  outputQty: number;
  outputItemName: string;
  batchNumber: string;
}

// Shape of the 422 InsufficientStock error from the API
interface InsufficientStockError {
  itemName: string;
  required: number;
  available: number;
}

export function ProductionOrderDetail({ id }: Props) {
  const t = useTranslations("Dashboard.production.orderDetail");
  const tCommon = useTranslations("Common");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";
  const router = useRouter();
  const { data: order, isLoading, isError } = useProductionOrder(id);
  const startOrder = useStartProductionOrder(id);
  const completeOrder = useCompleteProductionOrder(id);
  const cancelOrder = useCancelProductionOrder(id);
  const shipAll = useShipOrderRequests(id);
  const shipOne = useShipProductionRequest();

  const [successInfo, setSuccessInfo] = useState<CompleteSuccess | null>(null);
  const [insufficientStock, setInsufficientStock] =
    useState<InsufficientStockError | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);
  const [shipResults, setShipResults] = useState<ShipRequestResultDto[] | null>(null);

  if (isLoading) {
    return (
      <PageContainer style={{ color: "#6B7280", fontSize: 13 }}>
        {tCommon("loading")}
      </PageContainer>
    );
  }
  if (isError || !order) {
    return (
      <PageContainer style={{ color: "#F87171", fontSize: 13 }}>
        {t("loadError")}
      </PageContainer>
    );
  }

  const status = order.status;
  const isReadOnly = status === "done" || status === "cancelled";

  function clearFeedback() {
    setSuccessInfo(null);
    setInsufficientStock(null);
    setActionError(null);
  }

  function handleStart() {
    clearFeedback();
    startOrder.mutate();
  }

  function handleComplete() {
    clearFeedback();
    completeOrder.mutate(undefined, {
      onSuccess: (result) => {
        setSuccessInfo({
          outputQty: result.outputQty,
          outputItemName: result.outputItemName,
          // batch number not in result — use stock batch id short form
          batchNumber: result.newStockBatchId.substring(0, 8).toUpperCase(),
        });
      },
      onError: (err) => {
        if (err instanceof ApiError && err.status === 422) {
          // Try to parse structured error body: "Недостатньо {itemName}: потрібно {required}, є {available}"
          const msg = err.message;
          const match = msg.match(
            /Недостатньо (.+?):\s*потрібно ([\d.]+),\s*є ([\d.]+)/i
          );
          if (match) {
            setInsufficientStock({
              itemName: match[1],
              required: parseFloat(match[2]),
              available: parseFloat(match[3]),
            });
          } else {
            // Fallback: try to extract item name from simpler format
            setInsufficientStock({
              itemName: msg,
              required: 0,
              available: 0,
            });
          }
        } else {
          setActionError(err instanceof Error ? err.message : t("errorCompleteFallback"));
        }
      },
    });
  }

  function handleCancel() {
    if (!confirm(t("cancelConfirm"))) return;
    clearFeedback();
    cancelOrder.mutate(undefined, {
      onError: (err) => {
        setActionError(err instanceof Error ? err.message : t("errorCancelFallback"));
      },
    });
  }

  const anyPending =
    startOrder.isPending || completeOrder.isPending || cancelOrder.isPending;
  const shipPending = shipAll.isPending || shipOne.isPending;
  const isDone = status === "done";
  const unshipped = (order.requests ?? []).filter((r) => !r.shipmentTransferId);

  function handleShipAll() {
    if (!confirm(t("shipAllConfirm"))) return;
    clearFeedback();
    setShipResults(null);
    shipAll.mutate(undefined, {
      onSuccess: (res) => setShipResults(res),
      onError: (err) => setActionError(err instanceof Error ? err.message : t("shipError")),
    });
  }

  function handleShipOne(requestId: string) {
    clearFeedback();
    setShipResults(null);
    shipOne.mutate(requestId, {
      onError: (err) => setActionError(err instanceof Error ? err.message : t("shipError")),
    });
  }

  const consumptionColumns: TableColumn<ProductionConsumptionDto>[] = [
    {
      key: "item",
      header: t("consumptionsHeaderItem"),
      cellStyle: { color: "#E8EDF5" },
      render: (c) => c.itemName,
    },
    {
      key: "qty",
      header: t("consumptionsHeaderQty"),
      render: (c) => c.qtyConsumed,
    },
    {
      key: "batch",
      header: t("consumptionsHeaderBatch"),
      cellStyle: { fontFamily: "monospace", fontSize: 12, color: "#6B7280" },
      render: (c) => c.batchNumber ?? "—",
    },
    {
      key: "consumedAt",
      header: t("consumptionsHeaderConsumedAt"),
      cellStyle: { color: "#6B7280", fontSize: 12 },
      render: (c) => formatDateTime(c.consumedAt, intlLocale),
    },
  ];

  const requestColumns: TableColumn<ProductionOrderRequestRefDto>[] = [
    {
      key: "store",
      header: t("requestsHeaderStore"),
      cellStyle: { color: "#E8EDF5" },
      render: (r) => r.storeLocationName,
    },
    { key: "qty", header: t("requestsHeaderQty"), render: (r) => r.qty },
    {
      key: "reqStatus",
      header: t("requestsHeaderStatus"),
      render: (r) => (r.requestStatus ? <RequestStatusBadge status={r.requestStatus} /> : "—"),
    },
    {
      key: "shipment",
      header: t("requestsHeaderShipment"),
      render: (r) =>
        r.shipmentTransferId ? (
          <Link
            href="/transfers"
            onClick={(e) => e.stopPropagation()}
            style={{ color: "#34D399", fontSize: 12 }}
          >
            {t("shipped")}
          </Link>
        ) : (
          <span style={{ color: "#6B7280", fontSize: 12 }}>{t("notShipped")}</span>
        ),
    },
    ...(isDone
      ? [
          {
            key: "shipAction",
            header: "",

            render: (r: ProductionOrderRequestRefDto) =>
              r.shipmentTransferId ? null : (
                <span onClick={(e) => e.stopPropagation()}>
                  <Btn variant="success" disabled={shipPending} onClick={() => handleShipOne(r.requestId)}>
                    {t("shipOne")}
                  </Btn>
                </span>
              ),
          } as TableColumn<ProductionOrderRequestRefDto>,
        ]
      : []),
  ];

  return (
    <PageContainer style={{ maxWidth: 900 }}>
      <BackLink onClick={() => router.push("/production/orders")}>{t("backToOrders")}</BackLink>

      <PageHeader
        title={
          <span style={{ display: "inline-flex", alignItems: "center", gap: 12 }}>
            {order.recipeName}
            <OrderStatusBadge status={order.status} />
          </span>
        }
        actions={
          !isReadOnly ? (
            <>
              {status === "planned" && (
                <Btn onClick={handleStart} disabled={anyPending}>
                  {startOrder.isPending ? t("starting") : t("start")}
                </Btn>
              )}

              {status === "in_progress" && (
                <Btn variant="success" onClick={handleComplete} disabled={anyPending}>
                  {completeOrder.isPending ? t("completing") : t("complete")}
                </Btn>
              )}

              {(status === "planned" || status === "in_progress") && (
                <Btn variant="danger" onClick={handleCancel} disabled={anyPending}>
                  {cancelOrder.isPending ? t("cancelling") : t("cancel")}
                </Btn>
              )}
            </>
          ) : undefined
        }
      />

      <div>
        <div style={{ color: "#9CA3AF", fontSize: 13, display: "flex", gap: 20, flexWrap: "wrap" }}>
          <span>📍 {order.locationName}</span>
          <span>{"📦 "}{t("plannedOutputLabel")} {order.plannedQty}</span>
          <span>{"📅 "}{formatDateTime(order.createdAt, intlLocale)}</span>
          {order.neededBy && (
            <span>{"⏰ "}{t("neededByLabel")} {formatDateOnly(order.neededBy, intlLocale)}</span>
          )}
          {order.plannedDate && (
            <span>{"🗓 "}{t("plannedDateLabel")} {formatDateOnly(order.plannedDate, intlLocale)}</span>
          )}
          {order.startedAt && (
            <span>{"▶ "}{t("startedLabel")} {formatDateTime(order.startedAt, intlLocale)}</span>
          )}
          {order.completedAt && (
            <span>{"✅ "}{t("completedLabel")} {formatDateTime(order.completedAt, intlLocale)}</span>
          )}
        </div>
        {order.notes && (
          <div
            style={{
              marginTop: 10,
              padding: "8px 12px",
              background: "#0D1117",
              border: "1px solid #1F2937",
              borderRadius: 6,
              color: "#9CA3AF",
              fontSize: 13,
            }}
          >
            {order.notes}
          </div>
        )}
      </div>

        {/* Success toast */}
        {successInfo && (
          <div
            style={{
              padding: "14px 18px",
              background: "#064E3B",
              border: "1px solid #065F46",
              borderRadius: 10,
              color: "#34D399",
              fontSize: 13,
            }}
          >
            <div style={{ fontWeight: 700, marginBottom: 4 }}>
              {t("successHeading")}
            </div>
            <div>
              {t("successBody", { qty: successInfo.outputQty, name: successInfo.outputItemName })}
            </div>
            <div style={{ color: "#6EE7B7", fontSize: 12, marginTop: 4 }}>
              {t("successBatch", { batch: successInfo.batchNumber })}
            </div>
          </div>
        )}

        {/* 422 Insufficient stock error */}
        {insufficientStock && (
          <div
            style={{
              padding: "14px 18px",
              background: "#1F1010",
              border: "1px solid #7F1D1D",
              borderRadius: 10,
              color: "#F87171",
              fontSize: 13,
            }}
          >
            <div style={{ fontWeight: 700, marginBottom: 4 }}>
              {t("insufficientHeading")}
            </div>
            {insufficientStock.required > 0 ? (
              <div>
                {t("insufficientDetail", {
                  item: insufficientStock.itemName,
                  required: insufficientStock.required,
                  available: insufficientStock.available,
                })}
              </div>
            ) : (
              <div>{insufficientStock.itemName}</div>
            )}
          </div>
        )}

        {/* General action error */}
        {actionError && (
          <div
            style={{
              padding: "14px 18px",
              background: "#1F1010",
              border: "1px solid #7F1D1D",
              borderRadius: 10,
              color: "#F87171",
              fontSize: 13,
            }}
          >
            <strong>{t("actionErrorPrefix")}</strong> {actionError}
          </div>
        )}

        {/* Store requests feeding this order */}
        {order.requests && order.requests.length > 0 && (
          <div
            style={{
              background: "#111827",
              border: "1px solid #1F2937",
              borderRadius: 10,
              padding: 16,
              display: "flex",
              flexDirection: "column",
              gap: 12,
            }}
          >
            <h2 style={{ color: "#E8EDF5", fontSize: 14, fontWeight: 600, margin: 0 }}>
              {t("requestsTitle")}
            </h2>
            <Table
              columns={requestColumns}
              rows={order.requests}
              rowKey={(r) => r.requestLineId}
              onRowClick={() => router.push("/production/requests")}
            />
            {isDone && (
              <div style={{ display: "flex", flexDirection: "column", gap: 10 }}>
                <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", gap: 12, flexWrap: "wrap" }}>
                  <h3 style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 600, margin: 0 }}>
                    {t("shipmentsTitle")}
                  </h3>
                  {unshipped.length > 0 ? (
                    <Btn variant="success" onClick={handleShipAll} disabled={shipPending}>
                      {shipAll.isPending ? t("shippingAll") : t("shipAll")}
                    </Btn>
                  ) : (
                    <span style={{ color: "#34D399", fontSize: 12 }}>{t("allShipped")}</span>
                  )}
                </div>
                {shipResults && (
                  <div style={{ display: "flex", flexDirection: "column", gap: 6 }}>
                    {shipResults.map((r) => (
                      <div
                        key={r.requestId}
                        style={{
                          fontSize: 12,
                          padding: "6px 10px",
                          borderRadius: 6,
                          background: r.ok ? "#064E3B" : "#1F1010",
                          color: r.ok ? "#34D399" : "#F87171",
                        }}
                      >
                        {r.storeLocationName}: {r.ok ? t("shipOk") : r.error || t("shipError")}
                      </div>
                    ))}
                  </div>
                )}
              </div>
            )}
          </div>
        )}

        {/* Consumptions table (visible after completion) */}
        {order.consumptions.length > 0 && (
          <div
            style={{
              background: "#111827",
              border: "1px solid #1F2937",
              borderRadius: 10,
              padding: 16,
              display: "flex",
              flexDirection: "column",
              gap: 12,
            }}
          >
            <h2 style={{ color: "#E8EDF5", fontSize: 14, fontWeight: 600, margin: 0 }}>
              {t("consumptionsTitle")}
            </h2>
            <Table
              columns={consumptionColumns}
              rows={order.consumptions}
              rowKey={(c) => `${c.itemId}-${c.batchNumber ?? "none"}`}
            />
          </div>
        )}

        {/* Empty state for pending orders */}
        {order.consumptions.length === 0 && !isReadOnly && (
          <div
            style={{
              background: "#111827",
              border: "1px solid #1F2937",
              borderRadius: 10,
              padding: "32px 16px",
              textAlign: "center",
              color: "#4B5563",
              fontSize: 13,
            }}
          >
            {t("consumptionsEmpty")}
          </div>
        )}
    </PageContainer>
  );
}

// ── Helpers ────────────────────────────────────────────────────────────────────

function formatDateTime(iso: string, intlLocale: string) {
  return new Date(iso).toLocaleString(intlLocale, {
    day: "2-digit",
    month: "2-digit",
    year: "numeric",
    hour: "2-digit",
    minute: "2-digit",
  });
}
