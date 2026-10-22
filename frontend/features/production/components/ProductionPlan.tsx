"use client";

import { useMemo, useState } from "react";
import Link from "next/link";
import { useTranslations } from "next-intl";
import { useQueryClient } from "@tanstack/react-query";
import { Btn } from "@/components/ui/Btn";
import { PageContainer } from "@/components/ui/PageContainer";
import { Tabs } from "@/components/ui/Tabs";
import { filterInputStyle, inputStyle as formInputStyle, labelStyle } from "@/components/ui/fieldStyles";
import { Modal } from "@/components/ui/Modal";
import { Table, type TableColumn } from "@/components/ui/Table";
import { receiptsApi } from "@/features/receipts/api/receipts";
import { useProductionPlan, useProductionLocationList } from "../hooks/useProductionInsights";
import type { PlanGranularity, ProductionPlanDto } from "../insights-types";
import {
  PageHeader,
  ProductionLocationSelect,
  StateBox,
  Pill,
  cardStyle,
  mutedText,
  fmtQty,
  useIntlLocale,
} from "./InsightsShared";

type Product = NonNullable<ProductionPlanDto["products"]>[number];
type Ingredient = NonNullable<ProductionPlanDto["ingredients"]>[number];

function isoDate(d: Date) {
  const m = String(d.getMonth() + 1).padStart(2, "0");
  const day = String(d.getDate()).padStart(2, "0");
  return `${d.getFullYear()}-${m}-${day}`;
}

function defaultRange() {
  const from = new Date();
  const to = new Date();
  to.setDate(to.getDate() + 14);
  return { from: isoDate(from), to: isoDate(to) };
}

const GRANULARITIES: PlanGranularity[] = ["day", "week", "month"];

