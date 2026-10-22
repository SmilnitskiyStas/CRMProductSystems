"use client";

import { useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useTranslations, useLocale } from "next-intl";
import { toast } from "sonner";
import { ChevronDown, ChevronRight, RefreshCw } from "lucide-react";
import { Btn } from "@/components/ui/Btn";
import { StatusPill } from "@/components/ui/StatusPill";
import { Table, type TableColumn } from "@/components/ui/Table";
import { useMe } from "@/features/auth/hooks/useAuth";
import { useAutoDrafts, useAutoDraftActions } from "../hooks/useAutoDrafts";
import type { ReceiptDto, ReceiptItemDto } from "@/features/receipts/types";

const RELEASE_OVERRIDE_ROLES = new Set(["enterprise_admin", "network_manager", "provider"]);

const card: React.CSSProperties = {
  background: "#0D1117",
  border: "1px solid #1F2937",
  borderRadius: 10,
  padding: "12px 16px",
};

function QtyInput({
  item, disabled, onSave,
}: { item: ReceiptItemDto; disabled: boolean; onSave: (qty: number) => void }) {
  const [value, setValue] = useState(String(item.quantityOrdered));
  const commit = () => {
    const n = Number(value.replace(",", "."));
    if (!Number.isFinite(n) || n <= 0) { setValue(String(item.quantityOrdered)); return; }
    if (n !== item.quantityOrdered) onSave(n);
  };
  return (
    <input
      type="number" min={0} step="any" value={value} disabled={disabled}
      onChange={(e) => setValue(e.target.value)} onBlur={commit}
      style={{
        width: 90, background: "#111827", border: "1px solid #1F2937", borderRadius: 8,
        color: "#E8EDF5", padding: "4px 10px", fontSize: 13, outline: "none", opacity: disabled ? 0.5 : 1,
      }}
    />
  );
}

function DraftCard({ draft, meId, meRole }: { draft: ReceiptDto; meId?: string; meRole?: string }) {
  const t = useTranslations("Dashboard.orders.page.auto");
  const intl = useLocale() === "en" ? "en-US" : "uk-UA";
  const router = useRouter();
  const [open, setOpen] = useState(false);
  const { take, release, saveQuantity } = useAutoDraftActions();

  const takenByOther = !!draft.takenByUserId && draft.takenByUserId !== meId;
  const takenByMe = !!draft.takenByUserId && draft.takenByUserId === meId;
  const canRelease = !!draft.takenByUserId && (takenByMe || RELEASE_OVERRIDE_ROLES.has(meRole ?? ""));
  const canEdit = takenByMe;
  const total = draft.items.reduce((s, i) => s + i.quantityOrdered * (i.pricePurchase ?? 0), 0);
  const fail = (err: Error) => toast.error(t("toastFailed", { message: err.message }));

  const hint = takenByOther
    ? t("takenByOther", { name: draft.takenByName ?? "—" })
    : !draft.takenByUserId ? t("takeToEdit") : null;

  const lineColumns: TableColumn<ReceiptItemDto>[] = [
    { key: "item", header: t("colItem"), cellStyle: { color: "#E8EDF5", fontWeight: 500 }, render: (i) => i.productName },
    {
      key: "qty",
      header: t("colQty"),
      render: (i) => (
        <QtyInput
          key={`${i.id}-${i.quantityOrdered}`} item={i}
          disabled={!canEdit || saveQuantity.isPending}
          onSave={(qty) => saveQuantity.mutate(
            { receiptId: draft.id, item: { itemId: i.id, quantityOrdered: qty, quantityReceived: i.quantityReceived } },
            { onSuccess: () => toast.success(t("toastSaved")), onError: fail },
          )}
        />
      ),
    },
    {
      key: "price",
      header: t("colPrice"),
      render: (i) => (i.pricePurchase != null ? i.pricePurchase.toLocaleString(intl, { maximumFractionDigits: 2 }) : "—"),
    },
    {
      key: "amount",
      header: t("colAmount"),
      render: (i) =>
        i.pricePurchase != null
          ? (i.quantityOrdered * i.pricePurchase).toLocaleString(intl, { maximumFractionDigits: 0 })
          : "—",
    },
  ];

  return (
    <div style={card}>
      <div style={{ display: "flex", alignItems: "center", gap: 12, flexWrap: "wrap" }}>
        <button onClick={() => setOpen((v) => !v)} aria-label={open ? t("hideLines") : t("showLines")}
          style={{ background: "transparent", border: "none", color: "#9CA3AF", cursor: "pointer", display: "flex" }}>
          {open ? <ChevronDown size={16} /> : <ChevronRight size={16} />}
        </button>
        <button onClick={() => router.push(`/orders/${draft.id}`)}
          style={{ background: "transparent", border: "none", padding: 0, cursor: "pointer", color: "#E8EDF5", fontWeight: 600, fontSize: 14, textAlign: "left" }}>
          {draft.supplierName ?? t("noSupplier")}
        </button>
        <span style={{ color: "#6B7280", fontSize: 12 }}>{t("lines", { count: draft.items.length })}</span>
        <span style={{ color: "#9CA3AF", fontSize: 12, fontFamily: "monospace" }}>
          {total.toLocaleString(intl, { maximumFractionDigits: 0 })} ₴
        </span>
        <StatusPill
          bg={draft.takenByUserId ? "#1C2A3A" : "#111827"}
          text={draft.takenByUserId ? "#60A5FA" : "#9CA3AF"}
        >
          {draft.takenByUserId
            ? draft.takenByName ? t("statusTaken", { name: draft.takenByName }) : t("statusTakenUnknown")
            : t("statusNew")}
        </StatusPill>
        <div style={{ marginLeft: "auto", display: "flex", gap: 8, alignItems: "center", flexWrap: "wrap" }}>
          {!draft.takenByUserId && (
            <Btn
              onClick={() => take.mutate(draft.id, {
                onSuccess: () => { toast.success(t("toastTaken")); router.push(`/orders/${draft.id}`); },
                onError: fail,
              })}
              disabled={take.isPending}
            >
              {take.isPending ? t("taking") : t("take")}
            </Btn>
          )}
          <Btn variant="ghost" onClick={() => router.push(`/orders/${draft.id}`)}>{t("open")}</Btn>
          {canRelease && (
            <Btn
              variant="ghost"
              onClick={() => release.mutate(draft.id, {
                onSuccess: () => toast.success(t("toastReleased")), onError: fail,
              })}
              disabled={release.isPending}
            >
              {t("release")}
            </Btn>
          )}
          <Link href="/receipts" style={{ color: "#60A5FA", fontSize: 13 }}>{t("openInReceipts")}</Link>
        </div>
      </div>

      {open && (
        <div style={{ marginTop: 10 }}>
          {hint && <div style={{ color: "#F59E0B", fontSize: 12, marginBottom: 8 }}>{hint}</div>}
          <Table columns={lineColumns} rows={draft.items} rowKey={(i) => i.id} emptyMessage="—" />
        </div>
      )}
    </div>
  );
}

