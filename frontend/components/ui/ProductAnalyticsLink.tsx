"use client";

import Link from "next/link";
import { BarChart2 } from "lucide-react";

interface Props {
  productId: string;
  size?: number;
  style?: React.CSSProperties;
}

export function ProductAnalyticsLink({ productId, size = 13, style }: Props) {
  return (
    <Link
      href={`/inventory/${productId}?tab=analytics`}
      title="Аналітика товару"
      onClick={(e) => e.stopPropagation()}
      style={{
        display: "inline-flex",
        alignItems: "center",
        justifyContent: "center",
        background: "transparent",
        border: "1px solid #1F2937",
        borderRadius: 6,
        padding: "4px 7px",
        color: "#6B7280",
        textDecoration: "none",
        flexShrink: 0,
        transition: "color 0.1s, border-color 0.1s",
        ...style,
      }}
      onMouseEnter={(e) => {
        (e.currentTarget as HTMLElement).style.color = "#38BDF8";
        (e.currentTarget as HTMLElement).style.borderColor = "#38BDF8";
      }}
      onMouseLeave={(e) => {
        (e.currentTarget as HTMLElement).style.color = "#6B7280";
        (e.currentTarget as HTMLElement).style.borderColor = "#1F2937";
      }}
    >
      <BarChart2 size={size} />
    </Link>
  );
}
