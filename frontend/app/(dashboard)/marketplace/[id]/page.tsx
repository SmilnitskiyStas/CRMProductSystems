"use client";

import { useState } from "react";
import Link from "next/link";
import { ChevronLeft, MessageCircle } from "lucide-react";
import { useParams } from "next/navigation";
import { useSupplier, useSupplierReviewCount } from "@/features/marketplace/hooks/useMarketplace";
import { reviewWord } from "@/features/marketplace/utils";
import { SupplierMetrics } from "@/features/marketplace/components/SupplierMetrics";
import { SupplierItemsTab } from "@/features/marketplace/components/SupplierItemsTab";
import { SupplierReviewsTab } from "@/features/marketplace/components/SupplierReviewsTab";
import { AddSupplierItemModal } from "@/features/marketplace/components/AddSupplierItemModal";
import { SupplierChatPanel } from "@/features/marketplace/components/SupplierChatPanel";
import { PlanBadge } from "@/features/marketplace/components/PlanBadge";
import { StarRating } from "@/features/marketplace/components/StarRating";
import { useMe } from "@/features/auth/hooks/useAuth";
import { PROVIDER_TEAM } from "@/lib/roles";
import { Btn } from "@/components/ui/Btn";

type ActiveTab = "catalog" | "reviews";

