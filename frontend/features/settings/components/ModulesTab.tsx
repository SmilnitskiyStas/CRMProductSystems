"use client";

import { useTranslations } from "next-intl";
import { useModules } from "@/features/modules/hooks/useModules";
import { ALL_MODULE_KEYS } from "@/features/modules/types";

export function ModulesTab() {
  const t = useTranslations("Dashboard.settings.modulesTab");
  const tCatalog = useTranslations("Dashboard.modules.catalog");
  const tBusinessTypes = useTranslations("Dashboard.modules.businessTypes");
  const { data, isLoading, isError } = useModules();

  if (isLoading) {
    return (
      <div style={{ color: "#4B5563", fontSize: 13, padding: "16px 0" }}>
        {t("loading")}
      </div>
    );
  }

  if (isError || !data) {
    return (
      <div style={{ color: "#F87171", fontSize: 13, padding: "16px 0" }}>
        {t("loadError")}
      </div>
    );
  }

  const activeSet = new Set(data.modules);
  const businessTypeLabel = tBusinessTypes.has(data.businessType)
    ? tBusinessTypes(data.businessType)
    : data.businessType;

  return (
    <div>
      <div style={{ marginBottom: 24 }}>
        <h2 style={{ color: "#E8EDF5", fontSize: 16, fontWeight: 600, margin: 0 }}>
          {t("title")}
        </h2>
        <p style={{ color: "#4B5563", fontSize: 13, marginTop: 6 }}>
          {t("businessTypePrefix")} <strong style={{ color: "#9CA3AF" }}>{businessTypeLabel}</strong>
          {" — "}{t("businessTypeSuffix")}
        </p>
      </div>

      <div style={{ display: "flex", flexDirection: "column", gap: 10 }}>
        {ALL_MODULE_KEYS.map((key) => {
          const active = activeSet.has(key);
          return (
            <div
              key={key}
              style={{
                background: "#111827",
                border: `1px solid ${active ? "#1e3a5f" : "#1F2937"}`,
                borderRadius: 12,
                padding: "16px 20px",
                display: "flex",
                alignItems: "center",
                gap: 16,
              }}
            >
              {/* Toggle indicator (read-only — activation is provider-managed) */}
              <div
                role="img"
                aria-label={active ? t("activeModuleAria") : t("disabledModuleAria")}
                style={{
                  width: 38,
                  height: 22,
                  borderRadius: 11,
                  background: active ? "#1D4ED8" : "#1F2937",
                  position: "relative",
                  flexShrink: 0,
                }}
              >
                <div
                  style={{
                    width: 16,
                    height: 16,
                    borderRadius: "50%",
                    background: "#E8EDF5",
                    position: "absolute",
                    top: 3,
                    left: active ? 19 : 3,
                    transition: "left 0.15s",
                  }}
                />
              </div>

              <div style={{ flex: 1, minWidth: 0 }}>
                <div style={{ display: "flex", alignItems: "center", gap: 8, marginBottom: 4 }}>
                  <span style={{ color: "#E8EDF5", fontSize: 14, fontWeight: 600 }}>
                    {tCatalog(`${key}.label`)}
                  </span>
                  <span
                    style={{
                      padding: "2px 8px",
                      borderRadius: 4,
                      background: active ? "#052e16" : "#1c1917",
                      color: active ? "#4ADE80" : "#6B7280",
                      fontSize: 11,
                      fontWeight: 600,
                    }}
                  >
                    {active ? t("statusActive") : t("statusDisabled")}
                  </span>
                </div>
                <p style={{ color: "#4B5563", fontSize: 12, margin: 0, lineHeight: 1.5 }}>
                  {tCatalog(`${key}.description`)}
                </p>
              </div>
            </div>
          );
        })}
      </div>

      <p style={{ color: "#374151", fontSize: 12, marginTop: 20, lineHeight: 1.6 }}>
        {t("footerNote")}
      </p>
    </div>
  );
}
