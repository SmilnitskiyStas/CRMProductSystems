"use client";

import { useEffect } from "react";
import { X } from "lucide-react";

interface Props {
  isOpen: boolean;
  onClose: () => void;
  title: string;
  subtitle?: string;
  children: React.ReactNode;
  width?: number;
  actions?: React.ReactNode;
}

export function DetailDrawer({
  isOpen,
  onClose,
  title,
  subtitle,
  children,
  width = 520,
  actions,
}: Props) {
  // Close on Escape
  useEffect(() => {
    if (!isOpen) return;
    function onKey(e: KeyboardEvent) {
      if (e.key === "Escape") onClose();
    }
    document.addEventListener("keydown", onKey);
    return () => document.removeEventListener("keydown", onKey);
  }, [isOpen, onClose]);

  if (!isOpen) return null;

  return (
    <>
      {/* Overlay */}
      <div
        onClick={onClose}
        style={{
          position: "fixed",
          inset: 0,
          background: "rgba(0,0,0,0.55)",
          zIndex: 600,
          backdropFilter: "blur(2px)",
        }}
      />

      {/* Drawer panel */}
      <div
        style={{
          position: "fixed",
          top: 0,
          right: 0,
          bottom: 0,
          width: Math.min(width, window.innerWidth - 48),
          background: "#0D1117",
          borderLeft: "1px solid #1F2937",
          zIndex: 601,
          display: "flex",
          flexDirection: "column",
          boxShadow: "-8px 0 32px rgba(0,0,0,0.4)",
        }}
      >
        {/* Header */}
        <div
          style={{
            display: "flex",
            alignItems: "flex-start",
            justifyContent: "space-between",
            padding: "20px 24px 16px",
            borderBottom: "1px solid #1F2937",
            flexShrink: 0,
          }}
        >
          <div>
            <div style={{ color: "#E8EDF5", fontSize: 16, fontWeight: 700 }}>{title}</div>
            {subtitle && (
              <div style={{ color: "#4B5563", fontSize: 12, marginTop: 3 }}>{subtitle}</div>
            )}
          </div>
          <div style={{ display: "flex", alignItems: "center", gap: 6, flexShrink: 0, marginLeft: 12 }}>
            {actions}
            <button
              onClick={onClose}
              style={{
                background: "transparent",
                border: "1px solid #1F2937",
                borderRadius: 7,
                color: "#6B7280",
                padding: "4px 8px",
                cursor: "pointer",
                display: "flex",
                alignItems: "center",
              }}
            >
              <X size={15} />
            </button>
          </div>
        </div>

        {/* Content — scrollable */}
        <div
          style={{
            flex: 1,
            overflowY: "auto",
            padding: "20px 24px",
          }}
        >
          {children}
        </div>
      </div>
    </>
  );
}

// ── Helper sub-components used inside drawers ───────────────────────────────

export function DrawerField({
  label,
  value,
  color,
}: {
  label: string;
  value: React.ReactNode;
  color?: string;
}) {
  return (
    <div style={{ marginBottom: 14 }}>
      <div style={{ color: "#4B5563", fontSize: 11, fontWeight: 600, textTransform: "uppercase", letterSpacing: "0.05em", marginBottom: 4 }}>
        {label}
      </div>
      <div style={{ color: color ?? "#E8EDF5", fontSize: 13, fontWeight: 500 }}>{value}</div>
    </div>
  );
}

export function DrawerSection({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div style={{ marginBottom: 20 }}>
      <div
        style={{
          color: "#4B5563",
          fontSize: 11,
          fontWeight: 600,
          textTransform: "uppercase",
          letterSpacing: "0.05em",
          marginBottom: 10,
          paddingBottom: 6,
          borderBottom: "1px solid #1F2937",
        }}
      >
        {title}
      </div>
      {children}
    </div>
  );
}

export function DrawerGrid({ children }: { children: React.ReactNode }) {
  return (
    <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "0 24px" }}>
      {children}
    </div>
  );
}
