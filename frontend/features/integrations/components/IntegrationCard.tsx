"use client";

import type { ServiceMeta, IntegrationSummary } from "../types";

interface Props {
  meta: ServiceMeta;
  summary: IntegrationSummary | undefined;
  onConfigure: () => void;
}

export function IntegrationCard({ meta, summary, onConfigure }: Props) {
  const isConfigured = !!summary;
  const isEnabled = summary?.isEnabled ?? false;

  return (
    <div
      style={{
        background: "#111827",
        border: `1px solid ${isConfigured ? (isEnabled ? "#1e3a5f" : "#374151") : "#1F2937"}`,
        borderRadius: 12,
        padding: "20px 24px",
        display: "flex",
        alignItems: "center",
        gap: 16,
        transition: "border-color 0.15s",
      }}
    >
      {/* Icon */}
      <div
        style={{
          width: 48,
          height: 48,
          borderRadius: 10,
          background: "#1F2937",
          display: "flex",
          alignItems: "center",
          justifyContent: "center",
          fontSize: 22,
          flexShrink: 0,
        }}
      >
        {meta.icon}
      </div>

      {/* Info */}
      <div style={{ flex: 1, minWidth: 0 }}>
        <div style={{ display: "flex", alignItems: "center", gap: 8, marginBottom: 4 }}>
          <span style={{ color: "#E8EDF5", fontSize: 14, fontWeight: 600 }}>
            {meta.label}
          </span>
          {isConfigured && (
            <span
              style={{
                padding: "2px 8px",
                borderRadius: 4,
                background: isEnabled ? "#052e16" : "#1c1917",
                color: isEnabled ? "#4ADE80" : "#6B7280",
                fontSize: 11,
                fontWeight: 600,
              }}
            >
              {isEnabled ? "Активно" : "Вимкнено"}
            </span>
          )}
        </div>
        <p style={{ color: "#4B5563", fontSize: 12, margin: 0, lineHeight: 1.5 }}>
          {meta.description}
        </p>
        {summary && (
          <p style={{ color: "#374151", fontSize: 11, margin: "4px 0 0" }}>
            Оновлено: {new Date(summary.updatedAt).toLocaleDateString("uk-UA")}
          </p>
        )}
      </div>

      {/* Action */}
      <button
        onClick={onConfigure}
        style={{
          padding: "7px 16px",
          borderRadius: 8,
          background: isConfigured ? "transparent" : "#1D3461",
          border: `1px solid ${isConfigured ? "#374151" : "#3B82F6"}`,
          color: isConfigured ? "#9CA3AF" : "#93C5FD",
          fontSize: 13,
          fontWeight: 500,
          cursor: "pointer",
          whiteSpace: "nowrap",
          flexShrink: 0,
          transition: "all 0.15s",
        }}
        onMouseEnter={(e) => {
          (e.currentTarget as HTMLElement).style.borderColor = "#3B82F6";
          (e.currentTarget as HTMLElement).style.color = "#93C5FD";
        }}
        onMouseLeave={(e) => {
          (e.currentTarget as HTMLElement).style.borderColor = isConfigured ? "#374151" : "#3B82F6";
          (e.currentTarget as HTMLElement).style.color = isConfigured ? "#9CA3AF" : "#93C5FD";
        }}
      >
        {isConfigured ? "Налаштувати" : "Підключити"}
      </button>
    </div>
  );
}
