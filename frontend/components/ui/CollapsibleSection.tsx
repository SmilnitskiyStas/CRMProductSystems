"use client";

import { useState } from "react";
import { ChevronDown, ChevronRight } from "lucide-react";

export interface CollapsibleSectionProps {
  /** Section heading shown in the always-visible header row. */
  title: string;
  /** Whether the body is expanded on first render. Defaults to `true`. */
  defaultOpen?: boolean;
  children: React.ReactNode;
}

/**
 * Generic dark-theme collapsible panel: a clickable header row (title + chevron)
 * that shows / hides its body. Purely presentational — no feature coupling.
 */
export function CollapsibleSection({
  title,
  defaultOpen = true,
  children,
}: CollapsibleSectionProps) {
  const [open, setOpen] = useState(defaultOpen);

  return (
    <div style={{ border: "1px solid #1F2937", borderRadius: 10 }}>
      <button
        type="button"
        onClick={() => setOpen((v) => !v)}
        aria-expanded={open}
        style={{
          width: "100%",
          display: "flex",
          alignItems: "center",
          gap: 8,
          padding: "10px 14px",
          background: "transparent",
          border: "none",
          cursor: "pointer",
          color: "#9CA3AF",
          fontSize: 12,
          fontWeight: 600,
          fontFamily: "inherit",
          textAlign: "left",
        }}
      >
        {open ? (
          <ChevronDown size={14} aria-hidden />
        ) : (
          <ChevronRight size={14} aria-hidden />
        )}
        <span>{title}</span>
      </button>
      {open && (
        <div style={{ borderTop: "1px solid #1F2937", padding: "14px" }}>
          {children}
        </div>
      )}
    </div>
  );
}
