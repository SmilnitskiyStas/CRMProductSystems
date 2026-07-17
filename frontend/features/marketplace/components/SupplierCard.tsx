"use client";

import Link from "next/link";
import { useTranslations } from "next-intl";
import type { SupplierListItemDto } from "../types";
import { StarRating } from "./StarRating";
import { PlanBadge } from "./PlanBadge";
import { useSupplierReviewCount } from "../hooks/useMarketplace";

interface Props {
  supplier: SupplierListItemDto;
}

export function SupplierCard({ supplier }: Props) {
  const t = useTranslations("Dashboard.marketplace.card");
  const tMarketplace = useTranslations("Dashboard.marketplace");
  const { data: reviewCount } = useSupplierReviewCount(supplier.id);
  const categories = supplier.categories ?? [];

  return (
    <Link
      href={`/marketplace/${supplier.id}`}
      style={{ textDecoration: "none" }}
    >
      <div
        style={{
          background: "#111827",
          border: "1px solid #1F2937",
          borderRadius: 12,
          padding: "20px",
          display: "flex",
          flexDirection: "column",
          gap: 12,
          cursor: "pointer",
          transition: "border-color 0.15s",
        }}
        onMouseEnter={(e) => {
          (e.currentTarget as HTMLElement).style.borderColor = "#3B82F6";
        }}
        onMouseLeave={(e) => {
          (e.currentTarget as HTMLElement).style.borderColor = "#1F2937";
        }}
      >
        {/* Header row */}
        <div style={{ display: "flex", alignItems: "flex-start", justifyContent: "space-between", gap: 8 }}>
          <div style={{ minWidth: 0 }}>
            <div
              style={{
                color: "#E8EDF5",
                fontSize: 15,
                fontWeight: 600,
                marginBottom: 4,
                overflow: "hidden",
                textOverflow: "ellipsis",
                whiteSpace: "nowrap",
              }}
            >
              {supplier.name}
            </div>
            <div style={{ color: "#6B7280", fontSize: 12 }}>{supplier.region}</div>
          </div>
          <PlanBadge plan={supplier.plan} />
        </div>

        {/* Rating row */}
        <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
          <StarRating value={supplier.rating ?? 0} size={14} />
          <span style={{ color: "#9CA3AF", fontSize: 12 }}>
            {supplier.rating != null ? Number(supplier.rating).toFixed(1) : "—"}
          </span>
          {reviewCount != null && (
            <span style={{ color: "#4B5563", fontSize: 12 }}>
              ({tMarketplace("reviewCount", { count: reviewCount })})
            </span>
          )}
          {supplier.avgDeliveryDays != null && (
            <span style={{ color: "#4B5563", fontSize: 12, marginLeft: "auto" }}>
              {t("deliveryDays", { days: supplier.avgDeliveryDays })}
            </span>
          )}
        </div>

        {/* Categories chips */}
        {categories.length > 0 && (
          <div style={{ display: "flex", flexWrap: "wrap", gap: 4 }}>
            {categories.slice(0, 4).map((cat) => (
              <span
                key={cat}
                style={{
                  padding: "2px 8px",
                  background: "#1F2937",
                  borderRadius: 4,
                  color: "#9CA3AF",
                  fontSize: 11,
                }}
              >
                {cat}
              </span>
            ))}
            {categories.length > 4 && (
              <span
                style={{
                  padding: "2px 8px",
                  background: "#1F2937",
                  borderRadius: 4,
                  color: "#4B5563",
                  fontSize: 11,
                }}
              >
                +{categories.length - 4}
              </span>
            )}
          </div>
        )}
      </div>
    </Link>
  );
}
