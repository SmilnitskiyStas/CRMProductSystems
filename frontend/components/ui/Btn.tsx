"use client";

import { useState } from "react";
import type { CSSProperties, MouseEvent, ReactNode } from "react";

/** Variant → base styles */
const VARIANT_STYLES: Record<
  string,
  { background: string; border: string; color: string; hoverBackground: string; underlineOnHover?: boolean }
> = {
  /** Primary action — matches "Запросити" style across all pages */
  primary: {
    background: "#1D3461",
    border: "1px solid #3B82F6",
    color: "#93C5FD",
    hoverBackground: "#243d73",
  },
  /** Destructive / cancel */
  danger: {
    background: "#2d0a0a",
    border: "1px solid #7F1D1D",
    color: "#F87171",
    hoverBackground: "#3d0f0f",
  },
  /** Approve / confirm */
  success: {
    background: "#1a3a2e",
    border: "1px solid #065F46",
    color: "#4ADE80",
    hoverBackground: "#1e4a38",
  },
  /** Destructive, low emphasis (transparent until hover) */
  dangerOutline: {
    background: "transparent",
    border: "1px solid #7F1D1D",
    color: "#F87171",
    hoverBackground: "#2d0a0a",
  },
  /** Text action — no chrome, underline on hover */
  link: {
    background: "transparent",
    border: "none",
    color: "#60A5FA",
    hoverBackground: "transparent",
    underlineOnHover: true,
  },
  /** Neutral / secondary */
  ghost: {
    background: "transparent",
    border: "1px solid #374151",
    color: "#9CA3AF",
    hoverBackground: "#1a2233",
  },
};

interface BtnProps {
  /** Visual variant — defaults to "primary" */
  variant?: "primary" | "danger" | "dangerOutline" | "success" | "ghost" | "link";
  /** Size — "md" (default) for page header buttons, "sm" for table inline actions */
  size?: "xs" | "sm" | "md";
  children: ReactNode;
  onClick?: (e: MouseEvent<HTMLButtonElement>) => void;
  disabled?: boolean;
  type?: "button" | "submit" | "reset";
  style?: CSSProperties;
  /** Icon to render to the left of children */
  icon?: ReactNode;
  title?: string;
  "aria-label"?: string;
  className?: string;
  /** Stretch to the container width */
  fullWidth?: boolean;
  /** Associates a submit button with a form by id */
  form?: string;
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
  title,
  "aria-label": ariaLabel,
  className,
  fullWidth,
  form,
}: BtnProps) {
  const [hovered, setHovered] = useState(false);
  const v = VARIANT_STYLES[variant];

  const isLink = variant === "link";
  const padding = isLink ? 0 : size === "xs" ? "2px 8px" : size === "sm" ? "4px 10px" : "9px 18px";
  const fontSize = size === "md" ? 13 : 11;

  const background = disabled
    ? isLink ? "transparent" : "#111827"
    : hovered
    ? v.hoverBackground
    : v.background;

  return (
    <button
      type={type}
      form={form}
      title={title}
      aria-label={ariaLabel}
      className={className}
      onClick={onClick}
      disabled={disabled}
      onMouseEnter={() => setHovered(true)}
      onMouseLeave={() => setHovered(false)}
      style={{
        display: "inline-flex",
        alignItems: "center",
        gap: 7,
        padding,
        borderRadius: 8,
        background,
        border: disabled ? (isLink ? "none" : "1px solid #1F2937") : v.border,
        color: disabled ? "#374151" : v.color,
        fontSize,
        fontWeight: 600,
        cursor: disabled ? "not-allowed" : "pointer",
        transition: "background 0.15s ease, opacity 0.15s",
        opacity: disabled ? 0.6 : 1,
        whiteSpace: "nowrap",
        ...(fullWidth ? { width: "100%", justifyContent: "center" } : null),
        ...(v.underlineOnHover && hovered && !disabled ? { textDecoration: "underline" } : null),
        ...style,
      }}
    >
      {icon && <span style={{ display: "flex", alignItems: "center" }}>{icon}</span>}
      {children}
    </button>
  );
}
