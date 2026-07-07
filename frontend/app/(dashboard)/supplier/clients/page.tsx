"use client";

import { useState } from "react";
import { Users, MessageCircle } from "lucide-react";
import { ClientsTab } from "@/features/supplier-cabinet/components/ClientsTab";
import { ChatInboxTab } from "@/features/supplier-cabinet/components/ChatInboxTab";
import { useMe } from "@/features/auth/hooks/useAuth";
import { SUPPLIER_ONLY, hasRole } from "@/lib/roles";

type PageTab = "clients" | "messages";

export default function SupplierClientsPage() {
  const { data: me } = useMe();
  const [tab, setTab] = useState<PageTab>("clients");

  if (me && !hasRole(me.role, SUPPLIER_ONLY)) {
    return (
      <div style={{ padding: "28px 32px", color: "#F87171", fontSize: 14 }}>
        Доступ лише для адміністраторів постачальника.
      </div>
    );
  }

  // null permissions = full/owner access; a restricted staff role without
  // client_management should not reach this page directly by URL either.
  if (me?.permissions && !me.permissions.client_management) {
    return (
      <div style={{ padding: "28px 32px", color: "#F87171", fontSize: 14 }}>
        Немає доступу до клієнтів.
      </div>
    );
  }

  return (
    <div style={{ padding: "28px 32px" }}>
      <div style={{ marginBottom: 24 }}>
        <h1 style={{ color: "#E8EDF5", fontSize: 22, fontWeight: 700, margin: 0 }}>
          Клієнти та повідомлення
        </h1>
        <p style={{ color: "#4B5563", fontSize: 14, marginTop: 6 }}>
          Клієнти кабінету постачальника — відгуки, завдання, переписка
        </p>
      </div>

      <div style={{ display: "flex", gap: 8, marginBottom: 20 }}>
        <button
          onClick={() => setTab("clients")}
          style={{
            display: "flex",
            alignItems: "center",
            gap: 7,
            padding: "8px 16px",
            borderRadius: 8,
            border: `1px solid ${tab === "clients" ? "#3B82F6" : "#1F2937"}`,
            background: tab === "clients" ? "#1D3461" : "#111827",
            color: tab === "clients" ? "#93C5FD" : "#9CA3AF",
            fontSize: 13,
            fontWeight: 600,
            cursor: "pointer",
          }}
        >
          <Users size={14} />
          Клієнти
        </button>
        <button
          onClick={() => setTab("messages")}
          style={{
            display: "flex",
            alignItems: "center",
            gap: 7,
            padding: "8px 16px",
            borderRadius: 8,
            border: `1px solid ${tab === "messages" ? "#3B82F6" : "#1F2937"}`,
            background: tab === "messages" ? "#1D3461" : "#111827",
            color: tab === "messages" ? "#93C5FD" : "#9CA3AF",
            fontSize: 13,
            fontWeight: 600,
            cursor: "pointer",
          }}
        >
          <MessageCircle size={14} />
          Повідомлення
        </button>
      </div>

      {tab === "clients" ? <ClientsTab /> : <ChatInboxTab />}
    </div>
  );
}
