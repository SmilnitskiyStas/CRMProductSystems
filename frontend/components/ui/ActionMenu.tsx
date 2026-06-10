"use client";

import { useEffect, useRef, useState, useCallback } from "react";
import { createPortal } from "react-dom";
import { MoreHorizontal } from "lucide-react";

export interface ActionMenuItem {
  label?: string;
  icon?: React.ReactNode;
  onClick?: () => void;
  href?: string;
  variant?: "default" | "danger" | "success" | "warning";
  disabled?: boolean;
  separator?: boolean;
}

interface Props {
  items: ActionMenuItem[];
  label?: string;
}

const VARIANT_COLOR: Record<string, string> = {
  default: "#9CA3AF",
  success: "#4ADE80",
  danger:  "#F87171",
  warning: "#FBBF24",
};

interface MenuPos {
  top: number;
  left: number;
  width: number;
}

export function ActionMenu({ items, label }: Props) {
  const [open, setOpen] = useState(false);
  const [pos, setPos] = useState<MenuPos | null>(null);
  const triggerRef = useRef<HTMLButtonElement>(null);
  const menuRef = useRef<HTMLDivElement>(null);

  const calcPos = useCallback(() => {
    if (!triggerRef.current) return;
    const rect = triggerRef.current.getBoundingClientRect();
    const menuWidth = 200;
    const viewportWidth = window.innerWidth;

    // prefer right-aligned; flip left if not enough space
    let left = rect.right - menuWidth;
    if (left < 8) left = rect.left;

    setPos({
      top: rect.bottom + 4 + window.scrollY,
      left: left + window.scrollX,
      width: menuWidth,
    });
  }, []);

  const toggle = () => {
    if (!open) calcPos();
    setOpen((v) => !v);
  };

  // Close on outside click
  useEffect(() => {
    if (!open) return;
    function handle(e: MouseEvent) {
      const target = e.target as Node;
      if (
        triggerRef.current?.contains(target) ||
        menuRef.current?.contains(target)
      )
        return;
      setOpen(false);
    }
    document.addEventListener("mousedown", handle);
    return () => document.removeEventListener("mousedown", handle);
  }, [open]);

  // Reposition on scroll / resize
  useEffect(() => {
    if (!open) return;
    const reCalc = () => calcPos();
    window.addEventListener("scroll", reCalc, true);
    window.addEventListener("resize", reCalc);
    return () => {
      window.removeEventListener("scroll", reCalc, true);
      window.removeEventListener("resize", reCalc);
    };
  }, [open, calcPos]);

  const dropdown =
    open && pos
      ? createPortal(
          <div
            ref={menuRef}
            style={{
              position: "absolute",
              top: pos.top,
              left: pos.left,
              width: pos.width,
              background: "#111827",
              border: "1px solid #1F2937",
              borderRadius: 10,
              boxShadow: "0 8px 24px rgba(0,0,0,0.6)",
              zIndex: 9999,
              overflow: "hidden",
            }}
          >
            {items.map((item, i) =>
              item.separator ? (
                <div
                  key={i}
                  style={{ height: 1, background: "#1F2937", margin: "3px 0" }}
                />
              ) : (
                <button
                  key={i}
                  disabled={item.disabled}
                  onClick={() => {
                    if (item.onClick) item.onClick();
                    if (item.href) window.open(item.href, "_blank");
                    setOpen(false);
                  }}
                  style={{
                    display: "flex",
                    alignItems: "center",
                    gap: 9,
                    width: "100%",
                    padding: "9px 14px",
                    background: "transparent",
                    border: "none",
                    color: item.disabled
                      ? "#374151"
                      : VARIANT_COLOR[item.variant ?? "default"],
                    fontSize: 13,
                    fontWeight: 500,
                    cursor: item.disabled ? "not-allowed" : "pointer",
                    textAlign: "left",
                    transition: "background 0.1s",
                  }}
                  onMouseEnter={(e) => {
                    if (!item.disabled)
                      (e.currentTarget as HTMLElement).style.background = "#1F2937";
                  }}
                  onMouseLeave={(e) => {
                    (e.currentTarget as HTMLElement).style.background = "transparent";
                  }}
                >
                  {item.icon && (
                    <span style={{ display: "flex", alignItems: "center", opacity: 0.8 }}>
                      {item.icon}
                    </span>
                  )}
                  {item.label}
                </button>
              ),
            )}
          </div>,
          document.body,
        )
      : null;

  return (
    <>
      <button
        ref={triggerRef}
        onClick={toggle}
        style={{
          display: "inline-flex",
          alignItems: "center",
          gap: 5,
          padding: label ? "5px 10px" : "5px 8px",
          background: open ? "#1F2937" : "transparent",
          border: "1px solid #374151",
          borderRadius: 7,
          color: "#9CA3AF",
          fontSize: 12,
          fontWeight: 600,
          cursor: "pointer",
          transition: "background 0.1s, border-color 0.1s",
          whiteSpace: "nowrap",
        }}
        onMouseEnter={(e) => {
          (e.currentTarget as HTMLElement).style.background = "#1F2937";
          (e.currentTarget as HTMLElement).style.borderColor = "#4B5563";
        }}
        onMouseLeave={(e) => {
          if (!open) {
            (e.currentTarget as HTMLElement).style.background = "transparent";
            (e.currentTarget as HTMLElement).style.borderColor = "#374151";
          }
        }}
      >
        <MoreHorizontal size={14} />
        {label && <span>{label}</span>}
      </button>

      {dropdown}
    </>
  );
}
