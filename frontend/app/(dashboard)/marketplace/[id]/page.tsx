"use client";

import { useState } from "react";
import Link from "next/link";
import { ChevronLeft, Eye, HeartHandshake, LifeBuoy, MessageCircle } from "lucide-react";
import { useParams } from "next/navigation";
import { toast } from "sonner";
import {
  useSupplier,
  useSupplierReviewCount,
  useSupplierChatMessages,
} from "@/features/marketplace/hooks/useMarketplace";
import { useMyCooperation } from "@/features/marketplace/hooks/useCooperation";
import { marketplaceApi } from "@/features/marketplace/api/marketplace-api";
import { reviewWord } from "@/features/marketplace/utils";
import { SupplierMetrics } from "@/features/marketplace/components/SupplierMetrics";
import { SupplierItemsTab } from "@/features/marketplace/components/SupplierItemsTab";
import { SupplierReviewsTab } from "@/features/marketplace/components/SupplierReviewsTab";
import { AddSupplierItemModal } from "@/features/marketplace/components/AddSupplierItemModal";
import { SupplierChatPanel } from "@/features/marketplace/components/SupplierChatPanel";
import { PlanBadge } from "@/features/marketplace/components/PlanBadge";
import { StarRating } from "@/features/marketplace/components/StarRating";
import { AgreementStatusBadge } from "@/features/marketplace/components/CooperationBadges";
import { CooperationRequestModal } from "@/features/marketplace/components/CooperationRequestModal";
import { SupplierOrderCart, type CartLine } from "@/features/marketplace/components/SupplierOrderCart";
import { SupportTicketsPanel } from "@/features/marketplace/components/SupportTicketsPanel";
import { useMe } from "@/features/auth/hooks/useAuth";
import { PROVIDER_TEAM, TENANT_ROLES, type AppRole } from "@/lib/roles";
import { Btn } from "@/components/ui/Btn";
import type { SupplierItemDto } from "@/features/marketplace/types";

type ActiveTab = "catalog" | "reviews";