export default function SupplierProfilePage() {
  const { id } = useParams<{ id: string }>();
  const [activeTab, setActiveTab] = useState<ActiveTab>("catalog");
  const [addItemModalOpen, setAddItemModalOpen] = useState(false);
  const [chatOpen, setChatOpen] = useState(false);

  const { data: supplier, isLoading, isError } = useSupplier(id);
  const { data: reviewCount } = useSupplierReviewCount(id);
  const { data: me } = useMe();
  const isProviderTeam = PROVIDER_TEAM.has(me?.role as any);

  if (isLoading) {
    return (
      <div style={{ padding: "28px 32px" }}>
        <div
          style={{
            height: 120,
            background: "#111827",
            borderRadius: 12,
            marginBottom: 20,
          }}
        />
        <div
          style={{
            display: "grid",
            gridTemplateColumns: "repeat(auto-fill, minmax(150px, 1fr))",
            gap: 12,
          }}
        >
          {[...Array(6)].map((_, i) => (
            <div
              key={i}
              style={{ height: 80, background: "#111827", borderRadius: 10 }}
            />
          ))}
        </div>
      </div>
    );
  }

  if (isError || !supplier) {
    return (
      <div style={{ padding: "28px 32px" }}>
        <div style={{ color: "#F87171", fontSize: 14 }}>
          Постачальника не знайдено або виникла помилка завантаження.
        </div>
        <Link
          href="/marketplace"
          style={{ color: "#3B82F6", fontSize: 13, display: "inline-flex", alignItems: "center", gap: 4, marginTop: 16 }}
        >
          <ChevronLeft size={14} /> Назад до маркетплейсу
        </Link>
      </div>
    );
  }

  const tabStyle = (tab: ActiveTab): React.CSSProperties => ({
    padding: "10px 20px",
    background: "transparent",
    border: "none",
    borderBottom: activeTab === tab ? "2px solid #3B82F6" : "2px solid transparent",
    color: activeTab === tab ? "#3B82F6" : "#6B7280",
    fontSize: 13,
    fontWeight: activeTab === tab ? 600 : 400,
    cursor: "pointer",
    marginBottom: -1,
    transition: "color 0.15s",
  });

  return (
    <div style={{ padding: "28px 32px" }}>
      {/* Back link */}
      <Link
        href="/marketplace"
        style={{
          display: "inline-flex",
          alignItems: "center",
          gap: 4,
          color: "#4B5563",
          fontSize: 13,
          textDecoration: "none",
          marginBottom: 20,
        }}
      >
        <ChevronLeft size={14} />
        Маркетплейс
      </Link>

      {/* Header card */}
      <div
        style={{
          background: "#111827",
          border: "1px solid #1F2937",
          borderRadius: 12,
          padding: "24px 28px",
          marginBottom: 24,
          display: "flex",
          alignItems: "flex-start",
          justifyContent: "space-between",
          gap: 20,
          flexWrap: "wrap",
        }}
      >
        <div style={{ flex: 1, minWidth: 0 }}>
          <div style={{ display: "flex", alignItems: "center", gap: 12, flexWrap: "wrap", marginBottom: 8 }}>
            <h1 style={{ color: "#E8EDF5", fontSize: 20, fontWeight: 700, margin: 0 }}>
              {supplier.supplierName}
            </h1>
            <PlanBadge plan={supplier.plan} />
            <Btn
              size="sm"
              icon={<MessageCircle size={13} />}
              onClick={() => setChatOpen(true)}
            >
              Написати постачальнику
            </Btn>
          </div>
          <div style={{ color: "#6B7280", fontSize: 13, marginBottom: 10 }}>
            {supplier.region}
          </div>
          <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
            <StarRating value={supplier.metrics?.rating ?? 0} size={15} />
            <span style={{ color: "#9CA3AF", fontSize: 13 }}>
              {supplier.metrics?.rating != null
                ? Number(supplier.metrics.rating).toFixed(1)
                : "Без оцінки"}
            </span>
            {reviewCount != null && (
              <span style={{ color: "#4B5563", fontSize: 13 }}>
                · {reviewCount} {reviewWord(reviewCount)}
              </span>
            )}
          </div>

          {/* Premium fields */}
          {supplier.plan === "premium" && (
            <div
              style={{
                marginTop: 16,
                display: "flex",
                flexDirection: "column",
                gap: 6,
              }}
            >
              {supplier.website && (
                <div style={{ display: "flex", gap: 8, alignItems: "center" }}>
                  <span style={{ color: "#4B5563", fontSize: 12 }}>Сайт:</span>
                  <a
                    href={supplier.website}
                    target="_blank"
                    rel="noopener noreferrer"
                    style={{ color: "#3B82F6", fontSize: 13 }}
                  >
                    {supplier.website}
                  </a>
                </div>
              )}
              {supplier.workingHours && (
                <div style={{ display: "flex", gap: 8 }}>
                  <span style={{ color: "#4B5563", fontSize: 12 }}>Графік:</span>
                  <span style={{ color: "#9CA3AF", fontSize: 13 }}>{supplier.workingHours}</span>
                </div>
              )}
              {supplier.paymentTerms && (
                <div style={{ display: "flex", gap: 8 }}>
                  <span style={{ color: "#4B5563", fontSize: 12 }}>Оплата:</span>
                  <span style={{ color: "#9CA3AF", fontSize: 13 }}>{supplier.paymentTerms}</span>
                </div>
              )}
              {(supplier.deliveryRegions ?? []).length > 0 && (
                <div style={{ display: "flex", gap: 8, flexWrap: "wrap", alignItems: "center" }}>
                  <span style={{ color: "#4B5563", fontSize: 12 }}>Доставка:</span>
                  {(supplier.deliveryRegions ?? []).map((r) => (
                    <span
                      key={r}
                      style={{
                        padding: "2px 8px",
                        background: "#1F2937",
                        borderRadius: 4,
                        color: "#9CA3AF",
                        fontSize: 11,
                      }}
                    >
                      {r}
                    </span>
                  ))}
                </div>
              )}
            </div>
          )}
        </div>

        {/* Categories */}
        {(supplier.categories ?? []).length > 0 && (
          <div style={{ display: "flex", flexWrap: "wrap", gap: 6, alignItems: "flex-start" }}>
            {(supplier.categories ?? []).map((cat) => (
              <span
                key={cat}
                style={{
                  padding: "4px 10px",
                  background: "#1F2937",
                  borderRadius: 6,
                  color: "#9CA3AF",
                  fontSize: 12,
                }}
              >
                {cat}
              </span>
            ))}
          </div>
        )}
      </div>

      {/* Metrics */}
      <div style={{ marginBottom: 28 }}>
        <h2 style={{ color: "#E8EDF5", fontSize: 15, fontWeight: 600, margin: "0 0 14px" }}>
          Показники роботи
        </h2>
        <SupplierMetrics metrics={supplier.metrics} />
      </div>

      {/* Tabs */}
      <div
        style={{
          borderBottom: "1px solid #1F2937",
          marginBottom: 24,
          display: "flex",
          alignItems: "center",
          justifyContent: "space-between",
        }}
      >
        <div style={{ display: "flex" }}>
          <button style={tabStyle("catalog")} onClick={() => setActiveTab("catalog")}>
            Каталог
          </button>
          <button style={tabStyle("reviews")} onClick={() => setActiveTab("reviews")}>
            Відгуки
          </button>
        </div>
        {isProviderTeam && activeTab === "catalog" && (
          <button
            onClick={() => setAddItemModalOpen(true)}
            style={{
              padding: "7px 16px",
              borderRadius: 8,
              border: "none",
              background: "#1D4ED8",
              color: "#E8EDF5",
              fontSize: 12,
              fontWeight: 600,
              cursor: "pointer",
              marginBottom: 1,
            }}
          >
            + Додати товар
          </button>
        )}
      </div>

      {/* Tab content */}
      <div
        style={{
          background: "#0D1117",
          border: "1px solid #1F2937",
          borderRadius: 12,
          padding: 24,
        }}
      >
        {activeTab === "catalog" && <SupplierItemsTab supplierId={id} />}
        {activeTab === "reviews" && <SupplierReviewsTab supplierId={id} />}
      </div>

      {addItemModalOpen && (
        <AddSupplierItemModal
          supplierId={id}
          onClose={() => setAddItemModalOpen(false)}
        />
      )}

      {chatOpen && (
        <SupplierChatPanel
          supplierId={id}
          supplierName={supplier.supplierName}
          onClose={() => setChatOpen(false)}
        />
      )}
    </div>
  );
}
