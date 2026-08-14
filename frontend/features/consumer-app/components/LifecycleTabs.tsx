"use client";

/**
 * TASK-525: shared 3-tab strip (Активні / Минулі / Чернетки) reused identically by
 * BannersSection and PromoProductsSection — both filter an already-fetched list client-side
 * by a lifecycle bucket, no per-tab API call. Same visual pattern as
 * marketing-analytics/price-segments/components/ModeTabs.tsx (underline tabs), extended with
 * an optional count badge per tab.
 */
export type LifecycleTab = "running" | "past" | "draft";

const TABS: LifecycleTab[] = ["running", "past", "draft"];

interface Props {
  tab: LifecycleTab;
  onTabChange: (tab: LifecycleTab) => void;
  labels: Record<LifecycleTab, string>;
  counts?: Partial<Record<LifecycleTab, number>>;
}

export function LifecycleTabs({ tab, onTabChange, labels, counts }: Props) {
  return (
    <div style={{ display: "flex", gap: 4, borderBottom: "1px solid #1F2937", marginBottom: 16 }}>
      {TABS.map((key) => {
        const active = tab === key;
        const count = counts?.[key];
        return (
          <button
            key={key}
            type="button"
            onClick={() => onTabChange(key)}
            style={{
              display: "flex",
              alignItems: "center",
              gap: 6,
              padding: "8px 16px",
              background: "transparent",
              border: "none",
              borderBottom: active ? "2px solid #3B82F6" : "2px solid transparent",
              color: active ? "#3B82F6" : "#6B7280",
              fontSize: 13,
              fontWeight: active ? 600 : 400,
              cursor: "pointer",
              marginBottom: -1,
              transition: "color 0.15s",
            }}
          >
            {labels[key]}
            {count !== undefined && (
              <span
                style={{
                  fontSize: 10,
                  fontWeight: 700,
                  padding: "1px 6px",
                  borderRadius: 999,
                  background: active ? "#1D3461" : "#1F2937",
                  color: active ? "#93C5FD" : "#6B7280",
                }}
              >
                {count}
              </span>
            )}
          </button>
        );
      })}
    </div>
  );
}
