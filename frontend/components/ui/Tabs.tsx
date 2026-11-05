"use client";

import type { ReactNode } from "react";

export interface TabItem<K extends string = string> {
  key: K;
  label: ReactNode;
  count?: number;
}

/** Underline-style tab row. Controlled via value/onChange. */
export function Tabs<K extends string = string>({
  items,
  value,
  onChange,
}: {
  items: TabItem<K>[];
  value: K;
  onChange: (key: K) => void;
}) {
  return (
    <div style={{ display: "flex", gap: 4, borderBottom: "1px solid #1F2937", flexWrap: "wrap" }}>
      {items.map((it) => {
        const active = it.key === value;
        return (
          <button
            key={it.key}
            type="button"
            onClick={() => onChange(it.key)}
            style={{
              background: "none",
              border: "none",
              borderBottom: `2px solid ${active ? "#3B82F6" : "transparent"}`,
              marginBottom: -1,
              padding: "8px 14px",
              fontSize: 13,
              fontWeight: active ? 600 : 400,
              color: active ? "#60A5FA" : "#6B7280",
              cursor: "pointer",
              display: "inline-flex",
              alignItems: "center",
              gap: 6,
              whiteSpace: "nowrap",
            }}
          >
            {it.label}
            {it.count != null && <span style={{ fontSize: 11, color: active ? "#60A5FA" : "#4B5563" }}>{it.count}</span>}
          </button>
        );
      })}
    </div>
  );
}
