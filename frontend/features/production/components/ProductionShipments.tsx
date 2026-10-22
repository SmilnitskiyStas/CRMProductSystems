"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { useTranslations } from "next-intl";
import { useQueryClient } from "@tanstack/react-query";
import { Printer } from "lucide-react";
import { Btn } from "@/components/ui/Btn";
import { PageContainer } from "@/components/ui/PageContainer";
import { Tabs } from "@/components/ui/Tabs";
import { Table, type TableColumn } from "@/components/ui/Table";
import { useProductionShipments } from "../hooks/useProductionInsights";
import { useShipProductionRequest } from "../hooks/useProductionRequests";
import type { ReadyShipmentDto, ShippedShipmentDto, ShipmentLineDto } from "../insights-types";
import {
  PageHeader,
  ProductionLocationSelect,
  StateBox,
  Pill,
  mutedText,
  fmtDate,
  fmtQty,
  useIntlLocale,
} from "./InsightsShared";

const PRINT_CSS = `
.ship-print-sheet { display: none; }
@media print {
  body * { visibility: hidden !important; }
  .ship-print-sheet, .ship-print-sheet * { visibility: visible !important; }
  .ship-print-sheet { display: block; position: absolute; left: 0; top: 0; width: 100%; background: #fff; color: #000; padding: 24px; font-family: sans-serif; }
  .ship-print-sheet table { width: 100%; border-collapse: collapse; margin-top: 16px; }
  .ship-print-sheet th, .ship-print-sheet td { border: 1px solid #000; padding: 6px 8px; font-size: 13px; text-align: left; }
  .ship-print-sheet .sign { margin-top: 48px; display: flex; justify-content: space-between; font-size: 13px; }
  .ship-print-sheet .sign span { display: inline-block; min-width: 220px; border-top: 1px solid #000; padding-top: 4px; }
}
`;

type Result = { ok: boolean; text: string };

function Lines({ lines, intl }: { lines?: ShipmentLineDto[]; intl: string }) {
  if (!lines?.length) return <span style={mutedText}>—</span>;
  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 2, alignItems: "flex-start", textAlign: "left" }}>
      {lines.map((l, i) => (
        <span key={i} style={{ fontSize: 12 }}>
          {l.itemName ?? "—"} — <b>{fmtQty(l.qty, intl)}</b> {l.unit ?? ""}
        </span>
      ))}
    </div>
  );
}