export default function SupplierProfilePage() {
  const { id } = useParams<{ id: string }>();
  const [activeTab, setActiveTab] = useState<ActiveTab>("catalog");
  const [addItemModalOpen, setAddItemModalOpen] = useState(false);
  const [chatOpen, setChatOpen] = useState(false);
  const [cooperationModalOpen, setCooperationModalOpen] = useState(false);
  const [supportOpen, setSupportOpen] = useState(false);
  const [cart, setCart] = useState<CartLine[]>([]);

  const { data: supplier, isLoading, isError } = useSupplier(id);
  const { data: reviewCount } = useSupplierReviewCount(id);
  const { data: me } = useMe();
  const isProviderTeam = PROVIDER_TEAM.has(me?.role as any);
  // Cooperation/orders/support — тільки клієнтські tenant-ролі: провайдерська
  // команда і supplier_admin (власний кабінет постачальника) не подають заявки.
  const isClientTenant = Boolean(me && TENANT_ROLES.has(me.role as AppRole));

  // Мої угоди: supplierTenantId ≠ публічний supplierId, тому зіставляємо за
  // назвою постачальника (handoff 317→318). Список — новіші перші, беремо першу.
  const { data: agreements } = useMyCooperation(isClientTenant);
  const agreement = supplier
    ? agreements?.find((a) => a.supplierName === supplier.supplierName)
    : undefined;
  const cooperationActive = agreement?.status === "active";

  // Hoisted to page level (not inside SupplierChatPanel) so the 3s poll keeps
  // running — and the unread badge stays visible — while the chat is closed.
  const { data: chatMessages = [] } = useSupplierChatMessages(isClientTenant ? id : null);
  const unreadChatCount = chatMessages.filter(
    (m) => m.senderTenantId !== me?.tenantId && !m.isRead,
  ).length;

  function handleAddToCart(item: SupplierItemDto, qty: number) {
    setCart((prev) => {
      const existing = prev.find((l) => l.item.id === item.id);
      if (existing) {
        return prev.map((l) => (l.item.id === item.id ? { ...l, qty: l.qty + qty } : l));
      }
      return [...prev, { item, qty }];
    });
    toast.success("Додано до кошика");
  }

  function handleUpdateQty(supplierItemId: string, qty: number) {
    setCart((prev) => prev.map((l) => (l.item.id === supplierItemId ? { ...l, qty } : l)));
  }

  function handleRemoveLine(supplierItemId: string) {
    setCart((prev) => prev.filter((l) => l.item.id !== supplierItemId));
  }

  function handleViewContract() {
    if (!agreement) return;
    marketplaceApi
      .downloadAgreementContract(agreement.id)
      .catch((err) => toast.error(err.message));
  }

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
            {isClientTenant && agreement && (
              <AgreementStatusBadge status={agreement.status} />
            )}
            <span style={{ position: "relative", display: "inline-flex" }}>
              <Btn
                size="sm"
                icon={<MessageCircle size={13} />}
                onClick={() => setChatOpen(true)}
              >
                Написати постачальнику
              </Btn>
              {!chatOpen && unreadChatCount > 0 && (
                <span
                  style={{
                    position: "absolute",
                    top: -6,
                    right: -6,
                    minWidth: 16,
                    height: 16,
                    padding: unreadChatCount > 9 ? "0 4px" : 0,
                    background: "#EF4444",
                    borderRadius: unreadChatCount > 9 ? 8 : "50%",
                    fontSize: 10,
                    fontWeight: 700,
                    color: "#fff",
                    display: "flex",
                    alignItems: "center",
                    justifyContent: "center",
                    lineHeight: 1,
                  }}
                >
                  {unreadChatCount > 9 ? "9+" : unreadChatCount}
                </span>
              )}
            </span>
            {isClientTenant && (
              <Btn
                size="sm"
                variant="ghost"
                icon={<LifeBuoy size={13} />}
                onClick={() => setSupportOpen(true)}
              >
                Служба підтримки
              </Btn>
            )}
            {isClientTenant &&
              (!agreement ||
                agreement.status === "rejected" ||
                agreement.status === "terminated") && (
                <Btn
                  size="sm"
                  variant="success"
                  icon={<HeartHandshake size={13} />}
                  onClick={() => setCooperationModalOpen(true)}
                >
                  Подати заявку на співпрацю
                </Btn>
              )}
            {isClientTenant &&
              agreement &&
              (agreement.status === "awaiting_signature" || agreement.status === "active") &&
              agreement.hasContractFile && (
                <Btn
                  size="sm"
                  variant="ghost"
                  icon={<Eye size={13} />}
                  onClick={handleViewContract}
                >
                  Переглянути договір
                </Btn>
              )}
          </div>
          {isClientTenant && agreement?.status === "awaiting_signature" && (
            <div style={{ color: "#FBBF24", fontSize: 12, marginBottom: 8 }}>
              Підпишіть договір через Вчасно або фізично — постачальник підтвердить
              підписання, після чого відкриються замовлення.
            </div>
          )}
          {isClientTenant &&
            agreement &&
            (agreement.status === "rejected" || agreement.status === "terminated") &&
            agreement.rejectionReason && (
              <div style={{ color: "#F87171", fontSize: 12, marginBottom: 8 }}>
                Причина: {agreement.rejectionReason}
              </div>
            )}
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
        {activeTab === "catalog" && (
          <SupplierItemsTab
            supplierId={id}
            onAddToCart={isClientTenant && cooperationActive ? handleAddToCart : undefined}
          />
        )}
        {activeTab === "reviews" && <SupplierReviewsTab supplierId={id} />}
      </div>

      {isClientTenant && cooperationActive && (
        <SupplierOrderCart
          supplierId={id}
          cart={cart}
          onUpdateQty={handleUpdateQty}
          onRemove={handleRemoveLine}
          onClear={() => setCart([])}
        />
      )}

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

      {cooperationModalOpen && (
        <CooperationRequestModal
          supplierId={id}
          supplierName={supplier.supplierName}
          onClose={() => setCooperationModalOpen(false)}
        />
      )}

      {supportOpen && (
        <SupportTicketsPanel
          supplierId={id}
          supplierName={supplier.supplierName}
          onClose={() => setSupportOpen(false)}
        />
      )}
    </div>
  );
}
