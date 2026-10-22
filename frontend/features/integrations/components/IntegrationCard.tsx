"use client";

import { useTranslations, useLocale } from "next-intl";
import { Btn } from "@/components/ui/Btn";
import { StatusPill } from "@/components/ui/StatusPill";
import type { ServiceMeta, IntegrationSummary } from "../types";

interface Props {
  meta: ServiceMeta;
  summary: IntegrationSummary | undefined;
  onConfigure: () => void;
}

export function IntegrationCard({ meta, summary, onConfigure }: Props) {
  const t = useTranslations("Dashboard.integrations.card");
  const tServices = useTranslations("Dashboard.integrations.services");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";
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
            {tServices(`${meta.service}.label`)}
          </span>
          {isConfigured && (
            <StatusPill bg={isEnabled ? "#052e16" : "#1c1917"} text={isEnabled ? "#4ADE80" : "#6B7280"}>
              {isEnabled ? t("active") : t("disabled")}
            </StatusPill>
          )}
        </div>
        <p style={{ color: "#4B5563", fontSize: 12, margin: 0, lineHeight: 1.5 }}>
          {tServices(`${meta.service}.description`)}
        </p>
        {summary && (
          <p style={{ color: "#374151", fontSize: 11, margin: "4px 0 0" }}>
            {t("updatedAt", { date: new Date(summary.updatedAt).toLocaleDateString(intlLocale) })}
          </p>
        )}
      </div>

      {/* Action */}
      <Btn variant={isConfigured ? "ghost" : "primary"} onClick={onConfigure} style={{ flexShrink: 0 }}>
        {isConfigured ? t("configureButton") : t("connectButton")}
      </Btn>
    </div>
  );
}
