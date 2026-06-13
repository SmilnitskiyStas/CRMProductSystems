"use client";

import { useEffect, useState } from "react";
import {
  CHECKBOX_BASE_URLS,
  detectCheckboxEnv,
} from "../api/prro";
import type { PrroProvider, CheckboxEnv, PrroTestResult } from "../api/prro";
import {
  usePrroSettings,
  useUpdatePrroSettings,
  useTestPrroConnection,
} from "../hooks/usePrroSettings";

// ── Style constants (consistent with the rest of the settings UI) ──────────

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

const sectionStyle: React.CSSProperties = {
  marginTop: 20,
  paddingTop: 20,
  borderTop: "1px solid #1F2937",
};

// ── Cashier auth mode ──────────────────────────────────────────────────────

type CashierAuthMode = "pin" | "loginPassword";

// Placeholder sentinel sent when the user hasn't changed a masked secret
const MASKED_UNCHANGED = "••••••••";

interface Props {
  onClose: () => void;
}

export function PrroConfigModal({ onClose }: Props) {
  const { data: settings, isLoading } = usePrroSettings(true);
  const update = useUpdatePrroSettings();
  const testConn = useTestPrroConnection();

  // ── Form state ────────────────────────────────────────────────────────────
  const [provider, setProvider] = useState<PrroProvider>("disabled");
  const [checkboxEnv, setCheckboxEnv] = useState<CheckboxEnv>("test");
  const [licenseKey, setLicenseKey] = useState("");
  const [cashierAuthMode, setCashierAuthMode] = useState<CashierAuthMode>("pin");
  const [cashierPin, setCashierPin] = useState("");
  const [cashierLogin, setCashierLogin] = useState("");
  const [cashierPassword, setCashierPassword] = useState("");

  // ── Test result state ─────────────────────────────────────────────────────
  const [testResult, setTestResult] = useState<PrroTestResult | null>(null);
  const [testError, setTestError] = useState<string | null>(null);

  // ── Errors ────────────────────────────────────────────────────────────────
  const [errors, setErrors] = useState<Record<string, string>>({});

  // Pre-fill when settings load
  useEffect(() => {
    if (!settings) return;
    setProvider(settings.provider);
    setCheckboxEnv(detectCheckboxEnv(settings.baseUrl));
    setLicenseKey(settings.licenseKey ?? "");

    // Detect cashier auth mode from which field has a value
    if (settings.cashierPinCode) {
      setCashierAuthMode("pin");
      setCashierPin(settings.cashierPinCode);
    } else {
      setCashierAuthMode("loginPassword");
      setCashierLogin(settings.cashierLogin ?? "");
      setCashierPassword(settings.cashierPassword ?? "");
    }
  }, [settings]);

  // ── Validation ────────────────────────────────────────────────────────────
  function validate(): boolean {
    if (provider !== "checkbox") return true;

    const errs: Record<string, string> = {};
    if (!licenseKey.trim()) errs.licenseKey = "Обов'язкове поле";
    if (cashierAuthMode === "pin") {
      if (!cashierPin.trim()) errs.cashierPin = "Обов'язкове поле";
    } else {
      if (!cashierLogin.trim()) errs.cashierLogin = "Обов'язкове поле";
      if (!cashierPassword.trim()) errs.cashierPassword = "Обов'язкове поле";
    }
    setErrors(errs);
    return Object.keys(errs).length === 0;
  }

  // ── Submit ────────────────────────────────────────────────────────────────
  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!validate()) return;

    const baseUrl =
      provider === "checkbox" ? CHECKBOX_BASE_URLS[checkboxEnv] : "";

    await update.mutateAsync({
      provider,
      baseUrl,
      licenseKey:
        provider === "checkbox"
          ? licenseKey || MASKED_UNCHANGED
          : "",
      cashierLogin:
        provider === "checkbox" && cashierAuthMode === "loginPassword"
          ? cashierLogin || MASKED_UNCHANGED
          : "",
      cashierPassword:
        provider === "checkbox" && cashierAuthMode === "loginPassword"
          ? cashierPassword || MASKED_UNCHANGED
          : "",
      cashierPinCode:
        provider === "checkbox" && cashierAuthMode === "pin"
          ? cashierPin || MASKED_UNCHANGED
          : "",
    });
    onClose();
  }

  // ── Test connection ───────────────────────────────────────────────────────
  // Always pass the current form state so the test uses just-entered credentials
  // without requiring a save first. The backend merges masked placeholders with
  // stored secrets, so unchanged fields keep their stored value.
  async function handleTest() {
    setTestResult(null);
    setTestError(null);
    try {
      const result = await testConn.mutateAsync({
        provider,
        baseUrl: provider === "checkbox" ? CHECKBOX_BASE_URLS[checkboxEnv] : "",
        licenseKey: licenseKey || MASKED_UNCHANGED,
        cashierPinCode:
          provider === "checkbox" && cashierAuthMode === "pin"
            ? cashierPin || MASKED_UNCHANGED
            : "",
        cashierLogin:
          provider === "checkbox" && cashierAuthMode === "loginPassword"
            ? cashierLogin || MASKED_UNCHANGED
            : "",
        cashierPassword:
          provider === "checkbox" && cashierAuthMode === "loginPassword"
            ? cashierPassword || MASKED_UNCHANGED
            : "",
      });
      setTestResult(result);
    } catch (err) {
      setTestError(err instanceof Error ? err.message : "Помилка підключення");
    }
  }

  function clearError(field: string) {
    setErrors((prev) => {
      const next = { ...prev };
      delete next[field];
      return next;
    });
  }

  const isBusy = update.isPending;
  const isTesting = testConn.isPending;

  return (
    <>
      {/* Backdrop */}
      <div
        onClick={onClose}
        style={{
          position: "fixed",
          inset: 0,
          background: "rgba(0,0,0,0.6)",
          zIndex: 40,
          backdropFilter: "blur(2px)",
        }}
      />

      {/* Modal */}
      <div
        style={{
          position: "fixed",
          top: "50%",
          left: "50%",
          transform: "translate(-50%, -50%)",
          zIndex: 50,
          background: "#111827",
          border: "1px solid #1F2937",
          borderRadius: 14,
          padding: 28,
          width: "min(560px, calc(100vw - 32px))",
          maxHeight: "90vh",
          overflowY: "auto",
        }}
      >
        {/* Header */}
        <div style={{ display: "flex", alignItems: "center", gap: 12, marginBottom: 24 }}>
          <span style={{ fontSize: 28 }}>🖨️</span>
          <div>
            <h2 style={{ color: "#E8EDF5", fontSize: 16, fontWeight: 700, margin: 0 }}>
              ПРРО Каса
            </h2>
            <p style={{ color: "#4B5563", fontSize: 12, margin: 0, marginTop: 3 }}>
              Інтеграція з програмним РРО для реєстрації продажів (v3).
            </p>
          </div>
          <button
            onClick={onClose}
            style={{
              marginLeft: "auto",
              background: "transparent",
              border: "none",
              color: "#4B5563",
              fontSize: 20,
              cursor: "pointer",
              lineHeight: 1,
              padding: "4px 8px",
            }}
          >
            ✕
          </button>
        </div>

        {isLoading ? (
          <div style={{ color: "#4B5563", fontSize: 13, padding: "16px 0" }}>
            Завантаження…
          </div>
        ) : (
          <form onSubmit={handleSubmit}>
            {/* ── Provider select ─────────────────────────────────────────── */}
            <div>
              <label style={labelStyle}>Провайдер</label>
              <select
                value={provider}
                onChange={(e) => {
                  setProvider(e.target.value as PrroProvider);
                  setTestResult(null);
                  setTestError(null);
                }}
                style={{
                  ...inputStyle,
                  cursor: "pointer",
                  appearance: "none",
                  backgroundImage: `url("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='12' height='12' viewBox='0 0 12 12'%3E%3Cpath fill='%236B7280' d='M6 8L1 3h10z'/%3E%3C/svg%3E")`,
                  backgroundRepeat: "no-repeat",
                  backgroundPosition: "right 12px center",
                  paddingRight: 36,
                }}
              >
                <option value="disabled">Вимкнено</option>
                <option value="checkbox">Checkbox</option>
              </select>
            </div>

            {/* ── Checkbox-specific fields ────────────────────────────────── */}
            {provider === "checkbox" && (
              <>
                {/* Base URL (environment) */}
                <div style={sectionStyle}>
                  <label style={labelStyle}>Середовище Checkbox</label>
                  <div style={{ display: "flex", gap: 12 }}>
                    {(["test", "prod"] as CheckboxEnv[]).map((env) => (
                      <label
                        key={env}
                        style={{
                          display: "flex",
                          alignItems: "center",
                          gap: 8,
                          cursor: "pointer",
                          color: checkboxEnv === env ? "#93C5FD" : "#9CA3AF",
                          fontSize: 13,
                        }}
                      >
                        <input
                          type="radio"
                          name="checkboxEnv"
                          value={env}
                          checked={checkboxEnv === env}
                          onChange={() => setCheckboxEnv(env)}
                          style={{ accentColor: "#3B82F6" }}
                        />
                        {env === "test" ? "Тестовий" : "Виробничий"}
                      </label>
                    ))}
                  </div>
                  <p style={hintStyle}>
                    {checkboxEnv === "test"
                      ? CHECKBOX_BASE_URLS.test
                      : CHECKBOX_BASE_URLS.prod}
                  </p>
                </div>

                {/* License key */}
                <div style={{ marginTop: 16 }}>
                  <label style={labelStyle}>
                    License Key{" "}
                    <span style={{ color: "#EF4444" }}>*</span>
                  </label>
                  <input
                    type="password"
                    placeholder="залишити без змін"
                    value={licenseKey}
                    onChange={(e) => {
                      setLicenseKey(e.target.value);
                      clearError("licenseKey");
                    }}
                    style={{
                      ...inputStyle,
                      borderColor: errors.licenseKey ? "#EF4444" : "#374151",
                    }}
                    autoComplete="new-password"
                  />
                  {errors.licenseKey && (
                    <p style={{ ...hintStyle, color: "#EF4444" }}>{errors.licenseKey}</p>
                  )}
                  <p style={hintStyle}>
                    Ключ ліцензії каси з кабінету Checkbox. Вже збережений ключ відображається замаскованим.
                  </p>
                </div>

                {/* Cashier auth mode toggle */}
                <div style={sectionStyle}>
                  <label style={labelStyle}>Авторизація касира</label>
                  <div style={{ display: "flex", gap: 0, borderRadius: 8, overflow: "hidden", border: "1px solid #374151", width: "fit-content" }}>
                    {(["pin", "loginPassword"] as CashierAuthMode[]).map((mode) => (
                      <button
                        key={mode}
                        type="button"
                        onClick={() => {
                          setCashierAuthMode(mode);
                          setErrors({});
                        }}
                        style={{
                          padding: "7px 16px",
                          background: cashierAuthMode === mode ? "#1D3461" : "transparent",
                          border: "none",
                          color: cashierAuthMode === mode ? "#93C5FD" : "#6B7280",
                          fontSize: 13,
                          cursor: "pointer",
                          fontWeight: cashierAuthMode === mode ? 600 : 400,
                          transition: "all 0.15s",
                        }}
                      >
                        {mode === "pin" ? "PIN-код" : "Логін / Пароль"}
                      </button>
                    ))}
                  </div>
                </div>

                {/* PIN mode */}
                {cashierAuthMode === "pin" && (
                  <div style={{ marginTop: 16 }}>
                    <label style={labelStyle}>
                      PIN-код касира <span style={{ color: "#EF4444" }}>*</span>
                    </label>
                    <input
                      type="password"
                      placeholder="залишити без змін"
                      value={cashierPin}
                      onChange={(e) => {
                        setCashierPin(e.target.value);
                        clearError("cashierPin");
                      }}
                      style={{
                        ...inputStyle,
                        borderColor: errors.cashierPin ? "#EF4444" : "#374151",
                      }}
                      autoComplete="new-password"
                    />
                    {errors.cashierPin && (
                      <p style={{ ...hintStyle, color: "#EF4444" }}>{errors.cashierPin}</p>
                    )}
                  </div>
                )}

                {/* Login + Password mode */}
                {cashierAuthMode === "loginPassword" && (
                  <div style={{ marginTop: 16, display: "flex", flexDirection: "column", gap: 16 }}>
                    <div>
                      <label style={labelStyle}>
                        Логін касира <span style={{ color: "#EF4444" }}>*</span>
                      </label>
                      <input
                        type="password"
                        placeholder="залишити без змін"
                        value={cashierLogin}
                        onChange={(e) => {
                          setCashierLogin(e.target.value);
                          clearError("cashierLogin");
                        }}
                        style={{
                          ...inputStyle,
                          borderColor: errors.cashierLogin ? "#EF4444" : "#374151",
                        }}
                        autoComplete="new-password"
                      />
                      {errors.cashierLogin && (
                        <p style={{ ...hintStyle, color: "#EF4444" }}>{errors.cashierLogin}</p>
                      )}
                    </div>
                    <div>
                      <label style={labelStyle}>
                        Пароль касира <span style={{ color: "#EF4444" }}>*</span>
                      </label>
                      <input
                        type="password"
                        placeholder="залишити без змін"
                        value={cashierPassword}
                        onChange={(e) => {
                          setCashierPassword(e.target.value);
                          clearError("cashierPassword");
                        }}
                        style={{
                          ...inputStyle,
                          borderColor: errors.cashierPassword ? "#EF4444" : "#374151",
                        }}
                        autoComplete="new-password"
                      />
                      {errors.cashierPassword && (
                        <p style={{ ...hintStyle, color: "#EF4444" }}>{errors.cashierPassword}</p>
                      )}
                    </div>
                  </div>
                )}

                {/* Test connection button + result */}
                <div style={{ marginTop: 20, padding: 16, background: "#0D1117", borderRadius: 10, border: "1px solid #1F2937" }}>
                  <div style={{ display: "flex", alignItems: "center", gap: 12, flexWrap: "wrap" }}>
                    <button
                      type="button"
                      onClick={handleTest}
                      disabled={isTesting}
                      style={{
                        padding: "7px 16px",
                        borderRadius: 8,
                        background: "transparent",
                        border: "1px solid #374151",
                        color: "#9CA3AF",
                        fontSize: 13,
                        cursor: isTesting ? "not-allowed" : "pointer",
                        opacity: isTesting ? 0.6 : 1,
                        whiteSpace: "nowrap",
                      }}
                    >
                      {isTesting ? "Перевірка…" : "Перевірити з'єднання"}
                    </button>

                    {/* Success result */}
                    {testResult && testResult.ok && (
                      <div style={{ fontSize: 12, color: "#4ADE80" }}>
                        <span style={{ fontWeight: 700 }}>✓</span> Каса{" "}
                        <span style={{ fontFamily: "monospace" }}>{testResult.fiscalNumber}</span>
                        {testResult.isTest && (
                          <span
                            style={{
                              marginLeft: 6,
                              padding: "1px 6px",
                              borderRadius: 4,
                              background: "#052e16",
                              color: "#86efac",
                              fontSize: 10,
                            }}
                          >
                            тест
                          </span>
                        )}{" "}
                        {testResult.cashierOk ? (
                          <>· <span style={{ color: "#4ADE80" }}>✓</span> Касир авторизований</>
                        ) : (
                          <>· <span style={{ color: "#FCA5A5" }}>✗</span> Касир: помилка авторизації</>
                        )}
                      </div>
                    )}

                    {/* ok=false but register was found — cashier credentials failed */}
                    {testResult && !testResult.ok && testResult.fiscalNumber && (
                      <div style={{ fontSize: 12, color: "#F87171" }}>
                        <span style={{ color: "#4ADE80", fontWeight: 700 }}>✓</span>{" "}
                        <span style={{ color: "#9CA3AF" }}>Каса</span>{" "}
                        <span style={{ fontFamily: "monospace", color: "#9CA3AF" }}>{testResult.fiscalNumber}</span>
                        {testResult.isTest && (
                          <span
                            style={{
                              marginLeft: 6,
                              padding: "1px 6px",
                              borderRadius: 4,
                              background: "#052e16",
                              color: "#86efac",
                              fontSize: 10,
                            }}
                          >
                            тест
                          </span>
                        )}
                        {" · "}
                        <span style={{ fontWeight: 700 }}>✗</span>{" "}
                        {testResult.error ?? "Помилка авторизації касира"}
                      </div>
                    )}

                    {/* ok=false, no register info — register not found or network error */}
                    {testResult && !testResult.ok && !testResult.fiscalNumber && (
                      <div style={{ fontSize: 12, color: "#F87171" }}>
                        <span style={{ fontWeight: 700 }}>✗</span>{" "}
                        {testResult.error ?? "Сервіс недоступний"}
                      </div>
                    )}

                    {/* Network / API error */}
                    {testError && (
                      <div style={{ fontSize: 12, color: "#F87171" }}>
                        <span style={{ fontWeight: 700 }}>✗</span> {testError}
                      </div>
                    )}
                  </div>
                </div>
              </>
            )}

            {/* ── Actions ──────────────────────────────────────────────────── */}
            <div
              style={{
                display: "flex",
                gap: 8,
                justifyContent: "flex-end",
                marginTop: 24,
                paddingTop: 20,
                borderTop: "1px solid #1F2937",
              }}
            >
              <button
                type="button"
                onClick={onClose}
                disabled={isBusy}
                style={{
                  padding: "8px 16px",
                  borderRadius: 8,
                  background: "transparent",
                  border: "1px solid #374151",
                  color: "#6B7280",
                  fontSize: 13,
                  cursor: "pointer",
                }}
              >
                Скасувати
              </button>
              <button
                type="submit"
                disabled={isBusy}
                style={{
                  padding: "8px 20px",
                  borderRadius: 8,
                  background: isBusy ? "#1e3a5f" : "#1D3461",
                  border: "1px solid #3B82F6",
                  color: "#93C5FD",
                  fontSize: 13,
                  fontWeight: 600,
                  cursor: isBusy ? "not-allowed" : "pointer",
                  opacity: isBusy ? 0.7 : 1,
                }}
              >
                {isBusy ? "Збереження…" : "Зберегти"}
              </button>
            </div>
          </form>
        )}
      </div>
    </>
  );
}
