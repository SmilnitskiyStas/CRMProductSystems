"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { createPortal } from "react-dom";
import { useTranslations } from "next-intl";

interface Props {
  barcodes: string[] | null | undefined;
}

interface PopPos {
  top: number;
  left: number;
}

/**
 * Barcode cell for the catalog table. Always shows the primary barcode (`barcodes[0]`,
 * the project-wide "active" convention — POS/receipts/analytics all read index 0). When a
 * product has more than one barcode, a "+N" pill appears and hovering/focusing the cell opens
 * a portal popover listing every barcode with the primary marked ★.
 *
 * Portal + getBoundingClientRect + outside-click/scroll close follows components/ui/ActionMenu.tsx
 * (the project has no Radix Popover/HoverCard primitive).
 */
export function BarcodeCell({ barcodes }: Props) {
  const t = useTranslations("Dashboard.inventory.table");
  const list = barcodes ?? [];
  const extra = list.length - 1;

  const [open, setOpen] = useState(false);
  const [pos, setPos] = useState<PopPos | null>(null);
  const anchorRef = useRef<HTMLSpanElement>(null);
  const popRef = useRef<HTMLDivElement>(null);
  const closeTimer = useRef<ReturnType<typeof setTimeout> | null>(null);

  const calcPos = useCallback(() => {
    if (!anchorRef.current) return;
    const rect = anchorRef.current.getBoundingClientRect();
    setPos({ top: rect.bottom + 4 + window.scrollY, left: rect.left + window.scrollX });
  }, []);

  const show = useCallback(() => {
    if (closeTimer.current) clearTimeout(closeTimer.current);
    if (list.length <= 1) return;
    calcPos();
    setOpen(true);
  }, [calcPos, list.length]);

  const hideSoon = useCallback(() => {
    if (closeTimer.current) clearTimeout(closeTimer.current);
    closeTimer.current = setTimeout(() => setOpen(false), 120);
  }, []);

  useEffect(() => {
    if (!open) return;
    const reCalc = () => calcPos();
    window.addEventListener("scroll", reCalc, true);
    window.addEventListener("resize", reCalc);
    function onDocMouseDown(e: MouseEvent) {
      const target = e.target as Node;
      if (anchorRef.current?.contains(target) || popRef.current?.contains(target)) return;
      setOpen(false);
    }
    document.addEventListener("mousedown", onDocMouseDown);
    return () => {
      window.removeEventListener("scroll", reCalc, true);
      window.removeEventListener("resize", reCalc);
      document.removeEventListener("mousedown", onDocMouseDown);
    };
  }, [open, calcPos]);

  useEffect(
    () => () => {
      if (closeTimer.current) clearTimeout(closeTimer.current);
    },
    [],
  );

  if (list.length === 0) return <span style={{ color: "#4B5563" }}>—</span>;

  return (
    <span
      ref={anchorRef}
      onMouseEnter={show}
      onMouseLeave={hideSoon}
      onClick={() => (open ? setOpen(false) : show())}
      onFocus={show}
      onBlur={hideSoon}
      tabIndex={extra > 0 ? 0 : undefined}
      style={{
        display: "inline-flex",
        alignItems: "center",
        gap: 6,
        cursor: extra > 0 ? "pointer" : "default",
        outline: "none",
      }}
    >
      <span>{list[0]}</span>

      {extra > 0 && (
        <span
          style={{
            fontSize: 10,
            fontWeight: 700,
            color: "#93C5FD",
            background: "#0F1F3D",
            border: "1px solid #1E3A5F",
            borderRadius: 5,
            padding: "1px 5px",
            lineHeight: 1.4,
          }}
        >
          {t("barcodeMore", { count: extra })}
        </span>
      )}

      {open &&
        pos &&
        createPortal(
          <div
            ref={popRef}
            onMouseEnter={show}
            onMouseLeave={hideSoon}
            style={{
              position: "absolute",
              top: pos.top,
              left: pos.left,
              background: "#111827",
              border: "1px solid #1F2937",
              borderRadius: 10,
              boxShadow: "0 8px 24px rgba(0,0,0,0.6)",
              zIndex: 9999,
              padding: "8px 10px",
              minWidth: 160,
            }}
          >
            <div
              style={{
                color: "#4B5563",
                fontSize: 10,
                fontWeight: 600,
                textTransform: "uppercase",
                letterSpacing: "0.05em",
                marginBottom: 6,
              }}
            >
              {t("barcodeAllTitle")}
            </div>
            <div style={{ display: "flex", flexDirection: "column", gap: 4 }}>
              {list.map((b, i) => (
                <div
                  key={b}
                  style={{
                    display: "flex",
                    alignItems: "center",
                    gap: 6,
                    fontFamily: "monospace",
                    fontSize: 12,
                    color: i === 0 ? "#E8EDF5" : "#9CA3AF",
                  }}
                >
                  <span style={{ width: 12, textAlign: "center", color: "#FBBF24" }}>
                    {i === 0 ? "★" : ""}
                  </span>
                  <span>{b}</span>
                  {i === 0 && (
                    <span style={{ fontFamily: "inherit", fontSize: 9, color: "#6B7280" }}>
                      {t("barcodePrimary")}
                    </span>
                  )}
                </div>
              ))}
            </div>
          </div>,
          document.body,
        )}
    </span>
  );
}
