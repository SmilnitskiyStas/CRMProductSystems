"use client";

import { useMemo, useState } from "react";
import { zodResolver } from "@hookform/resolvers/zod";
import { useForm } from "react-hook-form";
import { z } from "zod";
import { useTranslations } from "next-intl";
import Link from "next/link";
import { useLogin, useVerifyTwoFactor } from "../hooks/useAuth";
import { isTwoFactorChallenge } from "../types";
import { ApiError } from "@/lib/api";

// zod's message shape doesn't change across locales — only the strings inside
// .email()/.min() do — so the inferred value type is stable and can be hand-written
// here rather than derived per-render from a locale-dependent schema instance.
interface FormValues {
  email: string;
  password: string;
}

const labelStyle: React.CSSProperties = {
  fontSize: 12,
  fontWeight: 500,
  color: "#8A94A8",
  fontFamily: '"Inter", sans-serif',
  letterSpacing: "0.03em",
};

const inputStyle = (hasError: boolean): React.CSSProperties => ({
  background: "#0F1117",
  border: `1px solid ${hasError ? "#EF4444" : "#2A3347"}`,
  borderRadius: 4,
  padding: "10px 14px",
  color: "#E8EDF5",
  fontSize: 14,
  fontFamily: '"Inter", sans-serif',
  outline: "none",
  transition: "border-color 0.15s",
});

const errorBoxStyle: React.CSSProperties = {
  background: "#EF44441A",
  border: "1px solid #EF444440",
  borderRadius: 4,
  padding: "10px 14px",
  color: "#EF4444",
  fontSize: 13,
  fontFamily: '"Inter", sans-serif',
};

const submitStyle = (pending: boolean): React.CSSProperties => ({
  background: pending ? "#2D7DD280" : "#2D7DD2",
  color: "#fff",
  border: "none",
  borderRadius: 4,
  padding: "11px 0",
  fontSize: 14,
  fontWeight: 600,
  fontFamily: '"Inter", sans-serif',
  cursor: pending ? "not-allowed" : "pointer",
  transition: "background 0.15s",
  letterSpacing: "0.01em",
});

const linkBtnStyle: React.CSSProperties = {
  background: "none",
  border: "none",
  padding: 0,
  color: "#2D7DD2",
  fontSize: 12,
  fontFamily: '"Inter", sans-serif',
  cursor: "pointer",
  textDecoration: "underline",
  textUnderlineOffset: 3,
};

/** Maps API errors from the credentials step to a translated UI message. */
function loginErrorMessage(error: Error, t: ReturnType<typeof useTranslations>): string {
  if (error instanceof ApiError) {
    if (error.status === 429) return t("tooManyAttempts");
    if (error.status === 401) return t("invalidCredentials");
  }
  return error.message;
}

/** Maps API errors from the 2FA verify step to a translated UI message. */
function verifyErrorMessage(error: Error, t: ReturnType<typeof useTranslations>): string {
  if (error instanceof ApiError) {
    if (error.status === 429) return t("tooManyAttempts");
    if (error.status === 401) {
      // Expired/invalid challenge (5 min TTL) vs. a wrong code
      if (error.message.toLowerCase().includes("challenge"))
        return t("challengeExpired");
      return t("invalidCode");
    }
  }
  return error.message;
}

