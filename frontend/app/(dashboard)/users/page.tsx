"use client";

import { useState } from "react";
import { UserPlus } from "lucide-react";
import { UsersList } from "@/features/users/components/UsersList";
import { InviteUserModal } from "@/features/users/components/InviteUserModal";
import { useMe } from "@/features/auth/hooks/useAuth";
import { useUsers } from "@/features/users/hooks/useUsers";

export default function UsersPage() {
  const { data: me } = useMe();
  const { data: users } = useUsers();

  const [inviteOpen, setInviteOpen] = useState(false);

  const isAdmin = me?.role === "enterprise_admin" || me?.role === "provider";

  const activeCount   = users?.filter((u) => u.isActive).length  ?? 0;
  const totalCount    = users?.length ?? 0;
  const telegramCount = users?.filter((u) => u.hasTelegram).length ?? 0;

  return (
    <div style={{ padding: "28px 32px" }}>
      {/* Header */}
      <div
        style={{
          display: "flex",
          alignItems: "flex-start",
          justifyContent: "space-between",
          marginBottom: 28,
        }}
      >
        <div>
          <h1 style={{ color: "#E8EDF5", fontSize: 22, fontWeight: 700, margin: 0 }}>
            Персонал
          </h1>
          <p style={{ color: "#4B5563", fontSize: 14, marginTop: 6 }}>
            Управління користувачами та правами доступу
          </p>
        </div>

        {isAdmin && (
          <button
            onClick={() => setInviteOpen(true)}
            style={{
              display: "flex",
              alignItems: "center",
              gap: 8,
              padding: "9px 18px",
              borderRadius: 8,
              background: "#1D3461",
              border: "1px solid #3B82F6",
              color: "#93C5FD",
              fontSize: 13,
              fontWeight: 600,
              cursor: "pointer",
            }}
          >
            <UserPlus size={15} />
            Запросити
          </button>
        )}
      </div>

      {/* Stats row */}
      <div style={{ display: "flex", gap: 12, marginBottom: 24 }}>
        {[
          { label: "Всього",    value: totalCount,    color: "#60A5FA" },
          { label: "Активних",  value: activeCount,   color: "#4ADE80" },
          { label: "Telegram",  value: telegramCount, color: "#38BDF8" },
        ].map((stat) => (
          <div
            key={stat.label}
            style={{
              flex: 1,
              background: "#0D1117",
              border: "1px solid #1F2937",
              borderRadius: 10,
              padding: "14px 18px",
            }}
          >
            <div style={{ color: stat.color, fontSize: 22, fontWeight: 700 }}>
              {stat.value}
            </div>
            <div style={{ color: "#4B5563", fontSize: 12, marginTop: 2 }}>
              {stat.label}
            </div>
          </div>
        ))}
      </div>

      {/* Users table */}
      <UsersList />

      {/* Invite modal */}
      {inviteOpen && <InviteUserModal onClose={() => setInviteOpen(false)} />}
    </div>
  );
}
