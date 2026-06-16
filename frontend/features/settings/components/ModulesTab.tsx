"use client";

import { useModules } from "@/features/modules/hooks/useModules";
import { ALL_MODULES, BUSINESS_TYPE_LABELS } from "@/features/modules/types";

export function ModulesTab() {
  const { data, isLoading, isError } = useModules();

  if (isLoading) {
    return (
      <div style={{ color: "#4B5563", fontSize: 13, padding: "16px 0" }}>
        Завантаження модулів…
      </div>
    );
  }

  if (isError || !data) {
    return (
      <div style={{ color: "#F87171", fontSize: 13, padding: "16px 0" }}>
        Не вдалося завантажити список модулів.
      </div>
    );
  }

  const activeSet = new Set(data.modules);
  const businessTypeLabel = BUSINESS_TYPE_LABELS[data.businessType] ?? data.businessType;

  return (
    <div>
      <div style={{ marginBottom: 24 }}>
        <h2 style={{ color: "#E8EDF5", fontSize: 16, fontWeight: 600, margin: 0 }}>
          Модулі
        </h2>
        <p style={{ color: "#4B5563", fontSize: 13, marginTop: 6 }}>
          Тип бізнесу: <strong style={{ color: "#9CA3AF" }}>{businessTypeLabel}</strong>
          {" — "}набір модулів визначається при реєстрації та керується провайдером.
        </p>
      </div>

      <div style={{ display: "flex", flexDirection: "column", gap: 10 }}>
        {ALL_MODULES.map((mod) => {
          const active = activeSet.has(mod.key);
          return (
            <div
              key={mod.key}
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
                aria-label={active ? "Активний модуль" : "Вимкнений модуль"}
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
                    {mod.label}
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
                    {active ? "Активно" : "Вимкнено"}
                  </span>
                </div>
                <p style={{ color: "#4B5563", fontSize: 12, margin: 0, lineHeight: 1.5 }}>
                  {mod.description}
                </p>
              </div>
            </div>
          );
        })}
      </div>

      <p style={{ color: "#374151", fontSize: 12, marginTop: 20, lineHeight: 1.6 }}>
        Активація чи вимкнення модулів виконується провайдером платформи.
        Зверніться до підтримки, якщо потрібно змінити набір модулів вашого тенанта.
      </p>
    </div>
  );
}
