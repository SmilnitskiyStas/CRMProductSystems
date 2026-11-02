"use client";
import { Btn } from "@/components/ui/Btn";
import { PageContainer } from "@/components/ui/PageContainer";
import { PageHeader } from "@/components/ui/PageHeader";
import { BackLink } from "@/components/ui/BackLink";

import { ReceiptCategoryPanel } from "@/features/receipts/components/ReceiptCategoryPanel";
import { categorySource } from "@/features/receipts/api/category-work";
import { useEffect, useMemo, useRef, useState } from "react";
import { ApiError, getSessionEpoch } from "@/lib/api";
import { hasPendingReceiving } from "@/features/receipts/api/receipt-receiving";
import { useParams, useRouter } from "next/navigation";
import { ArrowLeft, Check, Package, Truck, Building2, Calendar, Hash, CheckCircle2 } from "lucide-react";
import { useTranslations, useLocale } from "next-intl";
import {
  useReceiptDetail,
  useUpdateReceiptItems,
  useReceiveReceipt,
  useCancelReceipt,
} from "@/features/receipts/hooks/useReceipts";
import { ReceiptStatusBadge } from "@/features/receipts/components/ReceiptStatusBadge";
import type { ReceiptStatus, UpdateItemPayload, ReceiptItemDto } from "@/features/receipts/types";
import { useMe } from "@/features/auth/hooks/useAuth";
import { AccessDenied } from "@/components/AccessDenied";
import { CAN_RECEIVE_STOCK, hasRole } from "@/lib/roles";
import { Table, type TableColumn } from "@/components/ui/Table";

// ── Styles ────────────────────────────────────────────────────────────────────

const inputStyle: React.CSSProperties = {
  background: "#111827",
  border: "1px solid #1F2937",
  borderRadius: 6,
  color: "#E8EDF5",
  fontSize: 12,
  padding: "5px 8px",
  outline: "none",
  width: "100%",
  boxSizing: "border-box",
  textAlign: "center",
};

// ── Helpers ───────────────────────────────────────────────────────────────────

function formatDate(s: string | null | undefined): string {
  if (!s) return "";
  if (s.includes("T")) return s.slice(0, 10);
  return s;
}

function displayDate(s: string | null | undefined, intlLocale: string): string {
  if (!s) return "—";
  try {
    return new Date(s).toLocaleDateString(intlLocale);
  } catch {
    return s;
  }
}

// ── Stat card ─────────────────────────────────────────────────────────────────
function StatCard({
  icon,
  label,
  value,
  sub,
}: {
  icon: React.ReactNode;
  label: string;
  value: React.ReactNode;
  sub?: string;
}) {
  return (
    <div
      style={{
        background: "#0D1117",
        border: "1px solid #1F2937",
        borderRadius: 10,
        padding: "14px 18px",
        display: "flex",
        alignItems: "flex-start",
        gap: 12,
        flex: "1 1 180px",
        minWidth: 0,
      }}
    >
      <div
        style={{
          color: "#4B5563",
          display: "flex",
          alignItems: "center",
          paddingTop: 2,
          flexShrink: 0,
        }}
      >
        {icon}
      </div>
      <div style={{ minWidth: 0 }}>
        <div style={{ color: "#4B5563", fontSize: 11, marginBottom: 4 }}>{label}</div>
        <div style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 600, wordBreak: "break-word" }}>
          {value}
        </div>
        {sub && <div style={{ color: "#4B5563", fontSize: 11, marginTop: 2 }}>{sub}</div>}
      </div>
    </div>
  );
}

