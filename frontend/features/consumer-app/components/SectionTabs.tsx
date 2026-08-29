"use client";

import type { ReactNode } from "react";

export interface SectionTabItem<TKey extends string> {
  key: TKey;
  label: ReactNode;
  icon?: ReactNode;
  count?: number;
}

export function SectionTabs<TKey extends string>({
  items,
  activeKey,
  onChange,
  ariaLabel,
  marginBottom = 16,
}: {
  items: readonly SectionTabItem<TKey>[];
  activeKey: TKey;
  onChange: (key: TKey) => void;
  ariaLabel: string;
  marginBottom?: number;
}) {
  return (
    <div style={{ maxWidth: "100%", overflow: "hidden", marginBottom, borderBottom: "1px solid #1F2937" }}>
      <div
        role="tablist"
        aria-label={ariaLabel}
        style={{ display: "flex", flexWrap: "wrap", gap: 4 }}
      >
        {items.map((item) => {
          const active = item.key === activeKey;
          return (
            <button
              key={item.key}
              type="button"
              role="tab"
              aria-selected={active}
              onClick={() => onChange(item.key)}
              style={{ display: "inline-flex", alignItems: "center", justifyContent: "center", gap: 7, minHeight: 42, padding: "8px 14px", marginBottom: -1, border: 0, borderBottom: active ? "2px solid #3B82F6" : "2px solid transparent", background: "transparent", color: active ? "#3B82F6" : "#6B7280", fontSize: 12, fontWeight: active ? 700 : 500, cursor: "pointer", whiteSpace: "nowrap", transition: "color 150ms ease, border-color 150ms ease" }}
            >
              {item.icon}
              <span>{item.label}</span>
              {item.count !== undefined && (
                <span style={{ minWidth: 20, padding: "1px 6px", borderRadius: 999, background: active ? "rgba(59,130,246,.14)" : "#1F2937", color: active ? "#60A5FA" : "#6B7280", fontSize: 10, fontWeight: 700, textAlign: "center" }}>
                  {item.count}
                </span>
              )}
            </button>
          );
        })}
      </div>
    </div>
  );
}
