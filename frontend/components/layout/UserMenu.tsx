"use client";

import { useState, useRef, useEffect } from "react";
import Link from "next/link";
import { Settings, User, LogOut } from "lucide-react";
import { useMe, useLogout } from "@/features/auth/hooks/useAuth";
import { ROLE_LABELS } from "@/features/profile/types";
import { UserProfileCard } from "@/features/profile/components/UserProfileCard";

export function UserMenu() {
  const { data: user } = useMe();
  const logout = useLogout();

  const [open, setOpen] = useState(false);
  const [profileOpen, setProfileOpen] = useState(false);
  const menuRef = useRef<HTMLDivElement>(null);

  const initials = user?.fullName
    ? user.fullName.split(" ").slice(0, 2).map((n) => n[0]).join("").toUpperCase()
    : "?";

  // Close dropdown on outside click
  useEffect(() => {
    if (!open) return;
    function handler(e: MouseEvent) {
      if (menuRef.current && !menuRef.current.contains(e.target as Node)) {
        setOpen(false);
      }
    }
    document.addEventListener("mousedown", handler);
    return () => document.removeEventListener("mousedown", handler);
  }, [open]);

  // Close dropdown on Escape
  useEffect(() => {
    function handler(e: KeyboardEvent) {
      if (e.key === "Escape") { setOpen(false); setProfileOpen(false); }
    }
    document.addEventListener("keydown", handler);
    return () => document.removeEventListener("keydown", handler);
  }, []);

  const menuItemStyle: React.CSSProperties = {
    display: "flex",
    alignItems: "center",
    gap: 10,
    width: "100%",
    padding: "9px 14px",
    background: "transparent",
    border: "none",
    color: "#9CA3AF",
    fontSize: 13,
    cursor: "pointer",
    textAlign: "left",
    borderRadius: 6,
    textDecoration: "none",
    transition: "background 0.1s, color 0.1s",
  };

  return (
    <>
      {/* Trigger — user card */}
      <div ref={menuRef} style={{ position: "relative" }}>
        <button
          onClick={() => setOpen((v) => !v)}
          style={{
            display: "flex",
            alignItems: "center",
            gap: 10,
            background: open ? "#111827" : "transparent",
            border: `1px solid ${open ? "#374151" : "transparent"}`,
            borderRadius: 8,
            padding: "4px 8px 4px 4px",
            cursor: "pointer",
            transition: "background 0.15s, border-color 0.15s",
          }}
        >
          {/* Avatar */}
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
          <div style={{ textAlign: "left" }}>
            <div style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 500, lineHeight: 1.2 }}>
              {user?.fullName ?? "…"}
            </div>
            <div style={{ color: "#4B5563", fontSize: 11, lineHeight: 1.2 }}>
              {ROLE_LABELS[user?.role ?? ""] ?? user?.role ?? ""}
            </div>
          </div>
          {/* Chevron */}
          <svg
            width="12" height="12" viewBox="0 0 12 12" fill="none"
            style={{
              marginLeft: 4,
              transform: open ? "rotate(180deg)" : "rotate(0deg)",
              transition: "transform 0.2s",
              color: "#4B5563",
            }}
          >
            <path d="M2 4l4 4 4-4" stroke="currentColor" strokeWidth="1.5"
              strokeLinecap="round" strokeLinejoin="round" />
          </svg>
        </button>

        {/* Dropdown */}
        {open && (
          <div
            style={{
              position: "absolute",
              top: "calc(100% + 6px)",
              right: 0,
              minWidth: 200,
              background: "#111827",
              border: "1px solid #1F2937",
              borderRadius: 10,
              padding: "6px",
              zIndex: 100,
              boxShadow: "0 8px 24px rgba(0,0,0,0.4)",
            }}
          >
            {/* User header */}
            <div
              style={{
                padding: "10px 14px 10px",
                borderBottom: "1px solid #1F2937",
                marginBottom: 6,
              }}
            >
              <div style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 600 }}>
                {user?.fullName ?? "…"}
              </div>
              <div style={{ color: "#4B5563", fontSize: 11, marginTop: 2 }}>
                {user?.email ?? ""}
              </div>
            </div>

            {/* Profile */}
            <button
              style={menuItemStyle}
              onClick={() => { setOpen(false); setProfileOpen(true); }}
              onMouseEnter={(e) => {
                (e.currentTarget as HTMLElement).style.background = "#1F2937";
                (e.currentTarget as HTMLElement).style.color = "#E8EDF5";
              }}
              onMouseLeave={(e) => {
                (e.currentTarget as HTMLElement).style.background = "transparent";
                (e.currentTarget as HTMLElement).style.color = "#9CA3AF";
              }}
            >
              <User size={15} />
              Профіль
            </button>

            {/* Settings */}
            <Link
              href="/settings-user"
              style={menuItemStyle}
              onClick={() => setOpen(false)}
              onMouseEnter={(e) => {
                (e.currentTarget as HTMLElement).style.background = "#1F2937";
                (e.currentTarget as HTMLElement).style.color = "#E8EDF5";
              }}
              onMouseLeave={(e) => {
                (e.currentTarget as HTMLElement).style.background = "transparent";
                (e.currentTarget as HTMLElement).style.color = "#9CA3AF";
              }}
            >
              <Settings size={15} />
              Налаштування
            </Link>

            {/* Divider */}
            <div style={{ height: 1, background: "#1F2937", margin: "6px 0" }} />

            {/* Logout */}
            <button
              style={{ ...menuItemStyle, color: "#6B7280" }}
              onClick={() => { setOpen(false); logout.mutate(); }}
              onMouseEnter={(e) => {
                (e.currentTarget as HTMLElement).style.background = "#2a0a0a";
                (e.currentTarget as HTMLElement).style.color = "#EF4444";
              }}
              onMouseLeave={(e) => {
                (e.currentTarget as HTMLElement).style.background = "transparent";
                (e.currentTarget as HTMLElement).style.color = "#6B7280";
              }}
            >
              <LogOut size={15} />
              Вийти
            </button>
          </div>
        )}
      </div>

      {/* Profile modal */}
      {profileOpen && (
        <ProfileModal onClose={() => setProfileOpen(false)} />
      )}
    </>
  );
}

