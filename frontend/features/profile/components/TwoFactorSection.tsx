"use client";

import { useState } from "react";
import { QRCodeSVG } from "qrcode.react";
import { toast } from "sonner";
import { ShieldCheck, ShieldOff, Copy, AlertTriangle } from "lucide-react";
import { useMe } from "@/features/auth/hooks/useAuth";
import {
  useTwoFactorSetup,
  useTwoFactorEnable,
  useTwoFactorDisable,
} from "../hooks/useProfile";
import { Btn } from "@/components/ui/Btn";
import { Modal } from "@/components/ui/Modal";

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
  fontSize: 12,
  margin: 0,
  lineHeight: 1.5,
};

const errorStyle: React.CSSProperties = {
  color: "#F87171",
  fontSize: 12,
  margin: "8px 0 0",
};

function copyText(text: string, message: string) {
  navigator.clipboard
    .writeText(text)
    .then(() => toast.success(message))
    .catch(() => toast.error("Не вдалося скопіювати"));
}

type Step = "idle" | "setup" | "recovery";

export function TwoFactorSection() {
  const { data: me } = useMe();
  const setup = useTwoFactorSetup();
  const enable = useTwoFactorEnable();
  const disable = useTwoFactorDisable();

  const [step, setStep] = useState<Step>("idle");
  const [enableCode, setEnableCode] = useState("");
  const [enableError, setEnableError] = useState<string | null>(null);
  const [recoveryCodes, setRecoveryCodes] = useState<string[]>([]);
  const [savedConfirmed, setSavedConfirmed] = useState(false);

  // Disable dialog state
  const [disableOpen, setDisableOpen] = useState(false);
  const [disablePassword, setDisablePassword] = useState("");
  const [disableCode, setDisableCode] = useState("");
  const [disableError, setDisableError] = useState<string | null>(null);

  const enabled = Boolean(me?.twoFactorEnabled);

  // ---- Enable flow ----

  function startSetup() {
    setEnableCode("");
    setEnableError(null);
    enable.reset();
    setup.mutate(undefined, {
      onSuccess: () => setStep("setup"),
      onError: (e) => toast.error(e.message),
    });
  }

  function cancelSetup() {
    setStep("idle");
    setEnableCode("");
    setEnableError(null);
    setup.reset();
    enable.reset();
  }

  function submitEnable(e: React.FormEvent) {
    e.preventDefault();
    if (enable.isPending) return;
    if (!/^\d{6}$/.test(enableCode.trim())) {
      setEnableError("Введіть 6-значний код із застосунку");
      return;
    }
    setEnableError(null);
    enable.mutate(enableCode.trim(), {
      onSuccess: (data) => {
        setRecoveryCodes(data.recoveryCodes);
        setSavedConfirmed(false);
        setStep("recovery");
        toast.success("Двофакторну автентифікацію увімкнено");
      },
      onError: (err) => setEnableError(err.message === "Invalid code." ? "Невірний код" : err.message),
    });
  }

  function finishRecovery() {
    if (!savedConfirmed) return;
    setRecoveryCodes([]);
    setStep("idle");
  }

  // ---- Disable flow ----

  function openDisable() {
    setDisablePassword("");
    setDisableCode("");
    setDisableError(null);
    disable.reset();
    setDisableOpen(true);
  }

  function submitDisable(e: React.FormEvent) {
    e.preventDefault();
    if (disable.isPending) return;
    if (!disablePassword.trim()) {
      setDisableError("Введіть пароль");
      return;
    }
    if (!disableCode.trim()) {
      setDisableError("Введіть код із застосунку або код відновлення");
      return;
    }
    setDisableError(null);
    disable.mutate(
      { password: disablePassword, code: disableCode.trim() },
      {
        onSuccess: () => {
          setDisableOpen(false);
          toast.success("Двофакторну автентифікацію вимкнено");
        },
        onError: (err) => {
          const map: Record<string, string> = {
            "Invalid password.": "Невірний пароль",
            "Invalid code.": "Невірний код",
          };
          setDisableError(map[err.message] ?? err.message);
        },
      },
    );
  }

  // ---- Step: recovery codes (shown exactly once) ----
  if (step === "recovery") {
    return (
      <div>
        <div
          style={{
            display: "flex",
            gap: 10,
            alignItems: "flex-start",
            background: "#2d1a0a",
            border: "1px solid #92400E",
            borderRadius: 8,
            padding: "12px 14px",
            marginBottom: 16,
          }}
        >
          <AlertTriangle size={16} color="#FBBF24" style={{ flexShrink: 0, marginTop: 1 }} />
          <p style={{ color: "#FBBF24", fontSize: 12, margin: 0, lineHeight: 1.5 }}>
            <strong>Збережіть ці коди — вони показуються лише один раз.</strong>
            <br />
            Кожен код відновлення можна використати один раз для входу, якщо ви втратите
            доступ до застосунку-автентифікатора.
          </p>
        </div>

        <div
          style={{
            display: "grid",
            gridTemplateColumns: "repeat(2, 1fr)",
            gap: 8,
            background: "#0D1117",
            border: "1px solid #374151",
            borderRadius: 8,
            padding: 16,
            marginBottom: 16,
          }}
        >
          {recoveryCodes.map((code) => (
            <span
              key={code}
              style={{
                color: "#E8EDF5",
                fontFamily: "monospace",
                fontSize: 14,
                letterSpacing: "0.05em",
                textAlign: "center",
              }}
            >
              {code}
            </span>
          ))}
        </div>

        <div style={{ display: "flex", alignItems: "center", gap: 12, marginBottom: 16 }}>
          <Btn
            variant="ghost"
            icon={<Copy size={13} />}
            onClick={() => copyText(recoveryCodes.join("\n"), "Коди скопійовано")}
          >
            Скопіювати всі коди
          </Btn>
        </div>

        <label
          style={{
            display: "flex",
            alignItems: "center",
            gap: 8,
            color: "#9CA3AF",
            fontSize: 13,
            marginBottom: 16,
            cursor: "pointer",
          }}
        >
          <input
            type="checkbox"
            checked={savedConfirmed}
            onChange={(e) => setSavedConfirmed(e.target.checked)}
            style={{ accentColor: "#3B82F6", width: 15, height: 15 }}
          />
          Я зберіг коди відновлення в надійному місці
        </label>

        <Btn variant="success" disabled={!savedConfirmed} onClick={finishRecovery}>
          Я зберіг коди
        </Btn>
      </div>
    );
  }

  // ---- Step: setup (QR + confirm code) ----
  if (step === "setup" && setup.data) {
    return (
      <form onSubmit={submitEnable}>
        <p style={{ ...hintStyle, marginBottom: 16 }}>
          1. Відскануйте QR-код застосунком-автентифікатором (Google Authenticator, Authy,
          1Password тощо).
          <br />
          2. Введіть 6-значний код із застосунку, щоб підтвердити налаштування.
        </p>

        <div style={{ display: "flex", gap: 20, flexWrap: "wrap", marginBottom: 16 }}>
          <div
            style={{
              background: "#FFFFFF",
              borderRadius: 8,
              padding: 12,
              display: "inline-flex",
              flexShrink: 0,
            }}
          >
            <QRCodeSVG value={setup.data.otpauthUri} size={168} bgColor="#FFFFFF" fgColor="#000000" />
          </div>

          <div style={{ flex: 1, minWidth: 220 }}>
            <label style={labelStyle}>Не можете відсканувати? Введіть секрет вручну:</label>
            <div style={{ display: "flex", alignItems: "center", gap: 8, marginBottom: 16 }}>
              <code
                style={{
                  background: "#0D1117",
                  border: "1px solid #374151",
                  borderRadius: 8,
                  padding: "8px 10px",
                  color: "#E8EDF5",
                  fontSize: 12,
                  fontFamily: "monospace",
                  wordBreak: "break-all",
                  flex: 1,
                }}
              >
                {setup.data.secret}
              </code>
              <Btn
                variant="ghost"
                size="sm"
                icon={<Copy size={12} />}
                onClick={() => copyText(setup.data!.secret, "Секрет скопійовано")}
              >
                Копіювати
              </Btn>
            </div>

            <label style={labelStyle}>Код підтвердження</label>
            <input
              value={enableCode}
              onChange={(e) => {
                setEnableCode(e.target.value.replace(/\D/g, "").slice(0, 6));
                setEnableError(null);
              }}
              type="text"
              inputMode="numeric"
              autoComplete="one-time-code"
              autoFocus
              placeholder="123456"
              style={{
                ...inputStyle,
                maxWidth: 160,
                letterSpacing: "0.25em",
                textAlign: "center",
                borderColor: enableError ? "#EF4444" : "#374151",
              }}
            />
            {enableError && <p style={errorStyle}>{enableError}</p>}
          </div>
        </div>

        <div style={{ display: "flex", gap: 10 }}>
          <Btn type="submit" disabled={enable.isPending}>
            {enable.isPending ? "Перевірка…" : "Підтвердити та увімкнути"}
          </Btn>
          <Btn variant="ghost" onClick={cancelSetup}>
            Скасувати
          </Btn>
        </div>
      </form>
    );
  }

  // ---- Step: idle (status + action) ----
  return (
    <div>
      <div style={{ display: "flex", alignItems: "center", gap: 10, marginBottom: 14 }}>
        {enabled ? (
          <>
            <ShieldCheck size={16} color="#4ADE80" />
            <span style={{ color: "#4ADE80", fontSize: 13, fontWeight: 600 }}>2FA увімкнено</span>
          </>
        ) : (
          <>
            <ShieldOff size={16} color="#6B7280" />
            <span style={{ color: "#9CA3AF", fontSize: 13, fontWeight: 600 }}>2FA вимкнено</span>
          </>
        )}
      </div>

      <p style={{ ...hintStyle, marginBottom: 16 }}>
        Двофакторна автентифікація захищає акаунт: після пароля потрібно ввести код із
        застосунку-автентифікатора.
      </p>

      {enabled ? (
        <Btn variant="danger" onClick={openDisable}>
          Вимкнути 2FA
        </Btn>
      ) : (
        <Btn onClick={startSetup} disabled={setup.isPending}>
          {setup.isPending ? "Підготовка…" : "Увімкнути 2FA"}
        </Btn>
      )}

      {disableOpen && (
        <Modal title="Вимкнути двофакторну автентифікацію" onClose={() => setDisableOpen(false)} width={440}>
          <form onSubmit={submitDisable}>
            <p style={{ ...hintStyle, marginBottom: 16 }}>
              Для підтвердження введіть пароль і код із застосунку-автентифікатора (або код
              відновлення).
            </p>

            <div style={{ display: "flex", flexDirection: "column", gap: 14, marginBottom: 18 }}>
              <div>
                <label style={labelStyle}>Пароль</label>
                <input
                  type="password"
                  value={disablePassword}
                  onChange={(e) => { setDisablePassword(e.target.value); setDisableError(null); }}
                  autoComplete="current-password"
                  style={inputStyle}
                />
              </div>
              <div>
                <label style={labelStyle}>Код 2FA або код відновлення</label>
                <input
                  type="text"
                  value={disableCode}
                  onChange={(e) => { setDisableCode(e.target.value); setDisableError(null); }}
                  autoComplete="one-time-code"
                  placeholder="123456 або XXXX-XXXX"
                  style={inputStyle}
                />
              </div>
            </div>

            {disableError && <p style={{ ...errorStyle, margin: "0 0 14px" }}>{disableError}</p>}

            <div style={{ display: "flex", gap: 10, justifyContent: "flex-end" }}>
              <Btn variant="ghost" onClick={() => setDisableOpen(false)}>
                Скасувати
              </Btn>
              <Btn type="submit" variant="danger" disabled={disable.isPending}>
                {disable.isPending ? "Вимкнення…" : "Вимкнути 2FA"}
              </Btn>
            </div>
          </form>
        </Modal>
      )}
    </div>
  );
}
