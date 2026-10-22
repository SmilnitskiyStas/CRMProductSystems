"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { useTranslations, useLocale } from "next-intl";
import { toast } from "sonner";
import { ArrowLeft, Printer, Trash2 } from "lucide-react";
import { Btn } from "@/components/ui/Btn";
import { Modal } from "@/components/ui/Modal";
import { Table, type TableColumn } from "@/components/ui/Table";
import { useMe } from "@/features/auth/hooks/useAuth";
import { useSuppliers } from "@/features/suppliers/hooks/useSuppliers";
import { useProductSearch } from "@/features/inventory/hooks/useProducts";
import { ReceiptStatusBadge } from "@/features/receipts/components/ReceiptStatusBadge";
import type { ReceiptDto, ReceiptItemDto } from "@/features/receipts/types";
import { useOrderCard, useOrderCardActions } from "../hooks/useOrderCard";

const RELEASE_OVERRIDE_ROLES = new Set(["enterprise_admin", "network_manager", "provider"]);

const panel: React.CSSProperties = {
  background: "#0D1117", border: "1px solid #1F2937", borderRadius: 10, padding: "14px 16px",
};
const label: React.CSSProperties = { color: "#9CA3AF", fontSize: 12, marginBottom: 5, display: "block" };
const input: React.CSSProperties = {
  background: "#111827", border: "1px solid #1F2937", borderRadius: 8, color: "#E8EDF5",
  padding: "6px 10px", fontSize: 13, outline: "none", boxSizing: "border-box",
};

function NumInput({
  value, disabled, onSave, allowEmpty, width = 90,
}: { value: number | null; disabled: boolean; onSave: (n: number | null) => void; allowEmpty?: boolean; width?: number }) {
  const [text, setText] = useState(value == null ? "" : String(value));
  const commit = () => {
    const raw = text.trim().replace(",", ".");
    if (raw === "" && allowEmpty) { if (value != null) onSave(null); return; }
    const n = Number(raw);
    if (!Number.isFinite(n) || n <= 0 || raw === "") { setText(value == null ? "" : String(value)); return; }
    if (n !== value) onSave(n);
  };
  return (
    <input type="number" min={0} step="any" value={text} disabled={disabled}
      onChange={(e) => setText(e.target.value)} onBlur={commit}
      style={{ ...input, width, opacity: disabled ? 0.6 : 1 }} />
  );
}

function ItemSearch({ onPick }: { onPick: (p: { id: string; name: string }) => void }) {
  const t = useTranslations("Dashboard.orders.page.card");
  const [search, setSearch] = useState("");
  const [debounced, setDebounced] = useState("");
  useEffect(() => {
    const h = setTimeout(() => setDebounced(search), 250);
    return () => clearTimeout(h);
  }, [search]);
  const { data: found = [] } = useProductSearch(debounced);
  return (
    <div style={{ marginTop: 10 }}>
      <input value={search} onChange={(e) => setSearch(e.target.value)}
        placeholder={t("addPlaceholder")} style={{ ...input, width: "100%" }} />
      {debounced.trim() && found.length > 0 && search === debounced && (
        <div style={{ border: "1px solid #1F2937", borderRadius: 8, marginTop: 4, maxHeight: 180, overflowY: "auto" }}>
          {found.map((p) => (
            <button key={p.id} type="button"
              onClick={() => { setSearch(""); setDebounced(""); onPick(p); }}
              style={{ display: "block", width: "100%", textAlign: "left", padding: "7px 12px", background: "transparent", border: "none", color: "#E8EDF5", fontSize: 13, cursor: "pointer" }}>
              {p.name} <span style={{ color: "#6B7280" }}>· {p.unit}</span>
            </button>
          ))}
        </div>
      )}
    </div>
  );
}

const PRINT_CSS = `
.order-print-sheet { display: none; }
@media print {
  body * { visibility: hidden !important; }
  .order-print-sheet, .order-print-sheet * { visibility: visible !important; }
  .order-print-sheet { display: block; position: absolute; left: 0; top: 0; width: 100%; background: #fff; color: #000; padding: 24px; font-family: sans-serif; }
  .order-print-sheet table { width: 100%; border-collapse: collapse; margin-top: 16px; }
  .order-print-sheet th, .order-print-sheet td { border: 1px solid #000; padding: 6px 8px; font-size: 13px; text-align: left; }
}
`;

