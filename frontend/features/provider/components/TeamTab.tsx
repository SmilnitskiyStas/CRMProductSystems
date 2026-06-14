"use client";

import { useState } from "react";
import { UserPlus, Trash2, Users } from "lucide-react";
import { useProviderTeam, useDeactivateMember } from "../hooks/useProviderTeam";
import { InviteProviderMemberModal } from "./InviteProviderMemberModal";

const ROLE_LABELS: Record<string, string> = {
  provider:       "Власник",
  provider_admin: "Адмін",
  provider_agent: "Агент",
};

const ROLE_COLORS: Record<string, string> = {
  provider:       "#A78BFA",
  provider_admin: "#60A5FA",
  provider_agent: "#4ADE80",
};

export function TeamTab() {
  const { data: members, isLoading } = useProviderTeam();
  const deactivate  = useDeactivateMember();
  const [showInvite, setShowInvite] = useState(false);

  return (
    <div
      style={{
        background: "#0D1117",
        border: "1px solid #1F2937",
        borderRadius: 12,
        padding: "20px 24px",
      }}
    >
      <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginBottom: 20 }}>
        <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
          <Users size={18} color="#60A5FA" />
          <h2 style={{ color: "#E8EDF5", fontSize: 16, fontWeight: 600, margin: 0 }}>
            Команда провайдера
          </h2>
        </div>
        <button
          onClick={() => setShowInvite(true)}
          style={{
            display: "flex", alignItems: "center", gap: 6,
            padding: "8px 14px", borderRadius: 8,
            background: "#1D3461", border: "1px solid #3B82F6",
            color: "#93C5FD", fontSize: 13, cursor: "pointer",
          }}
        >
          <UserPlus size={14} />
          Запросити
        </button>
      </div>

      {isLoading ? (
        <div style={{ color: "#4B5563", fontSize: 14 }}>Завантаження…</div>
      ) : !members?.length ? (
        <div style={{ color: "#4B5563", fontSize: 14 }}>Команда порожня</div>
      ) : (
        <div style={{ display: "flex", flexDirection: "column", gap: 8 }}>
          {members.map((m) => (
            <div
              key={m.id}
              style={{
                display: "flex", alignItems: "center", justifyContent: "space-between",
                background: "#111827", border: "1px solid #1F2937",
                borderRadius: 10, padding: "12px 16px",
                opacity: m.isActive ? 1 : 0.5,
              }}
            >
              <div style={{ display: "flex", flexDirection: "column", gap: 2 }}>
                <span style={{ color: "#E8EDF5", fontSize: 14, fontWeight: 500 }}>{m.fullName}</span>
                <span style={{ color: "#4B5563", fontSize: 12 }}>{m.email}</span>
              </div>
              <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
                <span
                  style={{
                    padding: "2px 10px",
                    borderRadius: 20,
                    fontSize: 11,
                    fontWeight: 600,
                    background: `${ROLE_COLORS[m.role] ?? "#6B7280"}22`,
                    border: `1px solid ${ROLE_COLORS[m.role] ?? "#6B7280"}55`,
                    color: ROLE_COLORS[m.role] ?? "#6B7280",
                  }}
                >
                  {ROLE_LABELS[m.role] ?? m.role}
                </span>
                {m.isActive && m.role !== "provider" && (
                  <button
                    onClick={() => {
                      if (confirm(`Деактивувати ${m.fullName}?`)) {
                        deactivate.mutate(m.id);
                      }
                    }}
                    style={{
                      background: "transparent", border: "none",
                      color: "#4B5563", cursor: "pointer", padding: 4,
                    }}
                    title="Деактивувати"
                  >
                    <Trash2 size={15} />
                  </button>
                )}
                {!m.isActive && (
                  <span style={{ color: "#4B5563", fontSize: 11 }}>Неактивний</span>
                )}
              </div>
            </div>
          ))}
        </div>
      )}

      {showInvite && <InviteProviderMemberModal onClose={() => setShowInvite(false)} />}
    </div>
  );
}