export function ProductionPlan() {
  const t = useTranslations("Dashboard.production.plan");
  const intl = useIntlLocale();
  const qc = useQueryClient();
  const init = useMemo(defaultRange, []);
  const [from, setFrom] = useState(init.from);
  const [to, setTo] = useState(init.to);
  const [granularity, setGranularity] = useState<PlanGranularity>("day");
  const [locationId, setLocationId] = useState("");
  const [tab, setTab] = useState<"products" | "ingredients">("products");

  const { data, isLoading, isError } = useProductionPlan({
    from,
    to,
    granularity,
    locationId: locationId || undefined,
  });
  const { list: prodLocations } = useProductionLocationList();

  // ── Purchase creation ──────────────────────────────────────────────────────
  const [showConfirm, setShowConfirm] = useState(false);
  const [destId, setDestId] = useState("");
  const [creating, setCreating] = useState(false);
  const [resultMsg, setResultMsg] = useState<{ ok: boolean; text: string } | null>(null);

  const missingIngredients = useMemo(
    () => (data?.ingredients ?? []).filter((i) => (i.missing ?? 0) > 0),
    [data]
  );
  const groups = useMemo(() => {
    const map = new Map<string, { supplierId: string | null; name: string | null; items: Ingredient[] }>();
    for (const i of missingIngredients) {
      const key = i.defaultSupplierId ?? "__own__";
      const g = map.get(key) ?? { supplierId: i.defaultSupplierId ?? null, name: i.defaultSupplierName ?? null, items: [] };
      g.items.push(i);
      map.set(key, g);
    }
    return Array.from(map.values());
  }, [missingIngredients]);

  const effectiveDest = locationId || (prodLocations.length === 1 ? prodLocations[0].id : destId);

  function openConfirm() {
    setResultMsg(null);
    setDestId(locationId || (prodLocations.length === 1 ? prodLocations[0].id : ""));
    setShowConfirm(true);
  }

  async function createPurchases() {
    if (!effectiveDest) return;
    setCreating(true);
    let created = 0;
    const errors: string[] = [];
    for (const g of groups) {
      try {
        await receiptsApi.create({
          supplierId: g.supplierId,
          destinationStoreId: effectiveDest,
          viaCentralStore: false,
          expectedAt: null,
          notes: t("purchaseNotes"),
          items: g.items.map((i) => ({
            productId: i.itemId,
            quantityOrdered: i.missing ?? 0,
            pricePurchase: null,
          })),
        });
        created++;
      } catch (e) {
        errors.push(`${g.name ?? t("ownGroup")}: ${e instanceof Error ? e.message : t("unknownError")}`);
      }
    }
    setCreating(false);
    setShowConfirm(false);
    if (created > 0) qc.invalidateQueries({ queryKey: ["receipts"] });
    setResultMsg({
      ok: errors.length === 0,
      text:
        t("purchaseResult", { created, total: groups.length }) + (errors.length ? ` — ${errors.join("; ")}` : ""),
    });
  }

  // ── Product table columns ──────────────────────────────────────────────────
  const productColumns: TableColumn<Product>[] = useMemo(() => {
    const cols: TableColumn<Product>[] = [
      {
        key: "name",
        header: t("colProduct"),
        render: (p) => (
          <span>
            {p.itemName ?? "—"}
            {p.hasRecipe === false && (
              <span style={{ marginLeft: 8 }}>
                <Pill color="#FBBF24" bg="#3a2a05">{t("noRecipe")}</Pill>
              </span>
            )}
          </span>
        ),
      },
    ];
    for (const b of data?.buckets ?? []) {
      cols.push({
        key: `b-${b.key}`,
        header: b.label,
        render: (p) => {
          const x = p.perBucket?.find((pb) => pb.bucketKey === b.key);
          if (!x || (!x.requestedQty && !x.pendingQty && !x.forecastQty)) return <span style={{ color: "#374151" }}>—</span>;
          return (
            <span title={t("bucketHint")}>
              {fmtQty(x.requestedQty ?? 0, intl)} · +{fmtQty(x.pendingQty ?? 0, intl)} · ~{fmtQty(x.forecastQty ?? 0, intl)}
            </span>
          );
        },
      });
    }
    cols.push(
      {
        key: "totals",
        header: t("colTotals"),
        render: (p) => (
          <span>
            {fmtQty(p.totalRequested ?? 0, intl)} · +{fmtQty(p.totalPending ?? 0, intl)} · ~{fmtQty(p.totalForecast ?? 0, intl)}
          </span>
        ),
      },
      { key: "onHand", header: t("colOnHand"), render: (p) => `${fmtQty(p.finishedOnHand ?? 0, intl)} ${p.unit ?? ""}` },
      { key: "open", header: t("colOpenOrders"), render: (p) => fmtQty(p.openOrderQty ?? 0, intl) },
      {
        key: "toProduce",
        header: t("colToProduce"),
        render: (p) => {
          const v = p.toProduce ?? 0;
          return (
            <span style={{ fontWeight: 700, color: v > 0 ? "#60A5FA" : "#6B7280", background: v > 0 ? "#0c1f3a" : "transparent", padding: "2px 8px", borderRadius: 6 }}>
              {fmtQty(v, intl)} {p.unit ?? ""}
            </span>
          );
        },
      }
    );
    return cols;
  }, [data?.buckets, intl, t]);

  const ingredientColumns: TableColumn<Ingredient>[] = [
    { key: "name", header: t("colIngredient"), render: (i) => i.itemName ?? "—" },
    { key: "req", header: t("colRequired"), render: (i) => `${fmtQty(i.required ?? 0, intl)} ${i.unit ?? ""}` },
    { key: "onHand", header: t("colIngOnHand"), render: (i) => `${fmtQty(i.onHand ?? 0, intl)} ${i.unit ?? ""}` },
    {
      key: "missing",
      header: t("colMissing"),
      render: (i) => {
        const m = i.missing ?? 0;
        return (
          <span style={{ fontWeight: 700, color: m > 0 ? "#F87171" : "#4ADE80" }}>
            {m > 0 ? `${fmtQty(m, intl)} ${i.unit ?? ""}` : "—"}
          </span>
        );
      },
    },
    { key: "supplier", header: t("colSupplier"), render: (i) => i.defaultSupplierName ?? <span style={mutedText}>{t("noSupplier")}</span> },
  ];

  const inputStyle = { ...filterInputStyle, colorScheme: "dark" } as const;

  return (
    <PageContainer>
      <PageHeader
        title={t("title")}
        subtitle={t("subtitle")}
        right={<ProductionLocationSelect value={locationId} onChange={setLocationId} allLabel={t("allLocations")} />}
      />

      <div style={{ ...cardStyle, display: "flex", gap: 14, flexWrap: "wrap", alignItems: "center" }}>
        <div style={{ display: "flex", gap: 6 }}>
          {GRANULARITIES.map((g) => (
            <Btn key={g} size="sm" variant={granularity === g ? "primary" : "ghost"} onClick={() => setGranularity(g)}>
              {t(`gran_${g}`)}
            </Btn>
          ))}
        </div>
        <label style={{ ...mutedText, display: "flex", alignItems: "center", gap: 6 }}>
          {t("from")}
          <input type="date" value={from} max={to} onChange={(e) => setFrom(e.target.value)} style={inputStyle} />
        </label>
        <label style={{ ...mutedText, display: "flex", alignItems: "center", gap: 6 }}>
          {t("to")}
          <input type="date" value={to} min={from} onChange={(e) => setTo(e.target.value)} style={inputStyle} />
        </label>
      </div>

      <Tabs
        items={[
          { key: "products", label: t("tabProducts") },
          { key: "ingredients", label: t("tabIngredients"), count: missingIngredients.length > 0 ? missingIngredients.length : undefined },
        ]}
        value={tab}
        onChange={setTab}
      />

      {resultMsg && (
        <div
          style={{
            padding: "12px 16px",
            borderRadius: 10,
            fontSize: 13,
            background: resultMsg.ok ? "#064E3B" : "#2d0a0a",
            border: `1px solid ${resultMsg.ok ? "#065F46" : "#7F1D1D"}`,
            color: resultMsg.ok ? "#34D399" : "#F87171",
          }}
        >
          {resultMsg.text}{" "}
          <Link href="/receipts" style={{ color: "inherit", fontWeight: 700 }}>
            {t("goToReceipts")} →
          </Link>
        </div>
      )}

      {isLoading ? (
        <StateBox kind="loading">{t("loading")}</StateBox>
      ) : isError ? (
        <StateBox kind="error">{t("loadError")}</StateBox>
      ) : tab === "products" ? (
        <>
          <div style={{ ...mutedText, marginBottom: 8 }}>{t("legend")}</div>
          <Table
            columns={productColumns}
            rows={data?.products ?? []}
            rowKey={(p) => p.itemId}
            emptyMessage={t("emptyProducts")}
            density="compact"
            minWidth={720}
          />
        </>
      ) : (
        <>
          <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: 10 }}>
            <div style={mutedText}>{t("mrpHint")}</div>
            <Btn disabled={missingIngredients.length === 0} onClick={openConfirm}>
              {t("createPurchase")}
            </Btn>
          </div>
          <Table
            columns={ingredientColumns}
            rows={data?.ingredients ?? []}
            rowKey={(i) => i.itemId}
            emptyMessage={t("emptyIngredients")}
          />
        </>
      )}

      {showConfirm && (
        <Modal title={t("confirmTitle")} onClose={() => !creating && setShowConfirm(false)} width={560}>
          <p style={{ color: "#9CA3AF", fontSize: 13, marginTop: 0 }}>{t("confirmBody", { count: groups.length })}</p>
          {!locationId && prodLocations.length > 1 && (
            <label style={{ ...labelStyle, marginBottom: 12 }}>
              {t("destination")}
              <select value={destId} onChange={(e) => setDestId(e.target.value)} style={{ ...formInputStyle, marginTop: 4 }}>
                <option value="">{t("chooseDestination")}</option>
                {prodLocations.map((l) => (
                  <option key={l.id} value={l.id}>{l.name}</option>
                ))}
              </select>
            </label>
          )}
          <div style={{ display: "flex", flexDirection: "column", gap: 10, marginBottom: 16 }}>
            {groups.map((g) => (
              <div key={g.supplierId ?? "own"} style={{ ...cardStyle, padding: 12 }}>
                <div style={{ color: "#E8EDF5", fontWeight: 700, fontSize: 13, marginBottom: 6 }}>
                  {g.supplierId ? g.name ?? t("unknownSupplier") : t("ownGroup")}
                </div>
                {g.items.map((i) => (
                  <div key={i.itemId} style={{ display: "flex", justifyContent: "space-between", fontSize: 12, color: "#9CA3AF" }}>
                    <span>{i.itemName}</span>
                    <span>{fmtQty(i.missing, intl)} {i.unit ?? ""}</span>
                  </div>
                ))}
              </div>
            ))}
          </div>
          <div style={{ display: "flex", justifyContent: "flex-end", gap: 8 }}>
            <Btn variant="ghost" disabled={creating} onClick={() => setShowConfirm(false)}>{t("cancel")}</Btn>
            <Btn disabled={creating || !effectiveDest} onClick={createPurchases}>
              {creating ? t("creating") : t("confirmCreate")}
            </Btn>
          </div>
        </Modal>
      )}
    </PageContainer>
  );
}