function ProfileModal({ onClose }: { onClose: () => void }) {
  return (
    <>
      {/* Backdrop */}
      <div
        onClick={onClose}
        style={{
          position: "fixed", inset: 0,
          background: "rgba(0,0,0,0.55)",
          zIndex: 200,
          backdropFilter: "blur(2px)",
        }}
      />

      {/* Panel */}
      <div
        style={{
          position: "fixed",
          top: 0, right: 0, bottom: 0,
          width: "min(520px, 100vw)",
          background: "#0D1117",
          borderLeft: "1px solid #1F2937",
          zIndex: 201,
          display: "flex",
          flexDirection: "column",
          overflowY: "auto",
        }}
      >
        {/* Header */}
        <div
          style={{
            display: "flex",
            alignItems: "center",
            justifyContent: "space-between",
            padding: "20px 24px",
            borderBottom: "1px solid #1F2937",
            flexShrink: 0,
          }}
        >
          <div>
            <h2 style={{ color: "#E8EDF5", fontSize: 16, fontWeight: 700, margin: 0 }}>
              Профіль
            </h2>
            <p style={{ color: "#4B5563", fontSize: 12, margin: "3px 0 0" }}>
              Інформація про акаунт
            </p>
          </div>
          <button
            onClick={onClose}
            style={{
              background: "transparent",
              border: "1px solid #1F2937",
              borderRadius: 8,
              padding: "6px 10px",
              color: "#4B5563",
              fontSize: 16,
              cursor: "pointer",
              lineHeight: 1,
            }}
          >
            ✕
          </button>
        </div>

        {/* Content */}
        <div style={{ padding: 24, flex: 1 }}>
          <UserProfileCard />
        </div>
      </div>
    </>
  );
}
