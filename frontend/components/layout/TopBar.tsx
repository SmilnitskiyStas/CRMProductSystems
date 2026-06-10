"use client";

import Link from "next/link";
import { Bell } from "lucide-react";
import { useMe } from "@/features/auth/hooks/useAuth";
import { UserMenu } from "./UserMenu";

interface Props {
  title?: string;
}

export function TopBar({ title }: Props) {
  const { data: user } = useMe();

  const storeName = user?.storeId ? "Магазин #1" : "ShelfGuard";

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

      {/* Right — bell + user menu */}
      <div style={{ display: "flex", alignItems: "center", gap: 12 }}>
        {/* Notification bell */}
        <Link
          href="/notifications"
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
  );
}
