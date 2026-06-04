"use client";

import { Bell } from "lucide-react";
import { useMe } from "@/features/auth/hooks/useAuth";

interface Props {
  title?: string;
}

export function TopBar({ title }: Props) {
  const { data: user } = useMe();

  const storeName = user?.storeId ? "Магазин #1" : "ShelfGuard";
  const initials = user?.fullName
    ? user.fullName
        .split(" ")
        .slice(0, 2)
        .map((n) => n[0])
        .join("")
        .toUpperCase()
    : "?";

  return (
    <header
      style={{
        height: 56,
        background: "#0D1117",
        borderBottom: "1px solid #1F2937",
        display: "flex",
        alignItems: "center",
        justifyContent: "space-between",
        padding: "0 24px",
        flexShrink: 0,
        position: "sticky",
        top: 0,
        zIndex: 10,
      }}
    >
      <div style={{ display: "flex", alignItems: "center", gap: 16 }}>
        <span style={{ color: "#E8EDF5", fontSize: 15, fontWeight: 600 }}>{storeName}</span>
        {title && (
          <>
            <span style={{ color: "#374151", fontSize: 14 }}>/</span>
            <span style={{ color: "#6B7280", fontSize: 14 }}>{title}</span>
          </>
        )}
      </div>

      <div style={{ display: "flex", alignItems: "center", gap: 16 }}>
        {/* Notification bell */}
        <button
          style={{
            background: "transparent",
            border: "1px solid #1F2937",
            borderRadius: 8,
            padding: "6px 8px",
            cursor: "pointer",
            color: "#6B7280",
            display: "flex",
            alignItems: "center",
            position: "relative",
          }}
        >
          <Bell size={16} />
          <span
            style={{
              position: "absolute",
              top: 4,
              right: 4,
              width: 6,
              height: 6,
              background: "#EF4444",
              borderRadius: "50%",
            }}
          />
        </button>

        {/* User avatar */}
        <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
          <div
            style={{
              width: 32,
              height: 32,
              borderRadius: "50%",
              background: "linear-gradient(135deg, #3B82F6, #6366F1)",
              display: "flex",
              alignItems: "center",
              justifyContent: "center",
              color: "#fff",
              fontSize: 12,
              fontWeight: 700,
              flexShrink: 0,
            }}
          >
            {initials}
          </div>
          <div>
            <div style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 500 }}>
              {user?.fullName ?? "…"}
            </div>
            <div style={{ color: "#4B5563", fontSize: 11 }}>{user?.role ?? ""}</div>
          </div>
        </div>
      </div>
    </header>
  );
}