function OrderBody({ order }: { order: ReceiptDto }) {
  const t = useTranslations("Dashboard.orders.page.card");
  const intl = useLocale() === "en" ? "en-US" : "uk-UA";
  const router = useRouter();
  const { data: me } = useMe();
  const { data: suppliers = [] } = useSuppliers();
  const a = useOrderCardActions(order.id);
  const [confirmSend, setConfirmSend] = useState(false);
  const [confirmCancel, setConfirmCancel] = useState(false);
  const [notes, setNotes] = useState(order.notes ?? "");
  const [expected, setExpected] = useState(order.expectedAt ? order.expectedAt.slice(0, 10) : "");

  const isDraft = order.status === "draft";
  const takenByMe = !!order.takenByUserId && order.takenByUserId === me?.id;
  const takenByOther = !!order.takenByUserId && !takenByMe;
  const canEdit = isDraft && takenByMe;
  const canRelease = isDraft && !!order.takenByUserId && (takenByMe || RELEASE_OVERRIDE_ROLES.has(me?.role ?? ""));
  const total = order.items.reduce((s, i) => s + i.quantityOrdered * (i.pricePurchase ?? 0), 0);
  const money = (n: number) => n.toLocaleString(intl, { maximumFractionDigits: 2 });
  const fail = (e: Error) => toast.error(e.message);
  const busy = Object.values(a).some((m) => m.isPending);
  const supplierName = order.supplierName ?? suppliers.find((s) => s.id === order.supplierId)?.name ?? null;

  const saveItem = (i: ReceiptItemDto, patch: { quantityOrdered?: number; pricePurchase?: number | null }) =>
    a.updateItem.mutate(
      { itemId: i.id, quantityReceived: i.quantityReceived, ...patch },
      { onSuccess: () => toast.success(t("toastSaved")), onError: fail },
    );

  const lineColumns: TableColumn<ReceiptItemDto>[] = [
    { key: "item", header: t("colItem"), cellStyle: { color: "#E8EDF5", fontWeight: 500 }, render: (i) => i.productName },
    {
      key: "qty",
      header: t("colQty"),
      render: (i) =>
        canEdit ? (
          <NumInput key={`q-${i.id}-${i.quantityOrdered}`} value={i.quantityOrdered} disabled={busy}
            onSave={(n) => n != null && saveItem(i, { quantityOrdered: n })} />
        ) : i.quantityOrdered,
    },
    {
      key: "price",
      header: t("colPrice"),
      render: (i) =>
        canEdit ? (
          <NumInput key={`p-${i.id}-${i.pricePurchase}`} value={i.pricePurchase} disabled={busy} allowEmpty width={100}
            onSave={(n) => saveItem(i, { pricePurchase: n })} />
        ) : i.pricePurchase != null ? money(i.pricePurchase) : "—",
    },
    {
      key: "amount",
      header: t("colAmount"),
      render: (i) => (i.pricePurchase != null ? money(i.quantityOrdered * i.pricePurchase) : "—"),
    },
    ...(canEdit
      ? [
          {
            key: "remove",
            header: "",
            width: 48,
            render: (i: ReceiptItemDto) => (
              <button aria-label={t("removeLine")} disabled={busy || order.items.length <= 1}
                onClick={() => a.removeItem.mutate(i.id, { onSuccess: () => toast.success(t("toastRemoved")), onError: fail })}
                style={{ background: "transparent", border: "none", color: "#F87171", cursor: "pointer", opacity: order.items.length <= 1 ? 0.4 : 1 }}>
                <Trash2 size={15} />
              </button>
            ),
          } as TableColumn<ReceiptItemDto>,
        ]
      : []),
  ];

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 20, width: "100%", padding: "28px 32px" }}>
      <style>{PRINT_CSS}</style>

      <div>
        <button onClick={() => router.push("/orders")}
          style={{ background: "transparent", border: "none", color: "#9CA3AF", cursor: "pointer", display: "inline-flex", alignItems: "center", gap: 6, fontSize: 13, padding: 0 }}>
          <ArrowLeft size={15} /> {t("back")}
        </button>
      </div>

      <div style={{ display: "flex", alignItems: "center", gap: 10, flexWrap: "wrap" }}>
        <h1 style={{ color: "#E8EDF5", fontSize: 22, fontWeight: 700, margin: 0 }}>
          {t("title", { store: order.destinationStoreName })}
        </h1>
        <ReceiptStatusBadge status={order.status} />
        {order.source === "auto_replenishment" && (
          <span style={{ fontSize: 11, fontWeight: 600, padding: "3px 8px", borderRadius: 20, background: "#1E1B2E", color: "#A78BFA" }}>{t("autoBadge")}</span>
        )}
        {isDraft && order.takenByUserId && (
          <span style={{ fontSize: 11, fontWeight: 600, padding: "3px 8px", borderRadius: 20, background: "#1C2A3A", color: "#60A5FA" }}>
            {order.takenByName ? t("takenBy", { name: order.takenByName }) : t("takenUnknown")}
          </span>
        )}
      </div>

      {!isDraft && (
        <div style={{ ...panel, color: "#A78BFA", fontSize: 13 }}>
          {order.status === "ordered"
            ? t("bannerSent", { name: order.orderedByName ?? "—", date: order.orderedAt ? new Date(order.orderedAt).toLocaleString(intl) : "—" })
            : t("bannerClosed")}
        </div>
      )}
      {isDraft && !order.takenByUserId && (
        <div style={{ ...panel, color: "#F59E0B", fontSize: 13 }}>{t("bannerUntaken")}</div>
      )}
      {isDraft && takenByOther && (
        <div style={{ ...panel, color: "#F59E0B", fontSize: 13 }}>{t("bannerOther", { name: order.takenByName ?? "—" })}</div>
      )}

      <div style={{ ...panel, display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(220px, 1fr))", gap: 14 }}>
        <div>
          <span style={label}>{t("store")}</span>
          <div style={{ color: "#E8EDF5", fontSize: 14 }}>{order.destinationStoreName}</div>
        </div>
        <div>
          <span style={label}>{t("supplier")}{canEdit ? " *" : ""}</span>
          {canEdit ? (
            <select value={order.supplierId ?? ""} disabled={busy}
              onChange={(e) => a.header.mutate({ supplierId: e.target.value || null }, { onSuccess: () => toast.success(t("toastSaved")), onError: fail })}
              style={{ ...input, width: "100%" }}>
              <option value="">{t("selectSupplier")}</option>
              {suppliers.map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}
            </select>
          ) : (
            <div style={{ color: "#E8EDF5", fontSize: 14 }}>{supplierName ?? t("noSupplier")}</div>
          )}
        </div>
        <div>
          <span style={label}>{t("expectedAt")}</span>
          {canEdit ? (
            <input type="date" value={expected} style={{ ...input, width: "100%" }}
              onChange={(e) => setExpected(e.target.value)}
              onBlur={() => {
                if (expected !== (order.expectedAt ? order.expectedAt.slice(0, 10) : ""))
                  a.header.mutate(expected ? { expectedAt: expected } : { clearExpectedAt: true },{ onSuccess: () => toast.success(t("toastSaved")), onError: fail });
              }} />
          ) : (
            <div style={{ color: "#E8EDF5", fontSize: 14 }}>{order.expectedAt ? order.expectedAt.slice(0, 10) : "—"}</div>
          )}
        </div>
        <div style={{ gridColumn: "1 / -1" }}>
          <span style={label}>{t("notes")}</span>
          {canEdit ? (
            <textarea value={notes} rows={2} style={{ ...input, width: "100%", resize: "vertical" }}
              onChange={(e) => setNotes(e.target.value)}
              onBlur={() => {
                if (notes !== (order.notes ?? ""))
                  a.header.mutate({ notes: notes.trim() || null }, { onSuccess: () => toast.success(t("toastSaved")), onError: fail });
              }} />
          ) : (
            <div style={{ color: "#E8EDF5", fontSize: 13 }}>{order.notes || "—"}</div>
          )}
        </div>
      </div>

      <Table
        columns={lineColumns}
        rows={order.items}
        rowKey={(i) => i.id}
        emptyMessage="—"
      />

      <div style={panel}>
        {canEdit && (
          <ItemSearch onPick={(p) => a.addItem.mutate(
            { productId: p.id, quantityOrdered: 1 },
            { onSuccess: () => toast.success(t("toastAdded")), onError: fail },
          )} />
        )}
        <div style={{ display: "flex", justifyContent: "flex-end", gap: 24, marginTop: 12, color: "#E8EDF5", fontSize: 14 }}>
          <span style={{ color: "#9CA3AF" }}>{t("positions", { count: order.items.length })}</span>
          <strong>{t("total")}: {money(total)} ₴</strong>
        </div>
      </div>

      <div style={{ display: "flex", gap: 10, flexWrap: "wrap", alignItems: "center" }}>
        {isDraft && !order.takenByUserId && (
          <Btn disabled={a.take.isPending}
            onClick={() => a.take.mutate(undefined, { onSuccess: () => toast.success(t("toastTaken")), onError: fail })}>
            {a.take.isPending ? t("taking") : t("take")}
          </Btn>
        )}
        {canEdit && (
          <Btn variant="success" disabled={busy || !order.supplierId || order.items.length === 0}
            onClick={() => setConfirmSend(true)}>
            {t("send")}
          </Btn>
        )}
        {canEdit && !order.supplierId && <span style={{ color: "#F59E0B", fontSize: 12 }}>{t("needSupplier")}</span>}
        {canRelease && (
          <Btn variant="ghost" disabled={a.release.isPending}
            onClick={() => a.release.mutate(undefined, { onSuccess: () => toast.success(t("toastReleased")), onError: fail })}>
            {t("release")}
          </Btn>
        )}
        <Btn variant="ghost" icon={<Printer size={15} />} onClick={() => window.print()}>{t("print")}</Btn>
        {canEdit && (
          <Btn variant="danger" disabled={busy} onClick={() => setConfirmCancel(true)}>{t("cancelOrder")}</Btn>
        )}
      </div>

      {confirmSend && (
        <Modal title={t("confirmSendTitle")} onClose={() => setConfirmSend(false)} width={460}>
          <div style={{ color: "#E8EDF5", fontSize: 14, display: "flex", flexDirection: "column", gap: 6 }}>
            <div>{t("supplier")}: <strong>{supplierName ?? "—"}</strong></div>
            <div>{t("positions", { count: order.items.length })}</div>
            <div>{t("total")}: <strong>{money(total)} ₴</strong></div>
            <div style={{ display: "flex", gap: 10, marginTop: 12, justifyContent: "flex-end" }}>
              <Btn variant="ghost" onClick={() => setConfirmSend(false)}>{t("back")}</Btn>
              <Btn variant="success" disabled={a.send.isPending}
                onClick={() => a.send.mutate(undefined, {
                  onSuccess: () => { setConfirmSend(false); toast.success(t("toastSent")); },
                  onError: (e) => { setConfirmSend(false); fail(e); },
                })}>
                {a.send.isPending ? t("sending") : t("confirmSend")}
              </Btn>
            </div>
          </div>
        </Modal>
      )}
      {confirmCancel && (
        <Modal title={t("confirmCancelTitle")} onClose={() => setConfirmCancel(false)} width={420}>
          <div style={{ color: "#E8EDF5", fontSize: 14 }}>
            {t("confirmCancelText")}
            <div style={{ display: "flex", gap: 10, marginTop: 14, justifyContent: "flex-end" }}>
              <Btn variant="ghost" onClick={() => setConfirmCancel(false)}>{t("back")}</Btn>
              <Btn variant="danger" disabled={a.cancel.isPending}
                onClick={() => a.cancel.mutate(undefined, {
                  onSuccess: () => { toast.success(t("toastCancelled")); router.push("/orders"); },
                  onError: (e) => { setConfirmCancel(false); fail(e); },
                })}>
                {t("cancelOrder")}
              </Btn>
            </div>
          </div>
        </Modal>
      )}

      <div className="order-print-sheet">
        <h2 style={{ margin: 0 }}>{t("printTitle")}</h2>
        <div style={{ marginTop: 8 }}>{t("store")}: {order.destinationStoreName}</div>
        <div>{t("supplier")}: {supplierName ?? "—"}</div>
        <div>{t("date")}: {new Date(order.orderedAt ?? order.createdAt).toLocaleDateString(intl)}</div>
        {order.expectedAt && <div>{t("expectedAt")}: {order.expectedAt.slice(0, 10)}</div>}
        <table>
          <thead><tr><th>{t("colItem")}</th><th>{t("colQty")}</th><th>{t("colPrice")}</th><th>{t("colAmount")}</th></tr></thead>
          <tbody>
            {order.items.map((i) => (
              <tr key={i.id}>
                <td>{i.productName}</td><td>{i.quantityOrdered}</td>
                <td>{i.pricePurchase != null ? money(i.pricePurchase) : "—"}</td>
                <td>{i.pricePurchase != null ? money(i.quantityOrdered * i.pricePurchase) : "—"}</td>
              </tr>
            ))}
          </tbody>
        </table>
        <p style={{ textAlign: "right", fontWeight: 700 }}>{t("total")}: {money(total)} ₴</p>
        {order.notes && <p>{t("notes")}: {order.notes}</p>}
      </div>
    </div>
  );
}

export function OrderCard({ id }: { id: string }) {
  const t = useTranslations("Dashboard.orders.page.card");
  const { data, isLoading, isError } = useOrderCard(id);
  if (isLoading) return null;
  if (isError || !data) return <div style={{ color: "#F87171", fontSize: 13, padding: "28px 32px" }}>{t("loadFailed")}</div>;
  // key resets local field state when the server copy changes owner/status.
  return <OrderBody key={`${data.id}-${data.status}-${data.takenByUserId ?? ""}`} order={data} />;
}
