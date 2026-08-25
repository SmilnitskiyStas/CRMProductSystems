"use client";

import { Suspense, useState } from "react";
import { useSearchParams } from "next/navigation";
import { LifeBuoy, Ticket, Star } from "lucide-react";
import { useTranslations } from "next-intl";
import { useMe } from "@/features/auth/hooks/useAuth";
import { AccessDenied } from "@/components/AccessDenied";
import { AT_LEAST_STORE_MANAGER, hasRole } from "@/lib/roles";
import { TicketList } from "@/features/customer-support/components/TicketList";
import { TicketDetail } from "@/features/customer-support/components/TicketDetail";
import { ReviewList } from "@/features/customer-support/components/ReviewList";
import type { ConsumerSupportTicketDto } from "@/features/customer-support/types";

type PageTab = "tickets" | "reviews";

/**
 * TASK-621 (§4 "Вхідні звернень і відгуків"), staff inbox for consumer support tickets
 * (TASK-616) and purchase reviews (TASK-617). Same role gate as `/customers`
 * (AtLeastStoreManager) — mirrors the `/consumer-app/loyalty-tiers` page-shell pattern
 * (AccessDenied + hasRole) rather than /service-desk's split-by-role rendering, since this page
 * has only one audience (tenant staff), unlike service-desk's tenant+provider split.
 *
 * Supports `?customerId=` (set by CustomerTicketsTab's deep link from the customer drawer) to
 * pre-filter the Tickets tab to one customer. GetInboxAsync (TASK-616) has no customerId filter
 * param, so TicketList does this client-side over a widened page — see its own comment for the
 * exact limitation.
 */
function CustomerSupportPageContent() {
  const t = useTranslations("Dashboard.customerSupport.page");
  const { data: me } = useMe();
  const roleAccess = me ? hasRole(me.role, AT_LEAST_STORE_MANAGER) : null;

  const searchParams = useSearchParams();
  const [customerIdFilter, setCustomerIdFilter] = useState<string | null>(searchParams.get("customerId"));

  const [tab, setTab] = useState<PageTab>("tickets");
  const [selectedTicket, setSelectedTicket] = useState<ConsumerSupportTicketDto | null>(null);

  if (roleAccess === false) {
    return <AccessDenied title={t("title")} />;
  }
  if (roleAccess === null) {
    return null;
  }

  const tabStyle = (active: boolean): React.CSSProperties => ({
    background: "transparent",
    border: "none",
    borderBottom: `2px solid ${active ? "#3B82F6" : "transparent"}`,
    color: active ? "#93C5FD" : "#4B5563",
    fontSize: 13,
    fontWeight: active ? 600 : 400,
    padding: "10px 16px",
    cursor: "pointer",
    transition: "color 0.15s, border-color 0.15s",
    display: "flex",
    alignItems: "center",
    gap: 6,
  });

  return (
    <div style={{ padding: "28px 32px", minHeight: "100vh" }}>
      {/* Header */}
      <div style={{ marginBottom: 24 }}>
        <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
          <LifeBuoy size={22} style={{ color: "#3B82F6" }} />
          <h1 style={{ color: "#E8EDF5", fontSize: 22, fontWeight: 700, margin: 0 }}>
            {t("title")}
          </h1>
        </div>
        <p style={{ color: "#4B5563", fontSize: 13, marginTop: 6, marginBottom: 0 }}>
          {t("subtitle")}
        </p>
      </div>

      {/* Tabs */}
      <div style={{ borderBottom: "1px solid #1F2937", marginBottom: 20, display: "flex", gap: 4 }}>
        <button style={tabStyle(tab === "tickets")} onClick={() => setTab("tickets")}>
          <Ticket size={14} />
          {t("tabTickets")}
        </button>
        <button style={tabStyle(tab === "reviews")} onClick={() => setTab("reviews")}>
          <Star size={14} />
          {t("tabReviews")}
        </button>
      </div>

      {/* Content */}
      {tab === "tickets" ? (
        <TicketList
          selectedId={selectedTicket?.id}
          onSelect={setSelectedTicket}
          customerIdFilter={customerIdFilter ?? undefined}
          onClearCustomerFilter={() => setCustomerIdFilter(null)}
        />
      ) : (
        <ReviewList />
      )}

      {/* Ticket detail sheet */}
      {selectedTicket && (
        <TicketDetail ticketId={selectedTicket.id} onClose={() => setSelectedTicket(null)} />
      )}
    </div>
  );
}

export default function CustomerSupportPage() {
  return (
    <Suspense>
      <CustomerSupportPageContent />
    </Suspense>
  );
}
