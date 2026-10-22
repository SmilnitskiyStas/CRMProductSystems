"use client";

import { useEffect, useState } from "react";
import { useTranslations } from "next-intl";
import { Modal } from "@/components/ui/Modal";
import { Btn } from "@/components/ui/Btn";
import { inputStyle, labelStyle } from "@/components/ui/fieldStyles";
import type { ServiceMeta, IntegrationService } from "../types";
import { useIntegration, useUpsertIntegration, useDeleteIntegration } from "../hooks/useIntegrations";

interface Props {
  meta: ServiceMeta;
  onClose: () => void;
}

const hintStyle: React.CSSProperties = {
  color: "#4B5563",
  fontSize: 11,
  marginTop: 4,
};

export function IntegrationConfigModal({ meta, onClose }: Props) {
  const t = useTranslations("Dashboard.integrations.configModal");
  const tServices = useTranslations("Dashboard.integrations.services");
  const { data: existing, isLoading } = useIntegration(meta.service, true);
  const upsert = useUpsertIntegration(meta.service);
  const remove = useDeleteIntegration(meta.service);

  // Local form state keyed by field.key
  const [values, setValues] = useState<Record<string, string>>({});
  const [isEnabled, setIsEnabled] = useState(true);
  const [errors, setErrors] = useState<Record<string, string>>({});

  // Pre-fill when existing config loads
  useEffect(() => {
    if (existing?.config) {
      const prefilled: Record<string, string> = {};
      for (const field of meta.fields) {
        const v = existing.config[field.key];
        prefilled[field.key] = typeof v === "string" ? v : "";
      }
      setValues(prefilled);
      setIsEnabled(existing.isEnabled);
    }
  }, [existing, meta.fields]);

  function validate(): boolean {
    const errs: Record<string, string> = {};
    for (const field of meta.fields) {
      if (field.required && !values[field.key]?.trim()) {
        errs[field.key] = t("requiredField");
      }
    }
    setErrors(errs);
    return Object.keys(errs).length === 0;
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!validate()) return;

    const config: Record<string, unknown> = {};
    for (const field of meta.fields) {
      if (values[field.key]?.trim()) {
        config[field.key] = values[field.key].trim();
      }
    }

    await upsert.mutateAsync({ config, isEnabled });
    onClose();
  }

  async function handleDelete() {
    if (!confirm(t("deleteConfirm", { label: tServices(`${meta.service}.label`) }))) return;
    await remove.mutateAsync();
    onClose();
  }

  const isBusy = upsert.isPending || remove.isPending;

  return (
    <Modal title={tServices(`${meta.service}.label`)} onClose={onClose} width={520}>
        <div style={{ display: "flex", alignItems: "center", gap: 12, marginBottom: 24 }}>
          <span style={{ fontSize: 28 }}>{meta.icon}</span>
          <p style={{ color: "#4B5563", fontSize: 12, margin: 0 }}>
            {tServices(`${meta.service}.description`)}
          </p>
        </div>

        {isLoading ? (
          <div style={{ color: "#4B5563", fontSize: 13, padding: "16px 0" }}>
            {t("loading")}
          </div>
        ) : (
          <form onSubmit={handleSubmit}>
            {/* Fields */}
            <div style={{ display: "flex", flexDirection: "column", gap: 16, marginBottom: 20 }}>
              {meta.fields.map((field) => {
                const fieldPrefix = `${meta.service}.fields.${field.key}`;
                return (
                  <div key={field.key}>
                    <label style={labelStyle}>
                      {tServices(`${fieldPrefix}.label`)}
                      {field.required && (
                        <span style={{ color: "#EF4444", marginLeft: 4 }}>*</span>
                      )}
                    </label>
                    <input
                      type={field.type}
                      placeholder={tServices(`${fieldPrefix}.placeholder`)}
                      value={values[field.key] ?? ""}
                      onChange={(e) => {
                        setValues((v) => ({ ...v, [field.key]: e.target.value }));
                        if (errors[field.key]) {
                          setErrors((er) => { const n = { ...er }; delete n[field.key]; return n; });
                        }
                      }}
                      style={{
                        ...inputStyle,
                        borderColor: errors[field.key] ? "#EF4444" : "#1F2937",
                      }}
                      autoComplete={field.type === "password" ? "new-password" : undefined}
                    />
                    {tServices.has(`${fieldPrefix}.hint`) && (
                      <p style={hintStyle}>{tServices(`${fieldPrefix}.hint`)}</p>
                    )}
                    {errors[field.key] && (
                      <p style={{ ...hintStyle, color: "#EF4444" }}>{errors[field.key]}</p>
                    )}
                  </div>
                );
              })}
            </div>

            {/* Enable toggle */}
            <div
              style={{
                display: "flex", alignItems: "center", justifyContent: "space-between",
                padding: "12px 0", borderTop: "1px solid #1F2937",
                borderBottom: "1px solid #1F2937", marginBottom: 20,
              }}
            >
              <div>
                <div style={{ color: "#D1D5DB", fontSize: 13, fontWeight: 500 }}>{t("activeIntegrationLabel")}</div>
                <div style={{ color: "#4B5563", fontSize: 12, marginTop: 2 }}>
                  {t("activeIntegrationHint")}
                </div>
              </div>
              <button
                type="button"
                role="switch"
                aria-checked={isEnabled}
                onClick={() => setIsEnabled((v) => !v)}
                style={{
                  width: 40, height: 22, borderRadius: 11,
                  background: isEnabled ? "#3B82F6" : "#374151",
                  border: "none", cursor: "pointer", position: "relative",
                  transition: "background 0.2s", flexShrink: 0,
                }}
              >
                <span
                  style={{
                    position: "absolute", top: 3, left: isEnabled ? 20 : 3,
                    width: 16, height: 16, borderRadius: "50%",
                    background: "#fff", transition: "left 0.2s",
                  }}
                />
              </button>
            </div>

            {/* Actions */}
            <div style={{ display: "flex", gap: 8, justifyContent: "space-between" }}>
              {existing && (
                <Btn variant="dangerOutline" onClick={handleDelete} disabled={isBusy}>
                  {t("deleteButton")}
                </Btn>
              )}
              <div style={{ display: "flex", gap: 8, marginLeft: "auto" }}>
                <Btn variant="ghost" onClick={onClose} disabled={isBusy}>
                  {t("cancelButton")}
                </Btn>
                <Btn type="submit" disabled={isBusy}>
                  {isBusy ? t("savingButton") : t("saveButton")}
                </Btn>
              </div>
            </div>
          </form>
        )}
    </Modal>
  );
}