// ── Page ──────────────────────────────────────────────────────────────────────
export default function ReceiptDetailPage() {
  const t = useTranslations("Dashboard.receipts");
  const tDetail = useTranslations("Dashboard.receipts.detail");
  const tCommon = useTranslations("Common");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";
  const { data: me } = useMe();
  const access = me ? hasRole(me.role, CAN_RECEIVE_STOCK) : null;

  const { id } = useParams<{ id: string }>();
  const router = useRouter();

  const { data: receipt, isLoading, refetch: reloadReceipt } = useReceiptDetail(access === true ? id : null);
  const categorySnapshot=useMemo(()=>receipt?categorySource(receipt):null,[receipt]);
  const updateItems = useUpdateReceiptItems(id);
  const receive = useReceiveReceipt();
  const cancel = useCancelReceipt();
  const [categoryBlocked,setCategoryBlocked]=useState(false);
  const [linePage,setLinePage]=useState(1);
  const [lineSearch,setLineSearch]=useState("");
  const [receivingPending, setReceivingPending] = useState(false);
  const [receivingBusy, setReceivingBusy] = useState(false);
  const [reviewRequired, setReviewRequired] = useState(false);
  const receivingLock = useRef(false);
  const receivingIdentity = useRef("");
  receivingIdentity.current = `${me?.tenantId}:${me?.id}:${id}`;
  useEffect(() => {
    setReviewRequired(false);
    if (!me?.tenantId) { setReceivingPending(false); return; }
    try { setReceivingPending(hasPendingReceiving(id, { tenantId: me.tenantId, actorId: me.id, locationId: "" })); }
    catch { setReceivingPending(true); }
  }, [id, me?.id, me?.tenantId]);

  const [edits, setEdits] = useState<
    Record<
      string,
      {
        quantityReceived?: string;
        expiryDate?: string;
        batchNumber?: string;
        confirmed: boolean;
      }
    >
  >({});

  if (access === null) return null;
  if (!access) return <AccessDenied title={t("title")} />;

  if (isLoading) {
    return (
      <div style={{ padding: 40, textAlign: "center", color: "#4B5563", fontSize: 13 }}>
        {tCommon("loading")}
      </div>
    );
  }
  if (!receipt) {
    return (
      <div style={{ padding: 40, textAlign: "center", color: "#F87171", fontSize: 13 }}>
        {tDetail("notFound")}
      </div>
    );
  }

  const collaborative=!!receipt.receivingWork?.active;
  const isEditable = !collaborative && !receivingPending && !receivingBusy && !reviewRequired && (receipt.status === "draft" || receipt.status === "ordered" || receipt.status === "in_transit");

  function getEdit(itemId: string) {
    return (
      edits[itemId] ?? {
        confirmed: receipt!.items.find((i) => i.id === itemId)?.isProcessed ?? false,
      }
    );
  }

  function updateEdit(
    itemId: string,
    patch: Partial<{
      quantityReceived: string;
      expiryDate: string;
      batchNumber: string;
      confirmed: boolean;
    }>,
  ) {
    setEdits((prev) => ({ ...prev, [itemId]: { ...getEdit(itemId), ...patch } }));
  }

  const processedCount = receipt.items.filter(
    (i) => getEdit(i.id).confirmed || i.isProcessed,
  ).length;
  const allProcessed = collaborative ? receipt.receivingWork!.groups.every(g=>g.ready) : processedCount === receipt.items.length;
  const filteredLines=receipt.items.filter(l=>!lineSearch.trim()||`${l.productName} ${l.productBarcode??""} ${l.batchNumber??""}`.toLocaleLowerCase("uk-UA").includes(lineSearch.trim().toLocaleLowerCase("uk-UA")));
  const totalLinePages=Math.max(1,Math.ceil(filteredLines.length/50)),currentLinePage=Math.min(linePage,totalLinePages);

  async function handleSave() {
    const payload: UpdateItemPayload[] = Object.entries(edits)
      .filter(([, e]) => e.confirmed || e.quantityReceived || e.expiryDate || e.batchNumber)
      .map(([itemId, e]) => ({
        itemId,
        quantityReceived: e.quantityReceived ? parseFloat(e.quantityReceived) : undefined,
        expiryDate: e.expiryDate || undefined,
        batchNumber: e.batchNumber || undefined,
      }));
    if (collaborative) return receipt!;
    if (payload.length > 0) {
      return await updateItems.mutateAsync(payload);
    }
    return receipt!;
  }

  async function handleReceive() {
    if (receivingLock.current) return;
    receivingLock.current = true; setReceivingBusy(true);
    const identity = receivingIdentity.current, epoch = getSessionEpoch();
    const current = () => receivingIdentity.current === identity && getSessionEpoch() === epoch;
    try {
      const reviewed = receivingPending ? receipt! : await handleSave();
      if (!current()) return;
      await receive.mutateAsync(me?.tenantId ? { id, confirmation: { tenantId: me.tenantId, actorId: me.id,
        locationId: reviewed.destinationStoreId, expectedFingerprint: reviewed.receivingFingerprint } } : id);
      if (current()) { setReceivingPending(false); router.push("/receipts"); }
    } catch (error) {
      if (!current()) return;
      if (me?.tenantId) {
        try {
          const pending = hasPendingReceiving(id, { tenantId: me.tenantId, actorId: me.id, locationId: "" }); setReceivingPending(pending);
          if (!pending && error instanceof ApiError && (error.body as { code?: string } | undefined)?.code === "receipt_posting_conflict") setReviewRequired(true);
        }
        catch { setReceivingPending(true); }
      }
    } finally { receivingLock.current = false; setReceivingBusy(false); }
  }

  const itemColumns: TableColumn<ReceiptItemDto>[] = [
    {
      key: "product",
      header: tDetail("headers.product"),
      width: 220,
      render: (item) => (
        <>
          <div style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 500, lineHeight: 1.4 }}>
            {item.productName}
          </div>
          {item.discrepancyNotes && (
            <div style={{ color: "#FBBF24", fontSize: 11, marginTop: 2 }}>
              ⚠ {item.discrepancyNotes}
            </div>
          )}
        </>
      ),
    },
    {
      key: "barcode",
      header: tDetail("headers.barcode"),
      cellStyle: { fontFamily: "monospace", fontSize: 11, color: "#4B5563" },
      render: (item) => item.productBarcode ?? "—",
    },
    {
      key: "ordered",
      header: tDetail("headers.ordered"),
      cellStyle: { fontFamily: "monospace" },
      render: (item) => `${item.quantityOrdered}${item.unit ? ` ${item.unit}` : ""}`,
    },
    {
      key: "received",
      header: tDetail("headers.received"),
      width: 110,
      render: (item) => {
        const edit = getEdit(item.id);
        const qtyReceived =
          edit.quantityReceived !== undefined
            ? parseFloat(edit.quantityReceived)
            : item.quantityReceived;
        const hasDiscrepancy = qtyReceived != null && qtyReceived < item.quantityOrdered;
        return isEditable && !item.isProcessed ? (
          <input
            type="number"
            min="0"
            step="any"
            placeholder={String(item.quantityOrdered)}
            value={edit.quantityReceived ?? ""}
            onChange={(e) => updateEdit(item.id, { quantityReceived: e.target.value })}
            style={inputStyle}
          />
        ) : (
          <span
            style={{
              fontFamily: "monospace",
              color: hasDiscrepancy ? "#FBBF24" : "#4ADE80",
              fontWeight: 600,
            }}
          >
            {item.quantityReceived ?? item.quantityOrdered}
          </span>
        );
      },
    },
    {
      key: "expiry",
      header: tDetail("headers.expiry"),
      width: 140,
      render: (item) => {
        const edit = getEdit(item.id);
        return isEditable && !item.isProcessed ? (
          <input
            type="date"
            value={edit.expiryDate ?? formatDate(item.expiryDate)}
            onChange={(e) => updateEdit(item.id, { expiryDate: e.target.value })}
            style={inputStyle}
          />
        ) : (
          <span style={{ color: "#9CA3AF", fontSize: 13 }}>
            {displayDate(item.expiryDate, intlLocale)}
          </span>
        );
      },
    },
    {
      key: "batch",
      header: tDetail("headers.batch"),
      width: 130,
      render: (item) => {
        const edit = getEdit(item.id);
        return isEditable && !item.isProcessed ? (
          <input
            type="text"
            placeholder={tDetail("batchNumberPlaceholder")}
            value={edit.batchNumber ?? (item.batchNumber ?? "")}
            onChange={(e) => updateEdit(item.id, { batchNumber: e.target.value })}
            style={inputStyle}
          />
        ) : (
          <span style={{ color: "#9CA3AF", fontSize: 12, fontFamily: "monospace" }}>
            {item.batchNumber ?? "—"}
          </span>
        );
      },
    },
    {
      key: "price",
      header: tDetail("headers.price"),
      cellStyle: { fontFamily: "monospace" },
      render: (item) =>
        item.pricePurchase != null ? `${item.pricePurchase.toLocaleString(intlLocale)} ₴` : "—",
    },
    {
      key: "status",
      header: tDetail("headers.status"),
      width: 90,
      render: (item) => {
        const edit = getEdit(item.id);
        return isEditable && !item.isProcessed ? (
          <button
            onClick={() => updateEdit(item.id, { confirmed: true })}
            style={{
              display: "inline-flex",
              alignItems: "center",
              gap: 4,
              background: "#1a3a2e",
              border: "1px solid #065F46",
              borderRadius: 6,
              color: "#4ADE80",
              fontSize: 11,
              fontWeight: 600,
              padding: "5px 10px",
              cursor: "pointer",
              whiteSpace: "nowrap",
            }}
          >
            <Check size={11} /> {tDetail("confirmItem")}
          </button>
        ) : (
          <span
            style={{
              display: "inline-flex",
              alignItems: "center",
              gap: 4,
              color: "#4ADE80",
              fontSize: 12,
              fontWeight: 600,
            }}
          >
            <CheckCircle2 size={14} /> {tDetail("done")}
          </span>
        );
      },
    },
  ];

  return (
    <PageContainer>
      <BackLink onClick={() => router.push("/receipts")}>{tCommon("back")}</BackLink>
      <PageHeader title={<span style={{display:"inline-flex",alignItems:"center",gap:10,flexWrap:"wrap"}}>{t("title")}<ReceiptStatusBadge status={receipt.status as ReceiptStatus}/></span>}
        subtitle={<>{receipt.destinationStoreName}{receipt.supplierName&&` · ${receipt.supplierName}`}{receipt.expectedAt&&` · ${t("drawer.expected")}: ${new Date(receipt.expectedAt).toLocaleDateString(intlLocale)}`}</>}
        actions={(isEditable || collaborative && ["draft","ordered","in_transit"].includes(receipt.status) || receivingPending || receivingBusy || reviewRequired)?<>
          <Btn variant="ghost" onClick={()=>cancel.mutate(id)} disabled={cancel.isPending||receivingPending||receivingBusy}>{tCommon("cancel")}</Btn>
          <Btn variant="success" icon={<Check size={14}/>} onClick={handleReceive} disabled={categoryBlocked||(!receivingPending&&!allProcessed)||receivingBusy||updateItems.isPending||reviewRequired}>{tDetail(receivingPending?"retryReceive":"confirmReceive")}</Btn>
        </>:undefined}/>
      {receivingPending && <p role="status" style={{ color: "#FBBF24" }}>{tDetail("receivingUncertain")}</p>}
      {updateItems.error instanceof Error && <p role="alert" style={{ color: "#F87171" }}>{updateItems.error.message}</p>}
      {reviewRequired && <button onClick={async () => { const result = await reloadReceipt(); if (!result.error) { setEdits({}); setReviewRequired(false); } }}>{tDetail("reviewReceiving")}</button>}
      {receive.error instanceof ApiError && <p role="alert" style={{ color: "#F87171" }}>{tDetail(
        (receive.error.body as { code?: string } | undefined)?.code === "receipt_receiving_storage" ? "receivingStorage" : "receivingFailed")}</p>}

      {/* ── Info cards ─────────────────────────────────────────────────── */}
      <div style={{ display: "flex", gap: 12, flexWrap: "wrap" }}>
        <StatCard
          icon={<Building2 size={16} />}
          label={t("drawer.destinationStore")}
          value={receipt.destinationStoreName}
        />
        <StatCard
          icon={<Truck size={16} />}
          label={t("drawer.supplier")}
          value={receipt.supplierName ?? "—"}
        />
        <StatCard
          icon={<Calendar size={16} />}
          label={t("drawer.expected")}
          value={displayDate(receipt.expectedAt, intlLocale)}
          sub={receipt.receivedAt ? `${t("drawer.received")}: ${displayDate(receipt.receivedAt, intlLocale)}` : undefined}
        />
        <StatCard
          icon={<Package size={16} />}
          label={t("drawer.items")}
          value={tDetail("itemsCount", { count: receipt.items.length })}
          sub={receipt.viaCentralStore ? t("drawer.viaCentral") : undefined}
        />
      </div>

      {/* ── Progress bar ───────────────────────────────────────────────── */}
      {isEditable && receipt.items.length > 0 && (
        <div
          style={{
            background: "#0D1117",
            border: "1px solid #1F2937",
            borderRadius: 10,
            padding: "14px 18px",
          }}
        >
          <div style={{ display: "flex", justifyContent: "space-between", marginBottom: 8 }}>
            <span style={{ color: "#9CA3AF", fontSize: 13, fontWeight: 500 }}>
              {tDetail("progress")}
            </span>
            <span style={{ color: allProcessed ? "#4ADE80" : "#6B7280", fontSize: 13, fontWeight: 600 }}>
              {processedCount} / {receipt.items.length}
            </span>
          </div>
          <div style={{ height: 6, background: "#1F2937", borderRadius: 3 }}>
            <div
              style={{
                height: "100%",
                borderRadius: 3,
                background: allProcessed ? "#059669" : "#3B82F6",
                width: `${(processedCount / Math.max(receipt.items.length, 1)) * 100}%`,
                transition: "width 0.3s",
              }}
            />
          </div>
        </div>
      )}

      {!receivingPending && <ReceiptCategoryPanel source={categorySnapshot!} onBlocked={setCategoryBlocked} user={{id:me!.id,role:me!.role,twoFactorEnabled:me!.twoFactorEnabled}} onChanged={()=>{void reloadReceipt();}} onReview={()=>{void reloadReceipt();}}/>}
      {/* ── Items table ────────────────────────────────────────────────── */}
      {!collaborative && <><label>Пошук товару<input aria-label="Пошук рядків накладної" style={inputStyle} value={lineSearch} onChange={e=>{setLineSearch(e.target.value);setLinePage(1);}}/></label>
      <Table
        columns={itemColumns}
        rows={filteredLines.slice((currentLinePage-1)*50,currentLinePage*50)}
        page={currentLinePage} totalPages={totalLinePages} totalCount={filteredLines.length} onPageChange={setLinePage}
        rowKey={(item) => item.id}
        minWidth={700}
        rowStyle={(item) => {
          const processed = getEdit(item.id).confirmed || item.isProcessed;
          return { background: processed ? "rgba(5,150,105,0.04)" : "transparent" };
        }}
      />

      </>}
      {/* ── Notes ──────────────────────────────────────────────────────── */}
      {receipt.notes && (
        <div
          style={{
            background: "#0D1117",
            border: "1px solid #1F2937",
            borderRadius: 10,
            padding: "14px 18px",
          }}
        >
          <div style={{ color: "#4B5563", fontSize: 11, fontWeight: 600, textTransform: "uppercase", letterSpacing: "0.05em", marginBottom: 6 }}>
            {t("drawer.notes")}
          </div>
          <div style={{ color: "#9CA3AF", fontSize: 13 }}>{receipt.notes}</div>
        </div>
      )}
    </PageContainer>
  );
}