export function LoginForm() {
  const t = useTranslations("Dashboard.auth");
  const tCommon = useTranslations("Common");
  const login = useLogin();
  const verify = useVerifyTwoFactor();

  // 2FA step state: challengeToken !== null → show the code step
  const [challengeToken, setChallengeToken] = useState<string | null>(null);
  const [code, setCode] = useState("");
  const [useRecovery, setUseRecovery] = useState(false);
  const [codeError, setCodeError] = useState<string | null>(null);

  const schema = useMemo(
    () =>
      z.object({
        email: z.string().email(t("emailInvalid")),
        password: z.string().min(1, t("passwordRequired")),
      }),
    [t],
  );

  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<FormValues>({ resolver: zodResolver(schema) });

  const onSubmit = (values: FormValues) => {
    login.mutate(
      { email: values.email, password: values.password },
      {
        onSuccess: (data) => {
          if (isTwoFactorChallenge(data)) {
            verify.reset();
            setCode("");
            setUseRecovery(false);
            setCodeError(null);
            setChallengeToken(data.challengeToken);
          }
        },
      },
    );
  };

  const onVerifySubmit = (e: React.FormEvent) => {
    e.preventDefault();
    if (!challengeToken || verify.isPending) return;

    const trimmed = code.trim();
    if (useRecovery) {
      if (trimmed.replace(/-/g, "").length < 8) {
        setCodeError(t("recoveryCodeHint"));
        return;
      }
    } else if (!/^\d{6}$/.test(trimmed)) {
      setCodeError(t("totpCodeHint"));
      return;
    }
    setCodeError(null);
    verify.mutate({ challengeToken, code: trimmed });
  };

  const backToCredentials = () => {
    setChallengeToken(null);
    setCode("");
    setUseRecovery(false);
    setCodeError(null);
    verify.reset();
    login.reset();
  };

  const switchCodeMode = () => {
    setUseRecovery((v) => !v);
    setCode("");
    setCodeError(null);
    verify.reset();
  };

  // ---- Step 2: 2FA code ----
  if (challengeToken !== null) {
    const hasError = Boolean(codeError);

    return (
      <form onSubmit={onVerifySubmit} noValidate style={{ display: "flex", flexDirection: "column", gap: 20 }}>
        <p style={{ margin: 0, fontSize: 13, color: "#8A94A8", fontFamily: '"Inter", sans-serif', lineHeight: 1.5 }}>
          {useRecovery
            ? t("recoveryStepInfo")
            : t("totpStepInfo")}
        </p>

        <div style={{ display: "flex", flexDirection: "column", gap: 6 }}>
          <label style={labelStyle}>
            {useRecovery ? t("recoveryCodeLabel") : t("totpCodeLabel")}
          </label>
          <input
            key={useRecovery ? "recovery" : "totp"}
            value={code}
            onChange={(e) => {
              const raw = e.target.value;
              setCode(
                useRecovery
                  ? raw.toUpperCase().replace(/[^A-Z0-9-]/g, "").slice(0, 9)
                  : raw.replace(/\D/g, "").slice(0, 6),
              );
              setCodeError(null);
            }}
            type="text"
            inputMode={useRecovery ? "text" : "numeric"}
            autoComplete="one-time-code"
            autoFocus
            placeholder={useRecovery ? "XXXX-XXXX" : "123456"}
            style={{
              ...inputStyle(hasError),
              letterSpacing: "0.25em",
              textAlign: "center",
              fontVariantNumeric: "tabular-nums",
            }}
            onFocus={(e) => { e.currentTarget.style.borderColor = hasError ? "#EF4444" : "#2D7DD2"; }}
            onBlur={(e)  => { e.currentTarget.style.borderColor = hasError ? "#EF4444" : "#2A3347"; }}
          />
          {codeError && (
            <span style={{ fontSize: 11, color: "#EF4444", fontFamily: '"Inter", sans-serif' }}>
              {codeError}
            </span>
          )}
        </div>

        {verify.error && <div style={errorBoxStyle}>{verifyErrorMessage(verify.error, t)}</div>}

        <button
          type="submit"
          disabled={verify.isPending}
          style={submitStyle(verify.isPending)}
          onMouseEnter={(e) => { if (!verify.isPending) e.currentTarget.style.background = "#3A8FE8"; }}
          onMouseLeave={(e) => { if (!verify.isPending) e.currentTarget.style.background = "#2D7DD2"; }}
        >
          {verify.isPending ? t("verifying") : tCommon("confirm")}
        </button>

        <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
          <button type="button" onClick={backToCredentials} style={{ ...linkBtnStyle, color: "#8A94A8" }}>
            {"← "}{tCommon("back")}
          </button>
          <button type="button" onClick={switchCodeMode} style={linkBtnStyle}>
            {useRecovery ? t("useAuthenticatorCode") : t("useRecoveryCode")}
          </button>
        </div>
      </form>
    );
  }

  // ---- Step 1: credentials ----
  return (
    <form onSubmit={handleSubmit(onSubmit)} noValidate style={{ display: "flex", flexDirection: "column", gap: 20 }}>

      {/* Email */}
      <div style={{ display: "flex", flexDirection: "column", gap: 6 }}>
        <label style={labelStyle}>{t("emailFieldLabel")}</label>
        <input
          {...register("email")}
          type="email"
          autoComplete="email"
          placeholder="you@company.com"
          style={inputStyle(Boolean(errors.email))}
          onFocus={(e) => { e.currentTarget.style.borderColor = errors.email ? "#EF4444" : "#2D7DD2"; }}
          onBlur={(e)  => { e.currentTarget.style.borderColor = errors.email ? "#EF4444" : "#2A3347"; }}
        />
        {errors.email && (
          <span style={{ fontSize: 11, color: "#EF4444", fontFamily: '"Inter", sans-serif' }}>
            {errors.email.message}
          </span>
        )}
      </div>

      {/* Password */}
      <div style={{ display: "flex", flexDirection: "column", gap: 6 }}>
        <label style={labelStyle}>{t("passwordFieldLabel")}</label>
        <input
          {...register("password")}
          type="password"
          autoComplete="current-password"
          placeholder="••••••••"
          style={inputStyle(Boolean(errors.password))}
          onFocus={(e) => { e.currentTarget.style.borderColor = errors.password ? "#EF4444" : "#2D7DD2"; }}
          onBlur={(e)  => { e.currentTarget.style.borderColor = errors.password ? "#EF4444" : "#2A3347"; }}
        />
        {errors.password && (
          <span style={{ fontSize: 11, color: "#EF4444", fontFamily: '"Inter", sans-serif' }}>
            {errors.password.message}
          </span>
        )}
      </div>

      {/* Forgot password */}
      <div style={{ display: "flex", justifyContent: "flex-end" }}>
        <Link href="/forgot-password" style={linkBtnStyle}>
          {t("forgotPasswordLink")}
        </Link>
      </div>

      {/* API error */}
      {login.error && <div style={errorBoxStyle}>{loginErrorMessage(login.error, t)}</div>}

      {/* Submit */}
      <button
        type="submit"
        disabled={login.isPending}
        style={submitStyle(login.isPending)}
        onMouseEnter={(e) => { if (!login.isPending) e.currentTarget.style.background = "#3A8FE8"; }}
        onMouseLeave={(e) => { if (!login.isPending) e.currentTarget.style.background = "#2D7DD2"; }}
      >
        {login.isPending ? t("signingIn") : t("signIn")}
      </button>
    </form>
  );
}
