"use client";

import { useEffect, useState } from "react";
import { useTranslations } from "next-intl";
import { toast } from "sonner";
import { Switch } from "@/components/ui/switch";
import { Btn } from "@/components/ui/Btn";
import { useLoyaltySettings, useUpdateLoyaltySettings } from "../hooks/useLoyaltySettings";
import type { CustomerCodeFormat } from "../types";

// ── Style constants (matches PrroConfigModal / ChangePasswordForm conventions —
// this feature has no prior settings UI of its own, so it follows the closest existing
// analog: PrroSettingsController's frontend counterpart) ──────────────────────────────

const cardStyle: React.CSSProperties = {
  background: "#0D1117",
  border: "1px solid #1F2937",
  borderRadius: 12,
  padding: 24,
};

const inputStyle: React.CSSProperties = {
  width: "100%",
  background: "#0D1117",
  border: "1px solid #374151",
  borderRadius: 8,
  padding: "9px 12px",
  color: "#E8EDF5",
  fontSize: 13,
  outline: "none",
  boxSizing: "border-box",
};

const labelStyle: React.CSSProperties = {
  display: "block",
  color: "#9CA3AF",
  fontSize: 12,
  fontWeight: 500,
  marginBottom: 6,
};

const hintStyle: React.CSSProperties = {
  color: "#4B5563",
  fontSize: 11,
  marginTop: 4,
};

const fieldWrapStyle: React.CSSProperties = { marginTop: 16 };

const selectStyle: React.CSSProperties = {
  ...inputStyle,
  cursor: "pointer",
  appearance: "none",
  backgroundImage:
    "url(\"data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='12' height='12' viewBox='0 0 12 12'%3E%3Cpath fill='%236B7280' d='M6 8L1 3h10z'/%3E%3C/svg%3E\")",
  backgroundRepeat: "no-repeat",
  backgroundPosition: "right 12px center",
  paddingRight: 36,
};

interface FieldErrors {
  accrualRatePercent?: string;
  redemptionCapPercent?: string;
  minRedemptionBalance?: string;
  codeTtlSeconds?: string;
}

/**
 * TASK-500: "Бонусна програма" card on the Consumer App page — the first (and today, only)
 * section of that page. All 5 pre-existing LoyaltyProgramSettings fields (never had a UI before
 * this task) plus the new `customerCodeFormat` field (TASK-499 backend contract). Numeric bounds
 * mirror LoyaltyService.UpsertSettingsAsync's server-side validation exactly (accrual/cap 0-100,
 * min balance >= 0, TTL 5-300s) so the client never round-trips an obviously-invalid value.
 */
