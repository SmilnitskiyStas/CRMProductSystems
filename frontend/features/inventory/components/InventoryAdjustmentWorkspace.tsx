"use client";

import { useTranslations, useLocale } from "next-intl";
import { Modal } from "@/components/ui/Modal";
import { Btn } from "@/components/ui/Btn";
import { useInventoryAdjustment } from "../hooks/useInventoryAdjustment";
import type { AdjustmentBatch, AdjustmentIdentity } from "../lib/inventoryAdjustmentSession";

type Props = AdjustmentIdentity & { selected: AdjustmentBatch | null; onClose: () => void };
const field = { background: "#111827", border: "1px solid #374151", borderRadius: 6, color: "#E8EDF5", padding: "9px 12px", width: "100%" };

/** Mount with key tenant:actor so changing identities cannot reuse a previous user's intent or receipt. */
export function InventoryAdjustmentWorkspace({ tenantId, actorId, selected, onClose }: Props) {
  const t = useTranslations("Dashboard.stock.adjustment");
  const locale = useLocale();
  const a = useInventoryAdjustment({ tenantId, actorId }, selected);
  const blocked = !a.loaded || a.storageError || a.context.isFetching || a.context.isError || !a.context.data?.status.canAdjust;
  const close = () => { if (a.close()) onClose(); };
  return <>
    {a.storageError && <p role="alert">{t("storage")} <Btn variant="ghost" onClick={a.load}>{t("reload")}</Btn></p>}
    {a.saved && !a.batch && <div role="status" style={{ color: "#FBBF24", padding: 12, border: "1px solid #854D0E", borderRadius: 8 }}>
      {t("saved", { name: a.saved.batch.productName, store: a.saved.batch.storeName })}{" "}
      <Btn onClick={a.resume}>{t("resume")}</Btn>
    </div>}
    {a.batch && <Modal title={t("title")} onClose={close} width={620}>
      <div role="dialog" aria-modal="true" aria-label={t("title")} style={{ display: "flex", flexDirection: "column", gap: 14, color: "#9CA3AF", fontSize: 13 }}>
        <p style={{ margin: 0, color: "#E8EDF5" }}>{a.batch.productName} · {a.batch.storeName}</p>
        <p style={{ margin: 0 }}>{t("batch", { number: a.batch.batchNumber || "—", date: a.batch.expiryDate })}</p>
        {a.receipt ? <>
          <p role="status" style={{ color: "#4ADE80" }}>{t(a.receipt.replayed ? "replayed" : "completed")}</p>
          <p>{t("receipt", { before: a.receipt.quantityBefore, after: a.receipt.quantityAfter,
            date: new Date(a.receipt.postedAt).toLocaleString(locale === "en" ? "en-US" : "uk-UA") })}</p>
          <p style={{ overflowWrap: "anywhere" }}>{t("document")}: {a.receipt.documentId}</p>
          <p>{t("receiptHint")}</p>
        </> : <>
          {a.storageError ? <p role="alert">{t("storage")}</p> : !a.loaded || a.context.isPending ? <p role="status">{t("loading")}</p> : a.context.isError ?
            <p role="alert">{t("loadFailed")}</p> : !a.context.data?.status.canAdjust &&
            <p role="status">{t(a.context.data?.status.fenced ? "paused" : "unavailable")}</p>}
          {a.saved && <div role="status" style={{ color: "#FBBF24", overflowWrap: "anywhere" }}>
            <p>{t(a.saved.phase === "uncertain" ? "uncertain" : "rejected")}</p>
            <p>{t("request")}: {a.saved.command.requestId}</p>
          </div>}
          <form onSubmit={e => { e.preventDefault(); void a.submit(); }} style={{ display: "flex", flexDirection: "column", gap: 14 }}>
            <label>{t("expected")}<input aria-label={t("expected")} style={field} readOnly
              value={a.saved?.command.expectedQuantity ?? a.context.data?.stock.quantity ?? ""} /></label>
            <label>{t("quantity")}<input aria-label={t("quantity")} style={field} type="text" inputMode="decimal" autoComplete="off"
              readOnly={!!a.saved || a.busy} disabled={blocked} value={a.saved?.command.quantity ?? a.quantity}
              onChange={e => a.setQuantity(e.target.value)} /></label>
            <label>{t("reason")}<textarea aria-label={t("reason")} style={field} rows={3} maxLength={500}
              readOnly={!!a.saved || a.busy} disabled={blocked} value={a.saved?.command.reason ?? a.reason}
              onChange={e => a.setReason(e.target.value)} /></label>
            <p style={{ margin: 0 }}>{t("hint")}</p>
            <div style={{ display: "flex", justifyContent: "flex-end", flexWrap: "wrap", gap: 10 }}>
              {a.saved?.phase !== "uncertain" && <Btn variant="ghost" disabled={!a.loaded || a.busy || a.context.isFetching || a.storageError}
                onClick={() => void a.review()}>{t(a.saved ? "review" : "reload")}</Btn>}
              {a.saved?.phase === "uncertain" && <Btn variant="ghost" disabled={a.busy || a.context.isFetching}
                onClick={() => void a.context.refetch()}>{t("reload")}</Btn>}
              <Btn type="submit" disabled={blocked || a.busy || (!a.saved && (!a.quantity || !a.reason.trim()))}>
                {t(a.busy ? "submitting" : a.saved ? "retry" : "submit")}
              </Btn>
            </div>
          </form>
        </>}
        {a.error && <p role="alert" style={{ color: "#F87171" }}>{t(a.error)}</p>}
        <div style={{ display: "flex", justifyContent: "flex-end" }}><Btn variant="ghost" disabled={a.busy} onClick={close}>{t("close")}</Btn></div>
      </div>
    </Modal>}
  </>;
}
