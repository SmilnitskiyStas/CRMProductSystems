"use client";

import { useState } from "react";
import Link from "next/link";
import { Bell, LifeBuoy, Bot } from "lucide-react";
import { useMe } from "@/features/auth/hooks/useAuth";
import { AppRoles, TENANT_ROLES } from "@/lib/roles";
import type { AppRole } from "@/lib/roles";
import { UserMenu } from "./UserMenu";
import { StoreSelector } from "./StoreSelector";
import { SupportChatWidget } from "./SupportChatWidget";
import type { ChatTab } from "./SupportChatWidget";
import { useUnreadCount } from "@/features/notifications/hooks/useNotifications";

interface Props {
  title?: string;
}

export function TopBar({ title }: Props) {
  const { data: user } = useMe();
  const { data: unreadCount = 0 } = useUnreadCount();
  const [panelOpen, setPanelOpen] = useState(false);
  const [activeTab, setActiveTab] = useState<ChatTab>("support");

  const userRole = (user?.role ?? "") as AppRole;

  function openPanel(tab: ChatTab) {
    setActiveTab(tab);
    setPanelOpen(true);
  }

  function handleTabChange(tab: ChatTab) {
    setActiveTab(tab);
  }

  const supportActive = panelOpen && activeTab === "support";
  const assistantActive = panelOpen && activeTab === "assistant";

  function btnStyle(active: boolean): React.CSSProperties {
    return {
      background: active ? "#1D3461" : "transparent",
      border: `1px solid ${active ? "#3B82F6" : "#1F2937"}`,
      borderRadius: 8,
      padding: "6px 12px",
      cursor: "pointer",
      color: active ? "#60A5FA" : "#6B7280",
      display: "flex",
      alignItems: "center",
      gap: 6,
      transition: "border-color 0.15s, color 0.15s, background 0.15s",
    };
  }

  function onMouseEnter(e: React.MouseEvent<HTMLButtonElement>, active: boolean) {
    if (!active) {
      (e.currentTarget as HTMLElement).style.borderColor = "#374151";
      (e.currentTarget as HTMLElement).style.color = "#9CA3AF";
    }
  }

  function onMouseLeave(e: React.MouseEvent<HTMLButtonElement>, active: boolean) {
    if (!active) {
      (e.currentTarget as HTMLElement).style.borderColor = "#1F2937";
      (e.currentTarget as HTMLElement).style.color = "#6B7280";
    }
  }

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
        {/* Left — store selector / breadcrumb */}
        <div style={{ display: "flex", alignItems: "center", gap: 12 }}>
          {/* supplier_admin has no stores — /api/stores would 403 (v4.1, ADR-016) */}
          {userRole !== AppRoles.SupplierAdmin && <StoreSelector />}
          {title && (
            <>
              <span style={{ color: "#374151", fontSize: 14 }}>/</span>
              <span style={{ color: "#6B7280", fontSize: 14 }}>{title}</span>
            </>
          )}
        </div>

        {/* Right — support + assistant + bell + user menu */}
        <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
          {/* Support chat button */}
          <button
            onClick={() => supportActive ? setPanelOpen(false) : openPanel("support")}
            title="Підтримка"
            style={btnStyle(supportActive)}
            onMouseEnter={(e) => onMouseEnter(e, supportActive)}
            onMouseLeave={(e) => onMouseLeave(e, supportActive)}
          >
            <LifeBuoy size={16} />
            <span style={{ fontSize: 13, fontWeight: 500 }}>Чат підтримка</span>
          </button>

          {/* AI assistant button */}
          <button
            onClick={() => assistantActive ? setPanelOpen(false) : openPanel("assistant")}
            title="AI Бізнес-Асистент"
            style={btnStyle(assistantActive)}
            onMouseEnter={(e) => onMouseEnter(e, assistantActive)}
            onMouseLeave={(e) => onMouseLeave(e, assistantActive)}
          >
            <Bot size={16} />
            <span style={{ fontSize: 13, fontWeight: 500 }}>Мій асистент</span>
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
            {unreadCount > 0 && (
              unreadCount > 9 ? (
                <span style={{
                  position: "absolute", top: -6, right: -6,
                  minWidth: 16, height: 16, padding: "0 4px",
                  background: "#EF4444", borderRadius: 8,
                  fontSize: 10, fontWeight: 700, color: "#fff",
                  display: "flex", alignItems: "center", justifyContent: "center",
                  lineHeight: 1,
                }}>
                  9+
                </span>
              ) : (
                <span style={{
                  position: "absolute", top: -6, right: -6,
                  width: 16, height: 16,
                  background: "#EF4444", borderRadius: "50%",
                  fontSize: 10, fontWeight: 700, color: "#fff",
                  display: "flex", alignItems: "center", justifyContent: "center",
                }}>
                  {unreadCount}
                </span>
              )
            )}
          </Link>

          {/* User dropdown */}
          <UserMenu />
        </div>
      </header>

      {/* Unified chat panel */}
      {panelOpen && (
        <SupportChatWidget
          userRole={userRole}
          onClose={() => setPanelOpen(false)}
          activeTab={activeTab}
          onTabChange={handleTabChange}
        />
      )}
    </>
  );
}