export function BonusProgramSection() {
  const t = useTranslations("Dashboard.consumerApp.bonusProgram");
  const { data: settings, isLoading, isError } = useLoyaltySettings();
  const update = useUpdateLoyaltySettings();

  const [isEnabled, setIsEnabled] = useState(false);
  const [accrualRatePercent, setAccrualRatePercent] = useState("3");
  const [redemptionCapPercent, setRedemptionCapPercent] = useState("50");
  const [minRedemptionBalance, setMinRedemptionBalance] = useState("0");
  const [codeTtlSeconds, setCodeTtlSeconds] = useState("30");
  const [customerCodeFormat, setCustomerCodeFormat] = useState<CustomerCodeFormat>("barcode");
  const [errors, setErrors] = useState<FieldErrors>({});

  // Pre-fill from whatever GetSettingsAsync returned — including its own proposed defaults
  // (3%/50%/0/30s/barcode, enabled) for a tenant that never saved a row, so this effect needs
  // no separate "is this new" branch of its own.
  useEffect(() => {
    if (!settings) return;
    setIsEnabled(settings.isEnabled);
    setAccrualRatePercent(String(settings.accrualRatePercent));
    setRedemptionCapPercent(String(settings.redemptionCapPercent));
    setMinRedemptionBalance(String(settings.minRedemptionBalance));
    setCodeTtlSeconds(String(settings.codeTtlSeconds));
    setCustomerCodeFormat(settings.customerCodeFormat);
  }, [settings]);

  function validate(): boolean {
    const e: FieldErrors = {};
    const accrual = Number(accrualRatePercent);
    const cap = Number(redemptionCapPercent);
    const minBalance = Number(minRedemptionBalance);
    const ttl = Number(codeTtlSeconds);

    if (!Number.isFinite(accrual) || accrual < 0 || accrual > 100) {
      e.accrualRatePercent = t("percentRangeError");
    }
    if (!Number.isFinite(cap) || cap < 0 || cap > 100) {
      e.redemptionCapPercent = t("percentRangeError");
    }
    if (!Number.isFinite(minBalance) || minBalance < 0) {
      e.minRedemptionBalance = t("nonNegativeError");
    }
    if (!Number.isInteger(ttl) || ttl < 5 || ttl > 300) {
      e.codeTtlSeconds = t("ttlRangeError");
    }

    setErrors(e);
    return Object.keys(e).length === 0;
  }

  function clearError(field: keyof FieldErrors) {
    setErrors((prev) => {
      if (!(field in prev)) return prev;
      const next = { ...prev };
      delete next[field];
      return next;
    });
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!validate() || update.isPending) return;

    try {
      await update.mutateAsync({
        isEnabled,
        accrualRatePercent: Number(accrualRatePercent),
        redemptionCapPercent: Number(redemptionCapPercent),
        minRedemptionBalance: Number(minRedemptionBalance),
        codeTtlSeconds: Number(codeTtlSeconds),
        customerCodeFormat,
      });
      toast.success(t("saveSuccess"));
    } catch (err) {
      toast.error(err instanceof Error ? err.message : t("saveError"));
    }
  }

  if (isLoading) {
    return (
      <div style={{ ...cardStyle, color: "#4B5563", fontSize: 13 }}>{t("loading")}</div>
    );
  }

  if (isError) {
    return (
      <div style={{ ...cardStyle, color: "#F87171", fontSize: 13 }}>{t("loadError")}</div>
    );
  }

  return (
    <div style={cardStyle}>
      {/* Header */}
      <div style={{ display: "flex", alignItems: "center", gap: 12, marginBottom: 20 }}>
        <span style={{ fontSize: 24 }}>🎁</span>
        <div>
          <h2 style={{ color: "#E8EDF5", fontSize: 15, fontWeight: 700, margin: 0 }}>
            {t("title")}
          </h2>
          <p style={{ color: "#4B5563", fontSize: 12, margin: 0, marginTop: 3 }}>
            {t("subtitle")}
          </p>
        </div>
      </div>

      <form onSubmit={handleSubmit}>
        {/* Enabled toggle */}
        <div
          style={{
            display: "flex",
            alignItems: "center",
            justifyContent: "space-between",
            gap: 12,
          }}
        >
          <div>
            <div style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 500 }}>
              {t("enabledLabel")}
            </div>
            <div style={{ color: "#4B5563", fontSize: 11, marginTop: 2 }}>
              {t("enabledHint")}
            </div>
          </div>
          <Switch checked={isEnabled} onCheckedChange={setIsEnabled} />
        </div>

        {/* Accrual rate */}
        <div style={fieldWrapStyle}>
          <label style={labelStyle}>{t("accrualRateLabel")}</label>
          <input
            type="number"
            min={0}
            max={100}
            step="0.1"
            value={accrualRatePercent}
            onChange={(e) => {
              setAccrualRatePercent(e.target.value);
              clearError("accrualRatePercent");
            }}
            style={{
              ...inputStyle,
              borderColor: errors.accrualRatePercent ? "#EF4444" : "#374151",
            }}
          />
          {errors.accrualRatePercent && (
            <p style={{ ...hintStyle, color: "#EF4444" }}>{errors.accrualRatePercent}</p>
          )}
          <p style={hintStyle}>{t("accrualRateHint")}</p>
        </div>

        {/* Redemption cap */}
        <div style={fieldWrapStyle}>
          <label style={labelStyle}>{t("redemptionCapLabel")}</label>
          <input
            type="number"
            min={0}
            max={100}
            step="0.1"
            value={redemptionCapPercent}
            onChange={(e) => {
              setRedemptionCapPercent(e.target.value);
              clearError("redemptionCapPercent");
            }}
            style={{
              ...inputStyle,
              borderColor: errors.redemptionCapPercent ? "#EF4444" : "#374151",
            }}
          />
          {errors.redemptionCapPercent && (
            <p style={{ ...hintStyle, color: "#EF4444" }}>{errors.redemptionCapPercent}</p>
          )}
          <p style={hintStyle}>{t("redemptionCapHint")}</p>
        </div>

        {/* Min redemption balance */}
        <div style={fieldWrapStyle}>
          <label style={labelStyle}>{t("minRedemptionBalanceLabel")}</label>
          <input
            type="number"
            min={0}
            step="0.01"
            value={minRedemptionBalance}
            onChange={(e) => {
              setMinRedemptionBalance(e.target.value);
              clearError("minRedemptionBalance");
            }}
            style={{
              ...inputStyle,
              borderColor: errors.minRedemptionBalance ? "#EF4444" : "#374151",
            }}
          />
          {errors.minRedemptionBalance && (
            <p style={{ ...hintStyle, color: "#EF4444" }}>{errors.minRedemptionBalance}</p>
          )}
          <p style={hintStyle}>{t("minRedemptionBalanceHint")}</p>
        </div>

        {/* Code TTL */}
        <div style={fieldWrapStyle}>
          <label style={labelStyle}>{t("codeTtlLabel")}</label>
          <input
            type="number"
            min={5}
            max={300}
            step="1"
            value={codeTtlSeconds}
            onChange={(e) => {
              setCodeTtlSeconds(e.target.value);
              clearError("codeTtlSeconds");
            }}
            style={{
              ...inputStyle,
              borderColor: errors.codeTtlSeconds ? "#EF4444" : "#374151",
            }}
          />
          {errors.codeTtlSeconds && (
            <p style={{ ...hintStyle, color: "#EF4444" }}>{errors.codeTtlSeconds}</p>
          )}
          <p style={hintStyle}>{t("codeTtlHint")}</p>
        </div>

        {/* Customer code format (TASK-500) */}
        <div style={fieldWrapStyle}>
          <label style={labelStyle}>{t("codeFormatLabel")}</label>
          <select
            value={customerCodeFormat}
            onChange={(e) => setCustomerCodeFormat(e.target.value as CustomerCodeFormat)}
            style={selectStyle}
          >
            <option value="qr">{t("codeFormatQr")}</option>
            <option value="barcode">{t("codeFormatBarcode")}</option>
          </select>
          <p style={hintStyle}>{t("codeFormatHint")}</p>
        </div>

        {/* Actions */}
        <div
          style={{
            display: "flex",
            alignItems: "center",
            gap: 12,
            marginTop: 24,
            paddingTop: 20,
            borderTop: "1px solid #1F2937",
          }}
        >
          <Btn type="submit" disabled={update.isPending}>
            {update.isPending ? t("savingButton") : t("saveButton")}
          </Btn>
          {settings?.updatedAt && (
            <span style={{ color: "#4B5563", fontSize: 12 }}>
              {t("lastUpdatedPrefix")} {new Date(settings.updatedAt).toLocaleString()}
            </span>
          )}
        </div>
      </form>
    </div>
  );
}
