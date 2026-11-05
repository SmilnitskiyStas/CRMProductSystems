"use client";

import { useState } from "react";
import { Plus, Ticket, MessageCircle } from "lucide-react";
import { useTranslations } from "next-intl";
import { useMe } from "@/features/auth/hooks/useAuth";
import { AT_LEAST_STORE_MANAGER, PROVIDER_TEAM } from "@/lib/roles";
import type { AppRole } from "@/lib/roles";
import { TicketList } from "@/features/service-desk/components/TicketList";
import { MyTicketList } from "@/features/service-desk/components/MyTicketList";
import { TicketDetail } from "@/features/service-desk/components/TicketDetail";
import { CreateTicketForm } from "@/features/service-desk/components/CreateTicketForm";
import type { TicketDto } from "@/features/service-desk/types";
import { ProviderSupportTab } from "@/features/provider/components/ProviderSupportTab";
import { ChatSupportTab } from "@/features/provider/components/ChatSupportTab";
import { ClientChatPanel } from "@/features/chat/components/ClientChatPanel";
import { Btn } from "@/components/ui/Btn";
import { PageContainer } from "@/components/ui/PageContainer";
import { PageHeader } from "@/components/ui/PageHeader";
import { Tabs } from "@/components/ui/Tabs";

type TenantTab = "all" | "my" | "chat";
type ProviderTab = "tickets" | "chat";

export default function ServiceDeskPage() {
  const t = useTranslations("Dashboard.serviceDesk.page");
  const { data: me } = useMe();
  const userRole = (me?.role ?? "") as AppRole;
  const isProvider = PROVIDER_TEAM.has(userRole);
  const isManager = AT_LEAST_STORE_MANAGER.has(userRole);

  // All hooks must run unconditionally — split rendering via flags below
  const [providerTab, setProviderTab] = useState<ProviderTab>("tickets");
  const [tenantTab, setTenantTab] = useState<TenantTab>(isManager ? "all" : "my");
  const [selectedTicket, setSelectedTicket] = useState<TicketDto | null>(null);
  const [createOpen, setCreateOpen] = useState(false);

  // ── Provider view ────────────────────────────────────────────────────────────
  if (isProvider) {
    return (
      <PageContainer>
        <PageHeader
          title={
            <span style={{ display: "inline-flex", alignItems: "center", gap: 10 }}>
              <Ticket size={22} style={{ color: "#3B82F6" }} />
              {t("providerTitle")}
            </span>
          }
          subtitle={t("providerSubtitle")}
        />

        {/* Provider tabs */}
        <Tabs<ProviderTab>
          value={providerTab}
          onChange={setProviderTab}
          items={[
            {
              key: "tickets",
              label: (
                <>
                  <Ticket size={14} />
                  {t("tabTickets")}
                </>
              ),
            },
            {
              key: "chat",
              label: (
                <>
                  <MessageCircle size={14} />
                  {t("tabChat")}
                </>
              ),
            },
          ]}
        />

        {providerTab === "tickets" ? <ProviderSupportTab /> : <ChatSupportTab />}
      </PageContainer>
    );
  }

  // ── Tenant view ──────────────────────────────────────────────────────────────
  return (
    <PageContainer>
      <PageHeader
        title={
          <span style={{ display: "inline-flex", alignItems: "center", gap: 10 }}>
            <Ticket size={22} style={{ color: "#3B82F6" }} />
            {t("tenantTitle")}
          </span>
        }
        subtitle={t("tenantSubtitle")}
        actions={
          <Btn icon={<Plus size={15} />} onClick={() => setCreateOpen(true)}>
            {t("newTicketButton")}
          </Btn>
        }
      />

      {/* Tabs */}
      <Tabs<TenantTab>
        value={tenantTab}
        onChange={setTenantTab}
        items={[
          ...(isManager ? [{ key: "all" as const, label: t("tabAllTickets") }] : []),
          { key: "my" as const, label: t("tabMyTickets") },
          {
            key: "chat" as const,
            label: (
              <>
                <MessageCircle size={14} />
                {t("tabChat")}
              </>
            ),
          },
        ]}
      />

      {/* Content */}
      {tenantTab === "chat" ? (
        <ClientChatPanel />
      ) : tenantTab === "all" && isManager ? (
        <TicketList
          selectedId={selectedTicket?.id}
          onSelect={(ticket) => setSelectedTicket(ticket)}
        />
      ) : (
        <MyTicketList
          selectedId={selectedTicket?.id}
          onSelect={(ticket) => setSelectedTicket(ticket)}
        />
      )}

      {/* Detail sheet */}
      {selectedTicket && (
        <TicketDetail
          ticketId={selectedTicket.id}
          userRole={userRole}
          onClose={() => setSelectedTicket(null)}
        />
      )}

      {/* Create dialog */}
      {createOpen && (
        <CreateTicketForm onClose={() => setCreateOpen(false)} />
      )}
    </PageContainer>
  );
}
