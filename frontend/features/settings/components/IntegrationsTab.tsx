"use client";

import { useState } from "react";
import { useIntegrations } from "@/features/integrations/hooks/useIntegrations";
import { IntegrationCard } from "@/features/integrations/components/IntegrationCard";
import { IntegrationConfigModal } from "@/features/integrations/components/IntegrationConfigModal";
import { ALL_SERVICES, SERVICE_META } from "@/features/integrations/types";
import type { IntegrationService, ServiceMeta } from "@/features/integrations/types";

export function IntegrationsTab() {
  const { data: summaries, isLoading } = useIntegrations();
  const [configuring, setConfiguring] = useState<ServiceMeta | null>(null);

  const summaryMap = Object.fromEntries(
    (summaries ?? []).map((s) => [s.service, s]),
  );

  if (isLoading) {
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
        {ALL_SERVICES.map((service: IntegrationService) => (
          <IntegrationCard
            key={service}
            meta={SERVICE_META[service]}
            summary={summaryMap[service]}
            onConfigure={() => setConfiguring(SERVICE_META[service])}
          />
        ))}
      </div>

      {configuring && (
        <IntegrationConfigModal
          meta={configuring}
          onClose={() => setConfiguring(null)}
        />
      )}
    </div>
  );
}