export function AutoOrdersSection({ storeId }: { storeId: string | undefined }) {
  const t = useTranslations("Dashboard.orders.page.auto");
  const { data: me } = useMe();
  const { data, isLoading, isError } = useAutoDrafts(storeId);
  const { generate } = useAutoDraftActions();
  const drafts = data?.items ?? [];

  const handleCalculate = () => {
    if (!storeId) return;
    generate.mutate(storeId, {
      onSuccess: (list) => {
        const cancelled = list.filter((d) => d.status === "cancelled").length;
        const active = list.length - cancelled;
        toast.success(
          list.length === 0
            ? t("calcNothing")
            : t("calcResult", { count: active }) + (cancelled ? ` · ${t("calcCancelled", { count: cancelled })}` : ""),
        );
      },
      onError: (err) => toast.error(t("toastFailed", { message: err.message })),
    });
  };

  return (
    <section>
      <div style={{ display: "flex", alignItems: "flex-start", justifyContent: "space-between", gap: 12, marginBottom: 12 }}>
        <div>
          <h2 style={{ color: "#E8EDF5", fontSize: 17, fontWeight: 700, margin: 0 }}>{t("title")}</h2>
          <p style={{ color: "#4B5563", fontSize: 12, margin: "4px 0 0" }}>{t("subtitle")}</p>
        </div>
        <Btn icon={<RefreshCw size={15} />} onClick={handleCalculate} disabled={generate.isPending || !storeId}>
          {generate.isPending ? t("calculating") : t("calculate")}
        </Btn>
      </div>

      {isError ? (
        <div style={{ color: "#F87171", fontSize: 13 }}>{t("loadFailed")}</div>
      ) : isLoading ? null : drafts.length === 0 ? (
        <div style={{ ...card, color: "#4B5563", fontSize: 13, textAlign: "center", padding: 24 }}>{t("empty")}</div>
      ) : (
        <div style={{ display: "flex", flexDirection: "column", gap: 10 }}>
          {drafts.map((d) => <DraftCard key={d.id} draft={d} meId={me?.id} meRole={me?.role} />)}
        </div>
      )}
    </section>
  );
}