export function ProductionShipments() {
  const t = useTranslations("Dashboard.production.shipments");
  const intl = useIntlLocale();
  const qc = useQueryClient();
  const ship = useShipProductionRequest();
  const [tab, setTab] = useState<"ready" | "shipped">("ready");
  const [locationId, setLocationId] = useState("");
  const [results, setResults] = useState<Record<string, Result>>({});
  const [busyId, setBusyId] = useState<string | null>(null);
  const [bulkBusy, setBulkBusy] = useState(false);
  const [printing, setPrinting] = useState<ReadyShipmentDto | null>(null);

  const { data, isLoading, isError } = useProductionShipments({
    status: tab,
    locationId: locationId || undefined,
  });

  // Print one store at a time: render its sheet, then open the print dialog.
  useEffect(() => {
    if (!printing) return;
    const timer = setTimeout(() => window.print(), 50);
    const done = () => setPrinting(null);
    window.addEventListener("afterprint", done);
    return () => {
      clearTimeout(timer);
      window.removeEventListener("afterprint", done);
    };
  }, [printing]);

  function refresh() {
    qc.invalidateQueries({ queryKey: ["production"] });
    qc.invalidateQueries({ queryKey: ["transfers"] });
  }

  async function shipOne(r: ReadyShipmentDto) {
    setBusyId(r.requestId);
    try {
      await ship.mutateAsync(r.requestId);
      setResults((p) => ({
        ...p,
        [r.requestId]: { ok: true, text: t("shippedOk") },
      }));
    } catch (e) {
      setResults((p) => ({
        ...p,
        [r.requestId]: { ok: false, text: e instanceof Error ? e.message : t("shipFailed") },
      }));
    }
    setBusyId(null);
    refresh();
  }

  async function shipAll(rows: ReadyShipmentDto[]) {
    setBulkBusy(true);
    for (const r of rows.filter((x) => x.canShip !== false && results[x.requestId]?.ok !== true)) {
      await shipOne(r);
    }
    setBulkBusy(false);
  }

  const readyRows = (tab === "ready" ? (data ?? []) : []) as ReadyShipmentDto[];
  const shippedRows = (tab === "shipped" ? (data ?? []) : []) as ShippedShipmentDto[];

  const readyColumns: TableColumn<ReadyShipmentDto>[] = [
    { key: "store", header: t("colStore"), render: (r) => <b style={{ color: "#E8EDF5" }}>{r.storeName ?? "—"}</b> },
    { key: "lines", header: t("colLines"), render: (r) => <Lines lines={r.lines} intl={intl} /> },
    { key: "needed", header: t("colNeededBy"), render: (r) => fmtDate(r.neededBy, intl) },
    {
      key: "orders",
      header: t("colOrders"),
      render: (r) =>
        r.orderIds?.length ? (
          <span style={{ display: "inline-flex", gap: 6, flexWrap: "wrap", justifyContent: "center" }}>
            {r.orderIds.map((id, i) => (
              <Link key={id} href={`/production/orders/${id}`} style={{ color: "#60A5FA", fontSize: 12 }}>
                #{i + 1}
              </Link>
            ))}
          </span>
        ) : (
          "—"
        ),
    },
    {
      key: "stage",
      header: t("colStatus"),
      render: (r) =>
        r.canShip === false ? <Pill color="#FBBF24" bg="#3a2a05">{r.productionStage ?? t("notReady")}</Pill> : <Pill color="#4ADE80" bg="#0b2a1e">{t("ready")}</Pill>,
    },
    {
      key: "actions",
      header: "",
      render: (r) => {
        const res = results[r.requestId];
        return (
          <div style={{ display: "flex", flexDirection: "column", gap: 6, alignItems: "center" }}>
            <div style={{ display: "flex", gap: 6 }}>
              <Btn size="sm" variant="success" disabled={r.canShip === false || busyId !== null || bulkBusy || res?.ok === true} onClick={() => shipOne(r)}>
                {busyId === r.requestId ? t("shipping") : t("ship")}
              </Btn>
              <Btn size="sm" variant="ghost" icon={<Printer size={12} />} onClick={() => setPrinting(r)}>
                {t("print")}
              </Btn>
            </div>
            {res && <span style={{ fontSize: 11, color: res.ok ? "#4ADE80" : "#F87171" }}>{res.text}</span>}
          </div>
        );
      },
    },
  ];

  const shippedColumns: TableColumn<ShippedShipmentDto>[] = [
    { key: "store", header: t("colStore"), render: (r) => <b style={{ color: "#E8EDF5" }}>{r.storeName ?? "—"}</b> },
    { key: "date", header: t("colShippedAt"), render: (r) => fmtDate(r.shippedAt, intl) },
    { key: "lines", header: t("colLines"), render: (r) => <Lines lines={r.lines} intl={intl} /> },
    {
      key: "transfer",
      header: t("colTransfer"),
      render: (r) =>
        r.transferId ? (
          <Link href="/transfers" style={{ color: "#60A5FA", fontSize: 12 }}>
            {t("openTransfer")}
          </Link>
        ) : (
          "—"
        ),
    },
    { key: "tstatus", header: t("colTransferStatus"), render: (r) => (r.transferStatus ? <Pill>{r.transferStatus}</Pill> : "—") },
  ];

  const bulkable = readyRows.filter((r) => r.canShip !== false && results[r.requestId]?.ok !== true).length;

  return (
    <PageContainer>
      <style>{PRINT_CSS}</style>
      <PageHeader
        title={t("title")}
        subtitle={t("subtitle")}
        right={
          <>
            <ProductionLocationSelect value={locationId} onChange={setLocationId} allLabel={t("allLocations")} />
            {tab === "ready" && (
              <Btn disabled={bulkable === 0 || bulkBusy || busyId !== null} onClick={() => shipAll(readyRows)}>
                {bulkBusy ? t("shipping") : t("shipAll", { count: bulkable })}
              </Btn>
            )}
          </>
        }
      />

      <Tabs
        items={[
          { key: "ready", label: t("tabReady") },
          { key: "shipped", label: t("tabShipped") },
        ]}
        value={tab}
        onChange={setTab}
      />

      {isLoading ? (
        <StateBox kind="loading">{t("loading")}</StateBox>
      ) : isError ? (
        <StateBox kind="error">{t("loadError")}</StateBox>
      ) : tab === "ready" ? (
        <Table columns={readyColumns} rows={readyRows} rowKey={(r) => r.requestId} emptyMessage={t("emptyReady")} minWidth={760} />
      ) : (
        <Table columns={shippedColumns} rows={shippedRows} rowKey={(r) => r.requestId} emptyMessage={t("emptyShipped")} minWidth={760} />
      )}

      {printing && (
        <div className="ship-print-sheet">
          <h2 style={{ margin: 0 }}>{t("printTitle")}</h2>
          <div style={{ marginTop: 8 }}>{t("printStore")}: {printing.storeName ?? "—"}</div>
          <div>{t("printDate")}: {new Date().toLocaleDateString(intl)}</div>
          {printing.neededBy && <div>{t("colNeededBy")}: {fmtDate(printing.neededBy, intl)}</div>}
          {/* eslint-disable-next-line no-restricted-syntax -- print sheet: plain table styled by PRINT_CSS */}
          <table>
            <thead>
              <tr>
                <th>{t("printItem")}</th>
                <th>{t("printQty")}</th>
                <th>{t("printUnit")}</th>
              </tr>
            </thead>
            <tbody>
              {(printing.lines ?? []).map((l, i) => (
                <tr key={i}>
                  <td>{l.itemName ?? "—"}</td>
                  <td>{fmtQty(l.qty, intl)}</td>
                  <td>{l.unit ?? ""}</td>
                </tr>
              ))}
            </tbody>
          </table>
          <div className="sign">
            <span>{t("printSenderSign")}</span>
            <span>{t("printReceiverSign")}</span>
          </div>
        </div>
      )}
    </PageContainer>
  );
}
