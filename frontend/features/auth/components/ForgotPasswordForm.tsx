"use client";

import { useMemo } from "react";
import { zodResolver } from "@hookform/resolvers/zod";
import { useForm } from "react-hook-form";
import { z } from "zod";
import { useTranslations } from "next-intl";
import { useForgotPassword } from "../hooks/useAuth";
import { ApiError } from "@/lib/api";

// Same field-shape rationale as LoginForm.tsx: zod's message text is the only
// locale-dependent part, so the inferred value type is hand-written here.
interface FormValues {
  email: string;
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

const successBoxStyle: React.CSSProperties = {
  background: "#22C55E1A",
  border: "1px solid #22C55E40",
  borderRadius: 4,
  padding: "10px 14px",
  color: "#22C55E",
  fontSize: 13,
  fontFamily: '"Inter", sans-serif',
  lineHeight: 1.5,
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

/**
 * Maps a failed forgot-password request to a translated message. The backend always
 * returns 204 regardless of whether the email exists/is active (no-enumeration posture,
 * TASK-456) — so the ONLY way this mutation ever rejects is a genuine transport failure
 * (rate limit or network/server error). There is no "email not found" error state to
 * render — don't invent one.
 */
function forgotPasswordErrorMessage(error: Error, t: ReturnType<typeof useTranslations>): string {
  if (error instanceof ApiError && error.status === 429) return t("tooManyAttempts");
  return t("somethingWentWrongError");
}

export function ForgotPasswordForm() {
  const t = useTranslations("Dashboard.auth");
  const forgotPassword = useForgotPassword();

  const schema = useMemo(
    () => z.object({ email: z.string().email(t("emailInvalid")) }),
    [t],
  );

  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<FormValues>({ resolver: zodResolver(schema) });

  const onSubmit = (values: FormValues) => {
    forgotPassword.mutate({ email: values.email });
  };

  // Always the same success state, regardless of what the server actually did with the
  // email — matches the 204-always contract byte-for-byte (TASK-456).
  if (forgotPassword.isSuccess) {
    return (
      <div role="status" style={successBoxStyle}>
        {t("forgotPasswordSuccessMessage")}
      </div>
    );
  }

  return (
    <form onSubmit={handleSubmit(onSubmit)} noValidate style={{ display: "flex", flexDirection: "column", gap: 20 }}>
      <p style={{ margin: 0, fontSize: 13, color: "#8A94A8", fontFamily: '"Inter", sans-serif', lineHeight: 1.5 }}>
        {t("forgotPasswordDescription")}
      </p>

      {/* Email */}
      <div style={{ display: "flex", flexDirection: "column", gap: 6 }}>
        <label style={labelStyle}>{t("emailFieldLabel")}</label>
        <input
          {...register("email")}
          type="email"
          autoComplete="email"
          placeholder="you@company.com"
          autoFocus
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

      {/* API error */}
      {forgotPassword.error && (
        <div style={errorBoxStyle}>{forgotPasswordErrorMessage(forgotPassword.error, t)}</div>
      )}

      {/* Submit */}
      <button
        type="submit"
        disabled={forgotPassword.isPending}
        style={submitStyle(forgotPassword.isPending)}
        onMouseEnter={(e) => { if (!forgotPassword.isPending) e.currentTarget.style.background = "#3A8FE8"; }}
        onMouseLeave={(e) => { if (!forgotPassword.isPending) e.currentTarget.style.background = "#2D7DD2"; }}
      >
        {forgotPassword.isPending ? t("forgotPasswordSending") : t("forgotPasswordSubmitButton")}
      </button>
    </form>
  );
}
