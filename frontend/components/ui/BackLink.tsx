"use client";

import Link from "next/link";
import { ArrowLeft } from "lucide-react";
import type { ReactNode } from "react";

const style = {
  display: "inline-flex",
  alignItems: "center",
  gap: 6,
  alignSelf: "flex-start",
  background: "none",
  border: "none",
  padding: 0,
  cursor: "pointer",
  color: "#9CA3AF",
  fontSize: 13,
  textDecoration: "none",
} as const;

/** "← Back" link for detail pages. Pass `href` (next/link) or `onClick`. */
export function BackLink({ href, onClick, children }: { href?: string; onClick?: () => void; children: ReactNode }) {
  if (href) {
    return (
      <Link href={href} style={style}>
        <ArrowLeft size={14} />
        {children}
      </Link>
    );
  }
  return (
    <button type="button" onClick={onClick} style={style}>
      <ArrowLeft size={14} />
      {children}
    </button>
  );
}
