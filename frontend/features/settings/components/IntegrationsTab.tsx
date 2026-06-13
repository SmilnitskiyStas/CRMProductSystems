"use client";

import { useState } from "react";
import { useIntegrations } from "@/features/integrations/hooks/useIntegrations";
import { usePrroSettings } from "@/features/integrations/hooks/usePrroSettings";
import { IntegrationCard } from "@/features/integrations/components/IntegrationCard";
import { IntegrationConfigModal } from "@/features/integrations/components/IntegrationConfigModal";
import { PrroConfigModal } from "@/features/integrations/components/PrroConfigModal";
import { ALL_SERVICES, SERVICE_META } from "@/features/integrations/types";
import type { IntegrationService, ServiceMeta } from "@/features/integrations/types";

/** Non-PRRO services that still use the generic IntegrationConfigModal */
const GENERIC_SERVICES = ALL_SERVICES.filter((s) => s !== "prro");

export function IntegrationsTab() {
  const { data: summaries, isLoading: summariesLoading } = useIntegrations();
  const { data: prroSettings, isLoading: prroLoading } = usePrroSettings();

  const [configuring, setConfiguring] = useState<ServiceMeta | null>(null);
  const [prroOpen, setPrroOpen] = useState(false);

  const summaryMap = Object.fromEntries(
    (summaries ?? []).map((s) => [s.service, s]),
  );

  // Derive a synthetic summary for PRRO from /api/settings/prro
  const prroConfigured = prroSettings?.provider === "checkbox";
  const prroSummary = prroSettings
    ? {
        service: "prro" as IntegrationService,
        isEnabled: prroConfigured,
        updatedAt: new Date().toISOString(), // not exposed by the API; use now as fallback
      }
    : summaryMap["prro"]; // fall back to generic summary if PRRO settings not loaded yet

  if (summariesLoading || prroLoading) {
    return (
      <div style={{ color: "#4B5563", fontSize: 13, padding: "16px 0" }}>
        Завантаження інтеграцій…
      </div>
    );
  }

  return (
    <div>
      <div style={{ marginBottom: 24 }}>
        <h2 style={{ color: "#E8EDF5", fontSize: 16, fontWeight: 600, margin: 0 }}>
          Зовнішні інтеграції
        </h2>
        <p style={{ color: "#4B5563", fontSize: 13, marginTop: 6 }}>
          Підключіть зовнішні сервіси для сповіщень, аналітики та автоматизації.
        </p>
      </div>

      <div style={{ display: "flex", flexDirection: "column", gap: 10 }}>
        {/* ПРРО card — dedicated modal */}
        <PrroCard
          provider={prroSettings?.provider ?? "disabled"}
          summary={prroSummary}
          onConfigure={() => setPrroOpen(true)}
        />

        {/* All other integrations — generic modal */}
        {GENERIC_SERVICES.map((service: IntegrationService) => (
          <IntegrationCard
            key={service}
            meta={SERVICE_META[service]}
            summary={summaryMap[service]}
            onConfigure={() => setConfiguring(SERVICE_META[service])}
          />
        ))}
      </div>

      {prroOpen && <PrroConfigModal onClose={() => setPrroOpen(false)} />}

      {configuring && (
        <IntegrationConfigModal
          meta={configuring}
          onClose={() => setConfiguring(null)}
        />
      )}
    </div>
  );
}

// ── Inline ПРРО card (provider-aware badge) ────────────────────────────────

interface PrroCardProps {
  provider: "checkbox" | "disabled";
  summary: { service: IntegrationService; isEnabled: boolean; updatedAt: string } | undefined;
  onConfigure: () => void;
}

function PrroCard({ provider, summary, onConfigure }: PrroCardProps) {
  const isConfigured = provider === "checkbox";

  const providerLabel = isConfigured ? "Checkbox" : "Вимкнено";
  const badgeBg = isConfigured ? "#052e16" : "#1c1917";
  const badgeColor = isConfigured ? "#4ADE80" : "#6B7280";

  return (
    <div
      style={{
        background: "#111827",
        border: `1px solid ${isConfigured ? "#1e3a5f" : "#1F2937"}`,
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
        🖨️
      </div>

      {/* Info */}
      <div style={{ flex: 1, minWidth: 0 }}>
        <div style={{ display: "flex", alignItems: "center", gap: 8, marginBottom: 4, flexWrap: "wrap" }}>
          <span style={{ color: "#E8EDF5", fontSize: 14, fontWeight: 600 }}>
            ПРРО Каса
          </span>
          <span
            style={{
              padding: "2px 8px",
              borderRadius: 4,
              background: badgeBg,
              color: badgeColor,
              fontSize: 11,
              fontWeight: 600,
            }}
          >
            {providerLabel}
          </span>
        </div>
        <p style={{ color: "#4B5563", fontSize: 12, margin: 0, lineHeight: 1.5 }}>
          Інтеграція з програмним РРО для реєстрації продажів (v3). Провайдер:{" "}
          <span style={{ color: isConfigured ? "#93C5FD" : "#6B7280" }}>
            {isConfigured ? "Checkbox" : "не підключено"}
          </span>
          .
        </p>
        {summary && isConfigured && (
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
      >
        {isConfigured ? "Налаштувати" : "Підключити"}
      </button>
    </div>
  );
}
