"use client";

import { useMemo } from "react";
import { useSearchParams } from "next/navigation";
import { zodResolver } from "@hookform/resolvers/zod";
import { useForm } from "react-hook-form";
import { z } from "zod";
import { useTranslations } from "next-intl";
import { useResetPassword } from "../hooks/useAuth";
import { ApiError } from "@/lib/api";

interface FormValues {
  newPassword: string;
  confirmPassword: string;
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
  lineHeight: 1.5,
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

const hintStyle: React.CSSProperties = {
  fontSize: 11,
  color: "#4B5563",
  fontFamily: '"Inter", sans-serif',
};

const fieldErrorStyle: React.CSSProperties = {
  fontSize: 11,
  color: "#EF4444",
  fontFamily: '"Inter", sans-serif',
};

// Backend's exact sentinel (TASK-456's AuthService.ResetPasswordAsync) for every
// not-found/expired/used/owner-inactive case — these are deliberately NOT distinguished
// from one another. Matched verbatim so the UI can show a friendlier localized string
// instead of the raw English backend text for this one well-known, expected case.
const INVALID_OR_EXPIRED_SENTINEL = "Invalid or expired reset link.";

/**
 * Maps a failed reset-password request to a display message.
 * - Rate limit → translated (shares "auth-login" 10/min, same family as login/2FA-verify).
 * - The generic invalid/expired-token sentinel → translated, friendlier text.
 * - Anything else (a PasswordValidator policy message) → shown as-is, same convention as
 *   ChangePasswordForm.tsx's `change.error.message` (the backend text is already meant to
 *   be user-facing; there are ~100+ possible variants, not worth enumerating/localizing).
 * - A non-ApiError (pure network/transport failure) → generic fallback.
 */
function resetPasswordErrorMessage(error: Error, t: ReturnType<typeof useTranslations>): string {
  if (error instanceof ApiError) {
    if (error.status === 429) return t("tooManyAttempts");
    if (error.message === INVALID_OR_EXPIRED_SENTINEL) return t("resetPasswordInvalidOrExpired");
    return error.message;
  }
  return t("somethingWentWrongError");
}

export function ResetPasswordForm() {
  const t = useTranslations("Dashboard.auth");
  const searchParams = useSearchParams();
  const token = searchParams.get("token");
  const resetPassword = useResetPassword();

  const schema = useMemo(
    () =>
      z
        .object({
          newPassword: z
            .string()
            .min(12, t("newPasswordMinError"))
            .refine((v) => /[a-zA-Zа-яА-ЯіІїЇєЄґҐ]/.test(v) && /\d/.test(v), {
              message: t("newPasswordComplexityError"),
            }),
          confirmPassword: z.string(),
        })
        .refine((v) => v.newPassword === v.confirmPassword, {
          message: t("confirmMismatchError"),
          path: ["confirmPassword"],
        }),
    [t],
  );

  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<FormValues>({ resolver: zodResolver(schema) });

  // No token at all in the URL — never render a form that could submit an empty token.
  if (!token) {
    return <div style={errorBoxStyle}>{t("resetPasswordMissingToken")}</div>;
  }

  const onSubmit = (values: FormValues) => {
    resetPassword.mutate({ token, newPassword: values.newPassword });
  };

  // ResetPasswordCard already renders a persistent "back to sign in" link below this
  // component in every state — no need to repeat it here.
  if (resetPassword.isSuccess) {
    return (
      <div role="status" style={successBoxStyle}>
        {t("resetPasswordSuccessMessage")}
      </div>
    );
  }

  return (
    <form onSubmit={handleSubmit(onSubmit)} noValidate style={{ display: "flex", flexDirection: "column", gap: 20 }}>
      {/* New password */}
      <div style={{ display: "flex", flexDirection: "column", gap: 6 }}>
        <label style={labelStyle}>{t("newPasswordLabel")}</label>
        <input
          {...register("newPassword")}
          type="password"
          autoComplete="new-password"
          placeholder="••••••••"
          autoFocus
          style={inputStyle(Boolean(errors.newPassword))}
          onFocus={(e) => { e.currentTarget.style.borderColor = errors.newPassword ? "#EF4444" : "#2D7DD2"; }}
          onBlur={(e)  => { e.currentTarget.style.borderColor = errors.newPassword ? "#EF4444" : "#2A3347"; }}
        />
        <span style={errors.newPassword ? fieldErrorStyle : hintStyle}>
          {errors.newPassword ? errors.newPassword.message : t("passwordPolicyHint")}
        </span>
      </div>

      {/* Confirm password */}
      <div style={{ display: "flex", flexDirection: "column", gap: 6 }}>
        <label style={labelStyle}>{t("confirmPasswordLabel")}</label>
        <input
          {...register("confirmPassword")}
          type="password"
          autoComplete="new-password"
          placeholder="••••••••"
          style={inputStyle(Boolean(errors.confirmPassword))}
          onFocus={(e) => { e.currentTarget.style.borderColor = errors.confirmPassword ? "#EF4444" : "#2D7DD2"; }}
          onBlur={(e)  => { e.currentTarget.style.borderColor = errors.confirmPassword ? "#EF4444" : "#2A3347"; }}
        />
        {errors.confirmPassword && (
          <span style={fieldErrorStyle}>{errors.confirmPassword.message}</span>
        )}
      </div>

      {/* API error */}
      {resetPassword.error && (
        <div style={errorBoxStyle}>{resetPasswordErrorMessage(resetPassword.error, t)}</div>
      )}

      {/* Submit */}
      <button
        type="submit"
        disabled={resetPassword.isPending}
        style={submitStyle(resetPassword.isPending)}
        onMouseEnter={(e) => { if (!resetPassword.isPending) e.currentTarget.style.background = "#3A8FE8"; }}
        onMouseLeave={(e) => { if (!resetPassword.isPending) e.currentTarget.style.background = "#2D7DD2"; }}
      >
        {resetPassword.isPending ? t("resetPasswordSubmitting") : t("resetPasswordSubmitButton")}
      </button>
    </form>
  );
}
