"use client";

import { useTranslations } from "next-intl";
import { ArrowDownRight, ArrowUpRight, Minus } from "lucide-react";
import { Modal } from "@/components/ui/Modal";
import { Btn } from "@/components/ui/Btn";
import { useShelfInsights } from "../hooks/useShelves";
import type { ShelfItemInsightDto } from "../types";
import { resolveScanImageUrl } from "./ShelfScanPanel";

interface Props {
  onClose: () => void;
  onEditProducts: () => void;
  locationId: string;
  zoneId: string;
  shelfId: string;
  sectionLabel: string;
}

const LOW_COVER_DAYS = 3;

function fmt(n: number): string {
  return Number.isInteger(n) ? String(n) : n.toFixed(1);
}

export function ShelfInsightPanel({ onClose, onEditProducts, locationId, zoneId, shelfId, sectionLabel }: Props) {
  const t = useTranslations("Dashboard.locations.shelfInsight");
  const { data, isLoading, isError, error, refetch } = useShelfInsights(locationId, zoneId, shelfId);

  return (
    <Modal title={t("title", { section: sectionLabel })} onClose={onClose} width={760}>
      <div style={{ display: "flex", flexDirection: "column", gap: 12 }}>
        {isLoading && <p style={{ color: "#6B7280", fontSize: 13, margin: 0 }}>{t("loading")}</p>}
        {isError && (
          <div style={{ display: "flex", flexDirection: "column", gap: 8 }}>
            <p style={{ color: "#F87171", fontSize: 13, margin: 0 }}>
              {t("error", { message: error instanceof Error ? error.message : "" })}
            </p>
            <div>
              <Btn onClick={() => refetch()}>{t("retry")}</Btn>
            </div>
          </div>
        )}
        {data && data.length === 0 && (
          <p style={{ color: "#6B7280", fontSize: 13, margin: 0 }}>{t("empty")}</p>
        )}
        {data?.map((it) => <InsightRow key={it.itemId} it={it} />)}
        <div style={{ display: "flex", justifyContent: "flex-end" }}>
          <Btn onClick={onEditProducts}>{t("editProducts")}</Btn>
        </div>
      </div>
    </Modal>
  );
}

function InsightRow({ it }: { it: ShelfItemInsightDto }) {
  const t = useTranslations("Dashboard.locations.shelfInsight");
  const elsewhere = it.storeQty + it.warehouseQty;
  const noShelfButInStock = it.onShelfQty <= 0 && elsewhere > 0;
  const outOfStock = it.onShelfQty <= 0 && elsewhere <= 0;
  const lowCover = it.daysOfCover !== null && it.daysOfCover < LOW_COVER_DAYS;
  const border = noShelfButInStock ? "#854D0E" : outOfStock || lowCover ? "#991B1B" : "#1F2937";

  return (
    <div
      style={{
        background: "#161B26",
        border: `1px solid ${border}`,
        borderRadius: 10,
        padding: 12,
        display: "flex",
        flexDirection: "column",
        gap: 8,
      }}
    >
      <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
        {it.imageUrl && (
          // eslint-disable-next-line @next/next/no-img-element
          <img
            src={resolveScanImageUrl(it.imageUrl)}
            alt=""
            style={{ width: 40, height: 40, objectFit: "cover", borderRadius: 6, background: "#0B0E14" }}
          />
        )}
        <div style={{ flex: 1, minWidth: 0 }}>
          <div
            style={{
              color: "#E8EDF5",
              fontSize: 13,
              fontWeight: 600,
              overflow: "hidden",
              textOverflow: "ellipsis",
              whiteSpace: "nowrap",
            }}
          >
            {it.name}
          </div>
          <div style={{ color: "#6B7280", fontSize: 11 }}>
            {it.barcode ? `${it.barcode} · ` : ""}
            {t("facings", { count: it.expectedFacings })}
          </div>
        </div>
        <Trend pct={it.trendPct} />
      </div>

      {noShelfButInStock && (
        <div
          style={{
            color: "#FCD34D",
            background: "#2A2000",
            border: "1px solid #854D0E",
            borderRadius: 6,
            padding: "4px 8px",
            fontSize: 12,
          }}
        >
          {t("inStockNotOnShelf")}
        </div>
      )}
      {outOfStock && (
        <div
          style={{
            color: "#F87171",
            background: "#2D0F0F",
            border: "1px solid #991B1B",
            borderRadius: 6,
            padding: "4px 8px",
            fontSize: 12,
          }}
        >
          {t("outOfStock")}
        </div>
      )}
      {lowCover && (
        <div style={{ color: "#F87171", fontSize: 12 }}>{t("lowCover", { days: fmt(it.daysOfCover ?? 0) })}</div>
      )}

      <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(110px, 1fr))", gap: 8 }}>
        <Stat label={t("onShelf")} value={fmt(it.onShelfQty)} />
        <Stat label={t("inStore")} value={fmt(it.storeQty)} />
        <Stat label={t("inWarehouse")} value={fmt(it.warehouseQty)} />
        <Stat
          label={t("nearestExpiry")}
          value={it.nearestExpiry ? new Date(it.nearestExpiry).toLocaleDateString("uk-UA") : "—"}
        />
        <Stat label={t("sold7d")} value={fmt(it.sold7d)} />
        <Stat label={t("sold30d")} value={fmt(it.sold30d)} />
        <Stat
          label={t("daysOfCover")}
          value={it.daysOfCover === null ? "—" : fmt(it.daysOfCover)}
          color={lowCover ? "#F87171" : undefined}
        />
      </div>
    </div>
  );
}

function Stat({ label, value, color }: { label: string; value: string; color?: string }) {
  return (
    <div style={{ background: "#0B0E14", border: "1px solid #1F2937", borderRadius: 6, padding: "6px 8px" }}>
      <div style={{ color: "#6B7280", fontSize: 10, textTransform: "uppercase", letterSpacing: "0.04em" }}>
        {label}
      </div>
      <div style={{ color: color ?? "#E8EDF5", fontSize: 14, fontWeight: 600, fontFamily: "monospace" }}>{value}</div>
    </div>
  );
}

function Trend({ pct }: { pct: number | null }) {
  const t = useTranslations("Dashboard.locations.shelfInsight");
  if (pct === null) {
    return (
      <span style={{ color: "#4B5563" }} title={t("trendNone")}>
        <Minus size={14} />
      </span>
    );
  }
  const up = pct > 1;
  const down = pct < -1;
  const color = up ? "#4ADE80" : down ? "#F87171" : "#9CA3AF";
  const Icon = up ? ArrowUpRight : down ? ArrowDownRight : Minus;
  return (
    <span
      style={{ display: "flex", alignItems: "center", gap: 2, color, fontSize: 12, fontWeight: 600 }}
      title={t("trendHint")}
    >
      <Icon size={14} />
      {pct > 0 ? "+" : ""}
      {Math.round(pct)}%
    </span>
  );
}
