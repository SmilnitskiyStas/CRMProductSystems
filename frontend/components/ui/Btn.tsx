"use client";

import type { CSSProperties, ReactNode } from "react";

/** Variant → base styles */
const VARIANT_STYLES: Record<
  string,
  { background: string; border: string; color: string }
> = {
  /** Primary action — matches "Запросити" style across all pages */
  primary: {
    background: "#1D3461",
    border: "1px solid #3B82F6",
    color: "#93C5FD",
  },
  /** Destructive / cancel */
  danger: {
    background: "#2d0a0a",
    border: "1px solid #7F1D1D",
    color: "#F87171",
  },
  /** Approve / confirm */
  success: {
    background: "#1a3a2e",
    border: "1px solid #065F46",
    color: "#4ADE80",
  },
  /** Neutral / secondary */
  ghost: {
    background: "transparent",
    border: "1px solid #374151",
    color: "#9CA3AF",
  },
};

interface BtnProps {
  /** Visual variant — defaults to "primary" */
  variant?: "primary" | "danger" | "success" | "ghost";
  /** Size — "md" (default) for page header buttons, "sm" for table inline actions */
  size?: "sm" | "md";
  children: ReactNode;
  onClick?: () => void;
  disabled?: boolean;
  type?: "button" | "submit" | "reset";
  style?: CSSProperties;
  /** Icon to render to the left of children */
  icon?: ReactNode;
}

export function Btn({
  variant = "primary",
  size = "md",
  children,
  onClick,
  disabled,
  type = "button",
  style,
  icon,
}: BtnProps) {
  const v = VARIANT_STYLES[variant];

  const padding = size === "sm" ? "4px 10px" : "9px 18px";
  const fontSize = size === "sm" ? 11 : 13;

  return (
    <button
      type={type}
      onClick={onClick}
      disabled={disabled}
      style={{
        display: "inline-flex",
        alignItems: "center",
        gap: 7,
        padding,
        borderRadius: 8,
        background: disabled ? "#111827" : v.background,
        border: disabled ? "1px solid #1F2937" : v.border,
        color: disabled ? "#374151" : v.color,
        fontSize,
        fontWeight: 600,
        cursor: disabled ? "not-allowed" : "pointer",
        transition: "opacity 0.15s",
        opacity: disabled ? 0.6 : 1,
        whiteSpace: "nowrap",
        ...style,
      }}
    >
      {icon && <span style={{ display: "flex", alignItems: "center" }}>{icon}</span>}
      {children}
    </button>
  );
}
