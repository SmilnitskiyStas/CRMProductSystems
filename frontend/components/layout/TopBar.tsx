"use client";

import { useState } from "react";
import Link from "next/link";
import { Bell, LifeBuoy } from "lucide-react";
import { useMe } from "@/features/auth/hooks/useAuth";
import { TENANT_ROLES } from "@/lib/roles";
import type { AppRole } from "@/lib/roles";
import { UserMenu } from "./UserMenu";
import { SupportChatWidget } from "./SupportChatWidget";

interface Props {
  title?: string;
}

export function TopBar({ title }: Props) {
  const { data: user } = useMe();
  const [chatOpen, setChatOpen] = useState(false);

  const storeName = user?.storeId ? "Магазин #1" : "ShelfGuard";
  const userRole = (user?.role ?? "") as AppRole;

  return (
    <>
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
        {/* Left — store name / breadcrumb */}
        <div style={{ display: "flex", alignItems: "center", gap: 16 }}>
          <span style={{ color: "#E8EDF5", fontSize: 15, fontWeight: 600 }}>{storeName}</span>
          {title && (
            <>
              <span style={{ color: "#374151", fontSize: 14 }}>/</span>
              <span style={{ color: "#6B7280", fontSize: 14 }}>{title}</span>
            </>
          )}
        </div>

        {/* Right — support + bell + user menu */}
        <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
          {/* Support button */}
          <button
            onClick={() => setChatOpen((v) => !v)}
            title="Підтримка"
            style={{
              background: chatOpen ? "#1D3461" : "transparent",
              border: `1px solid ${chatOpen ? "#3B82F6" : "#1F2937"}`,
              borderRadius: 8,
              padding: "6px 8px",
              cursor: "pointer",
              color: chatOpen ? "#60A5FA" : "#6B7280",
              display: "flex",
              alignItems: "center",
              transition: "border-color 0.15s, color 0.15s, background 0.15s",
            }}
            onMouseEnter={(e) => {
              if (!chatOpen) {
                (e.currentTarget as HTMLElement).style.borderColor = "#374151";
                (e.currentTarget as HTMLElement).style.color = "#9CA3AF";
              }
            }}
            onMouseLeave={(e) => {
              if (!chatOpen) {
                (e.currentTarget as HTMLElement).style.borderColor = "#1F2937";
                (e.currentTarget as HTMLElement).style.color = "#6B7280";
              }
            }}
          >
            <LifeBuoy size={16} />
          </button>

          {/* Notification bell */}
          <Link
            href="/notifications"
            title="Сповіщення"
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
              textDecoration: "none",
              transition: "border-color 0.15s, color 0.15s",
            }}
            onMouseEnter={(e) => {
              (e.currentTarget as HTMLElement).style.borderColor = "#374151";
              (e.currentTarget as HTMLElement).style.color = "#9CA3AF";
            }}
            onMouseLeave={(e) => {
              (e.currentTarget as HTMLElement).style.borderColor = "#1F2937";
              (e.currentTarget as HTMLElement).style.color = "#6B7280";
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
          </Link>

          {/* User dropdown */}
          <UserMenu />
        </div>
      </header>

      {/* Floating support chat widget */}
      {chatOpen && (
        <SupportChatWidget
          userRole={userRole}
          onClose={() => setChatOpen(false)}
        />
      )}
    </>
  );
}
