"use client";

import { useState } from "react";
import { Users, MessageCircle, Star } from "lucide-react";
import { useSupplierClients } from "../hooks/useSupplierCabinet";
import { SupplierClientChatPanel } from "./SupplierClientChatPanel";
import { Btn } from "@/components/ui/Btn";

export function ClientsTab() {
  const { data: clients, isLoading, isError } = useSupplierClients();
  const [chatClient, setChatClient] = useState<{ tenantId: string; tenantName: string } | null>(null);

  return (
    <div
      style={{
        background: "#111827",
        border: "1px solid #1F2937",
        borderRadius: 12,
        padding: "20px 24px",
      }}
    >
      <div style={{ display: "flex", alignItems: "center", gap: 8, marginBottom: 16 }}>
        <Users size={18} color="#60A5FA" />
        <h2 style={{ color: "#E8EDF5", fontSize: 16, fontWeight: 600, margin: 0 }}>
          Клієнти
        </h2>
        {clients && (
          <span
            style={{
              padding: "2px 8px",
              borderRadius: 10,
              background: "#1F2937",
              color: "#9CA3AF",
              fontSize: 11,
            }}
          >
            {clients.length}
          </span>
        )}
      </div>

      {isLoading && <div style={{ color: "#4B5563", fontSize: 13 }}>Завантаження…</div>}
      {isError && <div style={{ color: "#F87171", fontSize: 13 }}>Не вдалося завантажити список клієнтів.</div>}

      {!isLoading && !isError && (
        <div style={{ display: "flex", flexDirection: "column", gap: 8 }}>
          {clients?.map((client) => (
            <div
              key={client.tenantId}
              style={{
                background: "#0D1117",
                border: "1px solid #1F2937",
                borderRadius: 10,
                padding: "12px 16px",
                display: "flex",
                alignItems: "center",
                gap: 12,
              }}
            >
              <div style={{ flex: 1, minWidth: 0 }}>
                <div style={{ color: "#E8EDF5", fontSize: 14, fontWeight: 600, marginBottom: 4 }}>
                  {client.tenantName}
                </div>
                <div style={{ display: "flex", gap: 14, flexWrap: "wrap", color: "#6B7280", fontSize: 12 }}>
                  <span style={{ display: "flex", alignItems: "center", gap: 4 }}>
                    <Star size={12} color="#FBBF24" />
                    {client.avgRating != null ? client.avgRating.toFixed(1) : "—"}
                    {" "}({client.reviewCount})
                  </span>
                  <span>Завдань: {client.taskCount}</span>
                  <span>Остання взаємодія: {new Date(client.lastInteractionAt).toLocaleDateString("uk-UA")}</span>
                </div>
              </div>

              <Btn
                size="sm"
                icon={<MessageCircle size={13} />}
                onClick={() => setChatClient({ tenantId: client.tenantId, tenantName: client.tenantName })}
              >
                Написати
              </Btn>
            </div>
          ))}

          {(!clients || clients.length === 0) && (
            <div style={{ color: "#4B5563", fontSize: 13, textAlign: "center", padding: "24px 0" }}>
              Клієнтів ще немає.
            </div>
          )}
        </div>
      )}

      {chatClient && (
        <SupplierClientChatPanel
          clientTenantId={chatClient.tenantId}
          clientTenantName={chatClient.tenantName}
          onClose={() => setChatClient(null)}
        />
      )}
    </div>
  );
}
