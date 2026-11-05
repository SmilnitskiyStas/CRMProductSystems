"use client";

import { useEffect, useId, useRef } from "react";
import type { ReactNode } from "react";
import { X } from "lucide-react";

const FOCUSABLE =
  'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

interface Props {
  title: string;
  onClose: () => void;
  closeLabel: string;
  children: ReactNode;
  width?: number;
  /** Escape / backdrop click close the dialog unless this is true (e.g. while a request is in flight). */
  busy?: boolean;
  role?: "dialog" | "alertdialog";
}

/**
 * Accessible modal shell for the Beauty CRM section: role=dialog + aria-modal, labelled by its title,
 * focus moves inside on open, Tab is trapped, Escape closes, focus returns to the opener on close.
 */
export function BeautyDialog({ title, onClose, closeLabel, children, width = 480, busy = false, role = "dialog" }: Props) {
  const titleId = useId();
  const panelRef = useRef<HTMLDivElement>(null);
  const onCloseRef = useRef(onClose);
  const busyRef = useRef(busy);
  onCloseRef.current = onClose;
  busyRef.current = busy;

  // Captured during the first render, i.e. before any child's `autoFocus` moves focus.
  const openerRef = useRef<HTMLElement | null>(null);
  if (openerRef.current === null && typeof document !== "undefined") {
    openerRef.current = document.activeElement as HTMLElement | null;
  }

  useEffect(() => {
    const opener = openerRef.current;
    const panel = panelRef.current;
    // A child with `autoFocus` has already taken focus; otherwise land on the first control.
    if (panel && !(document.activeElement && document.activeElement !== panel && panel.contains(document.activeElement))) {
      (panel.querySelector<HTMLElement>(FOCUSABLE) ?? panel).focus();
    }

    function onKey(e: KeyboardEvent) {
      if (e.key === "Escape") {
        e.stopPropagation();
        if (!busyRef.current) onCloseRef.current();
        return;
      }
      if (e.key !== "Tab" || !panelRef.current) return;
      const items = Array.from(panelRef.current.querySelectorAll<HTMLElement>(FOCUSABLE));
      if (items.length === 0) {
        e.preventDefault();
        return;
      }
      const firstEl = items[0];
      const lastEl = items[items.length - 1];
      const active = document.activeElement;
      if (e.shiftKey && (active === firstEl || !panelRef.current.contains(active))) {
        e.preventDefault();
        lastEl.focus();
      } else if (!e.shiftKey && (active === lastEl || !panelRef.current.contains(active))) {
        e.preventDefault();
        firstEl.focus();
      }
    }
    document.addEventListener("keydown", onKey, true);
    return () => {
      document.removeEventListener("keydown", onKey, true);
      opener?.focus?.();
    };
  }, []);

  return (
    <div
      style={{
        position: "fixed",
        inset: 0,
        background: "rgba(0,0,0,0.7)",
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        zIndex: 1100,
        padding: 16,
      }}
      onMouseDown={(e) => {
        if (e.target === e.currentTarget && !busy) onClose();
      }}
    >
      <div
        ref={panelRef}
        role={role}
        aria-modal="true"
        aria-labelledby={titleId}
        tabIndex={-1}
        style={{
          background: "#0D1117",
          border: "1px solid #1F2937",
          borderRadius: 12,
          width: "100%",
          maxWidth: width,
          maxHeight: "90vh",
          overflowY: "auto",
          outline: "none",
        }}
      >
        <div
          style={{
            display: "flex",
            alignItems: "center",
            justifyContent: "space-between",
            padding: "16px 20px",
            borderBottom: "1px solid #1F2937",
          }}
        >
          <h2 id={titleId} style={{ color: "#E8EDF5", fontSize: 15, fontWeight: 600, margin: 0 }}>
            {title}
          </h2>
          <button
            type="button"
            aria-label={closeLabel}
            onClick={onClose}
            disabled={busy}
            style={{ background: "none", border: "none", color: "#6B7280", cursor: "pointer", padding: 4 }}
          >
            <X size={18} aria-hidden="true" />
          </button>
        </div>
        <div style={{ padding: 20 }}>{children}</div>
      </div>
    </div>
  );
}
