"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import {
  LayoutDashboard,
  Package,
  ShoppingCart,
  ArrowLeftRight,
  Trash2,
  BarChart2,
  Bell,
  Settings,
  LogOut,
} from "lucide-react";
import { useLogout } from "@/features/auth/hooks/useAuth";

interface NavItem {
  href: string;
  label: string;
  icon: React.ReactNode;
}

const NAV_ITEMS: NavItem[] = [
  { href: "/dashboard", label: "Дашборд", icon: <LayoutDashboard size={18} /> },
  { href: "/inventory", label: "Каталог", icon: <Package size={18} /> },
  { href: "/stock", label: "Залишки", icon: <ShoppingCart size={18} /> },
  { href: "/transfers", label: "Переміщення", icon: <ArrowLeftRight size={18} /> },
  { href: "/write-offs", label: "Списання", icon: <Trash2 size={18} /> },
  { href: "/analytics", label: "Аналітика", icon: <BarChart2 size={18} /> },
  { href: "/notifications", label: "Сповіщення", icon: <Bell size={18} /> },
  { href: "/settings", label: "Налаштування", icon: <Settings size={18} /> },
];

export function Sidebar() {
  const pathname = usePathname();
  const logout = useLogout();

  return (
    <aside
      style={{
        width: 240,
        minHeight: "100vh",
        background: "#0D1117",
        borderRight: "1px solid #1F2937",
        display: "flex",
        flexDirection: "column",
        flexShrink: 0,
        position: "sticky",
        top: 0,
        height: "100vh",
        overflowY: "auto",
      }}
    >
      {/* Logo */}
      <div
        style={{
          padding: "20px 20px 16px",
          borderBottom: "1px solid #1F2937",
          display: "flex",
          alignItems: "center",
          gap: 10,
        }}
      >
        <div
          style={{
            width: 32,
            height: 32,
            background: "linear-gradient(135deg, #3B82F6, #6366F1)",
            borderRadius: 8,
            display: "flex",
            alignItems: "center",
            justifyContent: "center",
            fontSize: 16,
            flexShrink: 0,
          }}
        >
          🛡️
        </div>
        <div>
          <div style={{ color: "#E8EDF5", fontSize: 14, fontWeight: 700 }}>ShelfGuard</div>
          <div style={{ color: "#4B5563", fontSize: 11 }}>v1.0</div>
        </div>
      </div>

      {/* Navigation */}
      <nav style={{ flex: 1, padding: "12px 10px" }}>
        <div style={{ display: "flex", flexDirection: "column", gap: 2 }}>
          {NAV_ITEMS.map((item) => {
            const active = pathname === item.href || pathname.startsWith(item.href + "/");
            return (
              <Link
                key={item.href}
                href={item.href}
                style={{
                  display: "flex",
                  alignItems: "center",
                  gap: 10,
                  padding: "9px 12px",
                  borderRadius: 8,
                  background: active ? "#1D3461" : "transparent",
                  color: active ? "#93C5FD" : "#6B7280",
                  fontSize: 13,
                  fontWeight: active ? 600 : 400,
                  textDecoration: "none",
                  transition: "background 0.1s, color 0.1s",
                }}
                onMouseEnter={(e) => {
                  if (!active) {
                    (e.currentTarget as HTMLElement).style.background = "#111827";
                    (e.currentTarget as HTMLElement).style.color = "#9CA3AF";
                  }
                }}
                onMouseLeave={(e) => {
                  if (!active) {
                    (e.currentTarget as HTMLElement).style.background = "transparent";
                    (e.currentTarget as HTMLElement).style.color = "#6B7280";
                  }
                }}
              >
                <span style={{ opacity: active ? 1 : 0.7 }}>{item.icon}</span>
                {item.label}
              </Link>
            );
          })}
        </div>
      </nav>

      {/* Logout */}
      <div style={{ padding: "12px 10px", borderTop: "1px solid #1F2937" }}>
        <button
          onClick={() => logout.mutate()}
          style={{
            width: "100%",
            display: "flex",
            alignItems: "center",
            gap: 10,
            padding: "9px 12px",
            borderRadius: 8,
            background: "transparent",
            border: "none",
            color: "#6B7280",
            fontSize: 13,
            cursor: "pointer",
            textAlign: "left",
            transition: "background 0.1s, color 0.1s",
          }}
          onMouseEnter={(e) => {
            (e.currentTarget as HTMLElement).style.background = "#2a0a0a";
            (e.currentTarget as HTMLElement).style.color = "#EF4444";
          }}
          onMouseLeave={(e) => {
            (e.currentTarget as HTMLElement).style.background = "transparent";
            (e.currentTarget as HTMLElement).style.color = "#6B7280";
          }}
        >
          <LogOut size={18} style={{ opacity: 0.7 }} />
          Вийти
        </button>
      </div>
    </aside>
  );
}
