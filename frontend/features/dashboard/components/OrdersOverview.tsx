"use client";

import Link from "next/link";
import { useTranslations, useLocale } from "next-intl";
import { AlertTriangle, ArrowRight, PackageCheck, Truck } from "lucide-react";
import { useOrderTracking, useOrderTrackingSummary } from "@/features/order-tracking/hooks/useOrderTracking";
import { usePrimaryStoreId } from "@/lib/useStoreContext";

const card: React.CSSProperties = {
  background: "#0D1117",
  border: "1px solid #1F2937",
  borderRadius: 12,
  padding: "16px 18px",
};

function Tile({
  href, icon, label, count, quantity, color, unitsLabel,
}: {
  href: string; icon: React.ReactNode; label: string; count: number | undefined; quantity: number | undefined;
  color: string; unitsLabel: string;
}) {
  return (
    <Link
      href={href}
      style={{
        flex: "1 1 180px", textDecoration: "none", background: "#0A0F1A", border: `1px solid ${color}33`,
        borderRadius: 10, padding: "12px 14px", display: "flex", flexDirection: "column", gap: 4,
      }}
    >
      <span style={{ display: "flex", alignItems: "center", gap: 6, color: "#6B7280", fontSize: 11, textTransform: "uppercase", letterSpacing: "0.05em" }}>
        <span style={{ color }}>{icon}</span>{label}
      </span>
      <span style={{ color, fontSize: 24, fontWeight: 700, fontFamily: "monospace", lineHeight: 1.1 }}>{count ?? "—"}</span>
      <span style={{ color: "#4B5563", fontSize: 11 }}>
        {quantity != null ? `${quantity.toLocaleString(undefined, { maximumFractionDigits: 0 })} ${unitsLabel}` : " "}
      </span>
    </Link>
  );
}

/**
 * Dashboard block: how much is on order, how much is on the way and which deliveries are late.
 * Every tile and delayed order links to the order-tracking page (pre-filtered) for the details.
 */
export function OrdersOverview() {
  const t = useTranslations("Dashboard.dashboard.orders");
  const locale = useLocale();
  const intl = locale === "en" ? "en-US" : "uk-UA";
  const storeId = usePrimaryStoreId();

  const { data: summary } = useOrderTrackingSummary(storeId);
  const { data: delayed } = useOrderTracking({ statuses: [], storeId, overdue: true, page: 1, pageSize: 5 });

  const ordered = summary ? summary.new + summary.confirmed : undefined;
  const orderedQty = summary ? summary.openQuantity - summary.inTransitQuantity : undefined;

  return (
    <div style={card}>
      <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginBottom: 12, gap: 10 }}>
        <h2 style={{ color: "#E8EDF5", fontSize: 15, fontWeight: 700, margin: 0 }}>{t("title")}</h2>
        <Link href="/orders/tracking" style={{ color: "#60A5FA", fontSize: 13, textDecoration: "none", display: "flex", alignItems: "center", gap: 4 }}>
          {t("viewAll")} <ArrowRight size={14} />
        </Link>
      </div>

      <div style={{ display: "flex", gap: 12, flexWrap: "wrap", marginBottom: delayed && delayed.items.length > 0 ? 14 : 0 }}>
        <Tile
          href="/orders/tracking?status=new,confirmed"
          icon={<PackageCheck size={13} />}
          label={t("ordered")}
          count={ordered}
          quantity={orderedQty}
          color="#FBBF24"
          unitsLabel={t("units")}
        />
        <Tile
          href="/orders/tracking?status=in_transit"
          icon={<Truck size={13} />}
          label={t("inTransit")}
          count={summary?.inTransit}
          quantity={summary?.inTransitQuantity}
          color="#34D399"
          unitsLabel={t("units")}
        />
        <Tile
          href="/orders/tracking?overdue=1"
          icon={<AlertTriangle size={13} />}
          label={t("delayed")}
          count={summary?.overdue}
          quantity={summary?.overdueQuantity}
          color={summary && summary.overdue > 0 ? "#F87171" : "#4B5563"}
          unitsLabel={t("units")}
        />
      </div>

      {delayed && delayed.items.length > 0 && (
        <div>
          <div style={{ color: "#6B7280", fontSize: 11, textTransform: "uppercase", letterSpacing: "0.05em", marginBottom: 6 }}>
            {t("delayedList")}
          </div>
          <div style={{ display: "flex", flexDirection: "column", gap: 4 }}>
            {delayed.items.map((o) => (
              <Link
                key={`${o.source}:${o.id}`}
                href="/orders/tracking?overdue=1"
                style={{
                  display: "flex", alignItems: "center", gap: 12, textDecoration: "none", background: "#0A0F1A",
                  border: "1px solid #1F2937", borderRadius: 8, padding: "8px 12px", fontSize: 12,
                }}
              >
                <span style={{ color: "#E8EDF5", fontWeight: 600, flex: 1, minWidth: 0, overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>
                  {o.counterparty ?? t("noSupplier")}
                  {o.number && <span style={{ color: "#4B5563", fontFamily: "monospace", fontWeight: 400 }}> · {o.number}</span>}
                </span>
                <span style={{ color: "#6B7280", whiteSpace: "nowrap" }}>{o.storeName ?? ""}</span>
                <span style={{ color: "#9CA3AF", fontFamily: "monospace", whiteSpace: "nowrap" }}>
                  {o.totalQuantity.toLocaleString(intl, { maximumFractionDigits: 0 })} {t("units")}
                </span>
                <span style={{ color: "#F87171", fontWeight: 600, whiteSpace: "nowrap" }}>
                  {t("overdueDays", { days: o.daysOverdue ?? 0 })}
                </span>
              </Link>
            ))}
          </div>
        </div>
      )}

      {summary && summary.open === 0 && (
        <p style={{ color: "#4B5563", fontSize: 12, margin: "10px 0 0" }}>{t("none")}</p>
      )}
    </div>
  );
}
