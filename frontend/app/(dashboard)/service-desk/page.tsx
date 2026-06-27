"use client";

import { useState } from "react";
import { Plus, Ticket, MessageCircle } from "lucide-react";
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

type TenantTab = "all" | "my" | "chat";
type ProviderTab = "tickets" | "chat";

export default function ServiceDeskPage() {
  const { data: me } = useMe();
  const userRole = (me?.role ?? "") as AppRole;
  const isProvider = PROVIDER_TEAM.has(userRole);
  const isManager = AT_LEAST_STORE_MANAGER.has(userRole);

  // All hooks must run unconditionally — split rendering via flags below
  const [providerTab, setProviderTab] = useState<ProviderTab>("tickets");
  const [tenantTab, setTenantTab] = useState<TenantTab>(isManager ? "all" : "my");
  const [selectedTicket, setSelectedTicket] = useState<TicketDto | null>(null);
  const [createOpen, setCreateOpen] = useState(false);

  const tabStyle = (active: boolean) => ({
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

  // ── Provider view ────────────────────────────────────────────────────────────
  if (isProvider) {
    return (
      <div style={{ padding: "28px 32px" }}>
        <div style={{ marginBottom: 24 }}>
          <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
            <Ticket size={22} style={{ color: "#3B82F6" }} />
            <h1 style={{ color: "#E8EDF5", fontSize: 22, fontWeight: 700, margin: 0 }}>
              Service Desk
            </h1>
          </div>
          <p style={{ color: "#4B5563", fontSize: 13, marginTop: 6, marginBottom: 0 }}>
            Тікети та чати всіх клієнтів платформи
          </p>
        </div>

        {/* Provider tabs */}
        <div
          style={{
            borderBottom: "1px solid #1F2937",
            marginBottom: 20,
            display: "flex",
            gap: 4,
          }}
        >
          <button
            style={tabStyle(providerTab === "tickets")}
            onClick={() => setProviderTab("tickets")}
          >
            <Ticket size={14} />
            Тікети
          </button>
          <button
            style={tabStyle(providerTab === "chat")}
            onClick={() => setProviderTab("chat")}
          >
            <MessageCircle size={14} />
            Чат
          </button>
        </div>

        {providerTab === "tickets" ? <ProviderSupportTab /> : <ChatSupportTab />}
      </div>
    );
  }

  // ── Tenant view ──────────────────────────────────────────────────────────────
  return (
    <div style={{ padding: "28px 32px", minHeight: "100vh" }}>
      {/* Header */}
      <div
        style={{
          display: "flex",
          alignItems: "flex-start",
          justifyContent: "space-between",
          marginBottom: 24,
        }}
      >
        <div>
          <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
            <Ticket size={22} style={{ color: "#3B82F6" }} />
            <h1 style={{ color: "#E8EDF5", fontSize: 22, fontWeight: 700, margin: 0 }}>
              Service Desk
            </h1>
          </div>
          <p style={{ color: "#4B5563", fontSize: 13, marginTop: 6, marginBottom: 0 }}>
            Заявки та запити підтримки
          </p>
        </div>

        <Btn icon={<Plus size={15} />} onClick={() => setCreateOpen(true)}>
          Новий тікет
        </Btn>
      </div>

      {/* Tabs */}
      <div
        style={{
          borderBottom: "1px solid #1F2937",
          marginBottom: 20,
          display: "flex",
          gap: 4,
        }}
      >
        {isManager && (
          <button
            style={tabStyle(tenantTab === "all")}
            onClick={() => setTenantTab("all")}
          >
            Всі тікети
          </button>
        )}
        <button
          style={tabStyle(tenantTab === "my")}
          onClick={() => setTenantTab("my")}
        >
          Мої тікети
        </button>
        <button
          style={tabStyle(tenantTab === "chat")}
          onClick={() => setTenantTab("chat")}
        >
          <MessageCircle size={14} />
          Чат
        </button>
      </div>

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
    </div>
  );
}
