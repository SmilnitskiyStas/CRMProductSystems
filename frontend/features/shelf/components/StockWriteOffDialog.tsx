"use client";

import { useState } from "react";
import { useTranslations } from "next-intl";
import { Modal } from "@/components/ui/Modal";
import { Btn } from "@/components/ui/Btn";
import { CreateWriteOffForm } from "@/features/write-offs/components/CreateWriteOffForm";
import { useApproveWriteOff } from "@/features/write-offs/hooks/useWriteOffs";
import type { WriteOffDto } from "@/features/write-offs/types";
import type { ProductStockDto } from "../types";

interface Props {
  batches: ProductStockDto[];
  canApprove: boolean;
  onClose: () => void;
  onCompleted: () => void;
}

export function StockWriteOffDialog({ batches, canApprove, onClose, onCompleted }: Props) {
  const t = useTranslations("Dashboard.stock.writeOff");
  const tReason = useTranslations("Dashboard.writeOffs.reason");
  const [created, setCreated] = useState<WriteOffDto | null>(null);
  const [error, setError] = useState<string | null>(null);
  const approve = useApproveWriteOff();
  async function handleApprove() {
    if (!created || approve.isPending) return;
    setError(null);
    try {
      await approve.mutateAsync({ id: created.id });
      onCompleted();
    } catch (err) {
      setError(err instanceof Error ? err.message : t("approveFailed"));
    }
  }
  return (
    <Modal title={t("title")} width={760} onClose={() => { if (!approve.isPending) onClose(); }}>
      {!created ? (
        <>
          <p style={{ color: "#9CA3AF", fontSize: 13 }}>{t("batchHint")}</p>
          <CreateWriteOffForm initialBatches={batches} onSuccess={setCreated} onCancel={onClose} />
        </>
      ) : (
        <div style={{ display: "flex", flexDirection: "column", gap: 16 }}>
          <p role="status" style={{ color: "#9CA3AF", margin: 0 }}>{canApprove ? t("readyToApprove") : t("pendingApproval")}</p>
          <p style={{ color: "#9CA3AF", margin: 0 }}>{created.storeName || batches[0]?.storeName} · {t("itemCount", { count: created.items.length })}</p>
          {created.reason && <p style={{ color: "#9CA3AF", margin: 0 }}>{t("reason")}: {tReason(created.reason)}</p>}
          {created.notes && <p style={{ color: "#9CA3AF", margin: 0, overflowWrap: "anywhere" }}>{created.notes}</p>}
          {error && <p role="alert" style={{ color: "#F87171", margin: 0 }}>{error}</p>}
          <div style={{ display: "flex", gap: 12, justifyContent: "flex-end" }}>
            <Btn variant="ghost" disabled={approve.isPending} onClick={onClose}>{t("close")}</Btn>
            {canApprove && <Btn variant="danger" disabled={approve.isPending} onClick={handleApprove}>{approve.isPending ? t("approving") : t("approve")}</Btn>}
          </div>
        </div>
      )}
    </Modal>
  );
}
