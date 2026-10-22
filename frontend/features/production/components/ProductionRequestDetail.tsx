"use client";

import { useState } from "react";
import Link from "next/link";
import { useTranslations, useLocale } from "next-intl";
import { Modal } from "@/components/ui/Modal";
import { Btn } from "@/components/ui/Btn";
import { ReasonModal } from "@/components/ui/ReasonModal";
import { Table, type TableColumn } from "@/components/ui/Table";
import {
  useProductionRequest,
  useSubmitProductionRequest,
  useCancelProductionRequest,
  useReviewProductionRequest,
  useRejectProductionRequest,
  useShipProductionRequest,
} from "../hooks/useProductionRequests";
import { ProductionRequestForm } from "./ProductionRequestForm";
import type { ProductionRequestLineDto } from "../types";
import {
  RequestStatusBadge,
  RequestSourceBadge,
  formatDateOnly,
  errMessage,
  reqInputStyle,
  reqErrorStyle,
} from "./ProductionRequestStatusBadge";

interface Props {
  id: string;
  /** Tenant has the production module: review / reject / ship actions are available. */
  canProduce: boolean;
  onClose: () => void;
}

export function ProductionRequestDetail({ id, canProduce, onClose }: Props) {
  const t = useTranslations("Dashboard.production.requests.detail");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";
  const { data: req, isLoading, isError } = useProductionRequest(id);
  const submit = useSubmitProductionRequest();
  const cancel = useCancelProductionRequest();
  const review = useReviewProductionRequest();
  const reject = useRejectProductionRequest();
  const ship = useShipProductionRequest();

  const [editing, setEditing] = useState(false);
  const [reviewing, setReviewing] = useState(false);
  const [rejecting, setRejecting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  if (isLoading || isError || !req) {
    return (
      <Modal title={t("title")} onClose={onClose}>
        <div style={{ color: isError ? "#F87171" : "#6B7280", fontSize: 13 }}>
          {isError ? t("loadError") : t("loading")}
        </div>
      </Modal>
    );
  }

  if (editing) {
    return <ProductionRequestForm draft={req} onClose={() => setEditing(false)} />;
  }

  const pending =
    submit.isPending || cancel.isPending || review.isPending || reject.isPending || ship.isPending;
  const act = { onError: (e: unknown) => setError(errMessage(e, t("actionError"))) };

  const isAuto = req.source === "auto_buffer" || req.source === "auto_promo";
  const showAutoReason = req.lines.some((l) => !!l.autoReason);
  const showAccepted = req.lines.some((l) => l.acceptedQty !== null);
  const columns: TableColumn<ProductionRequestLineDto>[] = [
    { key: "item", header: t("colItem"), cellStyle: { color: "#E8EDF5" }, render: (l) => l.itemName },
    { key: "req", header: t("colRequested"), render: (l) => `${l.requestedQty} ${l.unit}` },
    ...(showAutoReason
      ? [
          {
            key: "autoReason",
            header: t("colAutoReason"),
            cellStyle: { color: "#C4B5FD", fontSize: 12 },
            render: (l: ProductionRequestLineDto) => l.autoReason ?? "—",
          },
        ]
      : []),
    ...(showAccepted
      ? [
          {
            key: "acc",
            header: t("colAccepted"),
            render: (l: ProductionRequestLineDto) =>
              l.acceptedQty === null ? "—" : `${l.acceptedQty} ${l.unit}`,
          },
          {
            key: "reason",
            header: t("colReason"),
            cellStyle: { color: "#9CA3AF", fontSize: 12 },
            render: (l: ProductionRequestLineDto) => l.reason ?? "—",
          },
        ]
      : []),
  ];

  return (
    <>
      <Modal title={t("title")} onClose={onClose} width={680}>
        <div style={{ display: "flex", flexDirection: "column", gap: 14 }}>
          <div style={{ display: "flex", gap: 12, alignItems: "center", flexWrap: "wrap" }}>
            <RequestStatusBadge status={req.status} />
            <RequestSourceBadge source={req.source} />
            <span style={{ color: "#9CA3AF", fontSize: 13 }}>
              {req.storeLocationName} → {req.productionLocationName}
            </span>
            <span style={{ color: "#9CA3AF", fontSize: 13 }}>
              {t("neededBy")}: {formatDateOnly(req.neededBy, intlLocale)}
            </span>
          </div>

          {isAuto && req.status === "draft" && (
            <div style={{ color: "#C4B5FD", fontSize: 12 }}>{t("autoDraftHint")}</div>
          )}
          {req.notes && <div style={{ color: "#9CA3AF", fontSize: 13 }}>{req.notes}</div>}
          {req.reviewNote && (
            <div style={{ color: "#FBBF24", fontSize: 13 }}>
              {t("reviewNote")}: {req.reviewNote}
            </div>
          )}

          <Table columns={columns} rows={req.lines} rowKey={(l) => l.id} />

          {req.shipmentTransferId && (
            <div style={{ fontSize: 13, color: "#9CA3AF" }}>
              {t("shipment")}:{" "}
              <Link href="/transfers" style={{ color: "#60A5FA" }}>
                {t("openTransfers")}
              </Link>
              {req.shipmentStatus ? ` (${req.shipmentStatus})` : ""}
            </div>
          )}

          {error && <div style={reqErrorStyle}>{error}</div>}

          <div style={{ display: "flex", justifyContent: "flex-end", gap: 10, flexWrap: "wrap" }}>
            {req.status === "draft" && (
              <>
                <Btn variant="ghost" onClick={() => setEditing(true)} disabled={pending}>
                  {t("edit")}
                </Btn>
                <Btn
                  variant="success"
                  disabled={pending}
                  onClick={() => {
                    setError(null);
                    submit.mutate(id, act);
                  }}
                >
                  {t("submit")}
                </Btn>
              </>
            )}
            {(req.status === "draft" || req.status === "submitted") && (
              <Btn
                variant="danger"
                disabled={pending}
                onClick={() => {
                  if (!confirm(t("cancelConfirm"))) return;
                  setError(null);
                  cancel.mutate(id, act);
                }}
              >
                {t("cancel")}
              </Btn>
            )}
            {canProduce && req.status === "submitted" && (
              <>
                <Btn variant="danger" disabled={pending} onClick={() => setRejecting(true)}>
                  {t("reject")}
                </Btn>
                <Btn variant="primary" disabled={pending} onClick={() => setReviewing(true)}>
                  {t("review")}
                </Btn>
              </>
            )}
            {canProduce &&
              (req.status === "accepted" || req.status === "partial") &&
              !req.shippedAt && (
                <Btn
                  variant="success"
                  disabled={pending}
                  onClick={() => {
                    if (!confirm(t("shipConfirm"))) return;
                    setError(null);
                    ship.mutate(id, act);
                  }}
                >
                  {ship.isPending ? t("shipping") : t("ship")}
                </Btn>
              )}
          </div>
        </div>
      </Modal>

      {reviewing && (
        <ReviewDialog
          lines={req.lines}
          pending={review.isPending}
          onClose={() => setReviewing(false)}
          onSave={(body, onError) =>
            review.mutate({ id, body }, { onSuccess: () => setReviewing(false), onError })
          }
        />
      )}

      {rejecting && (
        <ReasonModal
          title={t("rejectTitle")}
          required
          pending={reject.isPending}
          onClose={() => setRejecting(false)}
          onConfirm={(reason) =>
            reject.mutate(
              { id, reason },
              { onSuccess: () => setRejecting(false), onError: act.onError }
            )
          }
        />
      )}
    </>
  );
}

// ── Review dialog: accept as-is / change qty with reason ───────────────────────

function ReviewDialog({
  lines,
  pending,
  onClose,
  onSave,
}: {
  lines: ProductionRequestLineDto[];
  pending: boolean;
  onClose: () => void;
  onSave: (
    body: { lines: { lineId: string; acceptedQty: number; reason?: string }[]; note?: string },
    onError: (e: unknown) => void
  ) => void;
}) {
  const t = useTranslations("Dashboard.production.requests.review");
  const [rows, setRows] = useState(
    lines.map((l) => ({ ...l, qty: String(l.acceptedQty ?? l.requestedQty), why: l.reason ?? "" }))
  );
  const [note, setNote] = useState("");
  const [error, setError] = useState<string | null>(null);

  function save() {
    setError(null);
    const out: { lineId: string; acceptedQty: number; reason?: string }[] = [];
    for (const r of rows) {
      const q = parseFloat(r.qty);
      if (isNaN(q) || q < 0) return setError(t("errorQty"));
      if (q < r.requestedQty && !r.why.trim()) return setError(t("errorReason", { item: r.itemName }));
      out.push({ lineId: r.id, acceptedQty: q, reason: r.why.trim() || undefined });
    }
    onSave({ lines: out, note: note.trim() || undefined }, (e) =>
      setError(errMessage(e, t("errorSave")))
    );
  }

  return (
    <Modal title={t("title")} onClose={onClose} width={700}>
      <div style={{ display: "flex", flexDirection: "column", gap: 10 }}>
        {rows.map((r, i) => {
          const short = parseFloat(r.qty) < r.requestedQty;
          return (
            <div key={r.id} style={{ display: "flex", gap: 8, alignItems: "center", flexWrap: "wrap" }}>
              <span style={{ flex: "1 1 160px", color: "#E8EDF5", fontSize: 13 }}>{r.itemName}</span>
              <span style={{ color: "#6B7280", fontSize: 12 }}>
                {t("requested")}: {r.requestedQty} {r.unit}
              </span>
              <input
                type="number"
                min={0}
                step="any"
                value={r.qty}
                onChange={(e) =>
                  setRows((p) => p.map((x, j) => (j === i ? { ...x, qty: e.target.value } : x)))
                }
                style={{ ...reqInputStyle, width: 100 }}
              />
              {short && (
                <input
                  value={r.why}
                  onChange={(e) =>
                    setRows((p) => p.map((x, j) => (j === i ? { ...x, why: e.target.value } : x)))
                  }
                  placeholder={t("reasonPlaceholder")}
                  style={{ ...reqInputStyle, flex: "1 1 180px", width: "auto" }}
                />
              )}
            </div>
          );
        })}
        <textarea
          value={note}
          onChange={(e) => setNote(e.target.value)}
          rows={2}
          placeholder={t("notePlaceholder")}
          style={{ ...reqInputStyle, resize: "vertical" }}
        />
        {error && <div style={reqErrorStyle}>{error}</div>}
        <div style={{ display: "flex", justifyContent: "flex-end", gap: 10 }}>
          <Btn variant="ghost" onClick={onClose} disabled={pending}>{t("cancel")}</Btn>
          <Btn variant="success" onClick={save} disabled={pending}>{t("save")}</Btn>
        </div>
      </div>
    </Modal>
  );
}
