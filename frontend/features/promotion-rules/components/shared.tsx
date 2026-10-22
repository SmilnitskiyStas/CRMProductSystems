"use client";

import type { CSSProperties, ReactNode } from "react";
import { X } from "lucide-react";
import { inputStyle, labelStyle } from "@/components/ui/fieldStyles";

export { inputStyle, labelStyle };



export const helpStyle: CSSProperties = { color: "#6B7280", fontSize: 12, marginTop: 6, lineHeight: 1.5 };

export const cardStyle: CSSProperties = {
  background: "#111827",
  border: "1px solid #1F2937",
  borderRadius: 12,
  padding: 18,
};

export function Field({
  label,
  help,
  children,
  htmlFor,
}: {
  label: string;
  help?: string;
  children: ReactNode;
  htmlFor?: string;
}) {
  return (
    <div style={{ marginBottom: 16 }}>
      <label htmlFor={htmlFor} style={labelStyle}>
        {label}
      </label>
      {children}
      {help && <div style={helpStyle}>{help}</div>}
    </div>
  );
}

export function Chip({
  children,
  onRemove,
  active,
  onClick,
  title,
}: {
  children: ReactNode;
  onRemove?: () => void;
  active?: boolean;
  onClick?: () => void;
  title?: string;
}) {
  return (
    <span
      onClick={onClick}
      title={title}
      style={{
        display: "inline-flex",
        alignItems: "center",
        gap: 6,
        padding: "5px 10px",
        borderRadius: 999,
        fontSize: 12,
        fontWeight: 500,
        cursor: onClick ? "pointer" : "default",
        background: active === false ? "#0D1117" : "#1D3461",
        border: `1px solid ${active === false ? "#374151" : "#3B82F6"}`,
        color: active === false ? "#9CA3AF" : "#BFDBFE",
        userSelect: "none",
      }}
    >
      {children}
      {onRemove && (
        <button
          type="button"
          aria-label="remove"
          onClick={(e) => {
            e.stopPropagation();
            onRemove();
          }}
          style={{ background: "none", border: "none", color: "inherit", cursor: "pointer", padding: 0, display: "flex" }}
        >
          <X size={12} />
        </button>
      )}
    </span>
  );
}

export function Note({ tone, children }: { tone: "info" | "warn" | "error"; children: ReactNode }) {
  const c = {
    info: { bg: "#0c1a30", border: "#1E3A5F", color: "#93C5FD" },
    warn: { bg: "#2a1f08", border: "#78500F", color: "#FBBF24" },
    error: { bg: "#2d0a0a", border: "#7F1D1D", color: "#F87171" },
  }[tone];
  return (
    <div
      style={{
        background: c.bg,
        border: `1px solid ${c.border}`,
        color: c.color,
        borderRadius: 8,
        padding: "10px 14px",
        fontSize: 13,
        lineHeight: 1.5,
        marginBottom: 14,
      }}
    >
      {children}
    </div>
  );
}

/** A radio-like selectable card used for rule type / audience / match-mode choices. */
export function ChoiceCard({
  selected,
  onClick,
  title,
  text,
  example,
}: {
  selected: boolean;
  onClick: () => void;
  title: string;
  text?: string;
  example?: string;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      style={{
        textAlign: "left",
        cursor: "pointer",
        background: selected ? "#0f2347" : "#111827",
        border: `2px solid ${selected ? "#3B82F6" : "#1F2937"}`,
        borderRadius: 12,
        padding: 16,
        color: "#E8EDF5",
        display: "flex",
        flexDirection: "column",
        gap: 6,
      }}
    >
      <span style={{ fontSize: 15, fontWeight: 700 }}>{title}</span>
      {text && <span style={{ fontSize: 12.5, color: "#9CA3AF", lineHeight: 1.5 }}>{text}</span>}
      {example && (
        <span
          style={{
            fontSize: 12,
            color: "#86EFAC",
            background: "#0b1f17",
            border: "1px solid #14532D",
            borderRadius: 6,
            padding: "5px 8px",
            marginTop: 4,
          }}
        >
          {example}
        </span>
      )}
    </button>
  );
}
