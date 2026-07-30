"use client";

import { Suspense } from "react";
import Link from "next/link";
import { useTranslations } from "next-intl";
import { AuthLogo } from "./AuthLogo";
import { ResetPasswordForm } from "./ResetPasswordForm";

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

/**
 * Visual chrome for `/reset-password` — mirrors LoginCard.tsx's card/footer shell.
 * `ResetPasswordForm` reads `?token=` via `useSearchParams()`, which requires a Suspense
 * boundary in a server-rendered route (same reason LoginCard.tsx wraps
 * SessionExpiredNotice — TASK-376).
 */
export function ResetPasswordCard() {
  const t = useTranslations("Dashboard.auth");

  return (
    <div style={{ width: "100%", maxWidth: 400, padding: "0 16px" }}>
      {/* Card */}
      <div
        style={{
          background: "#161B27",
          border: "1px solid #2A3347",
          borderRadius: 6,
          padding: "36px 32px",
        }}
      >
        {/* Logo */}
        <div style={{ marginBottom: 28, textAlign: "center" }}>
          <AuthLogo />
          <p
            style={{
              marginTop: 6,
              fontSize: 12,
              color: "#8A94A8",
              fontFamily: '"Inter", sans-serif',
            }}
          >
            {t("resetPasswordTitle")}
          </p>
        </div>

        {/* Divider */}
        <div style={{ borderTop: "1px solid #2A3347", marginBottom: 24 }} />

        <Suspense fallback={null}>
          <ResetPasswordForm />
        </Suspense>

        <div style={{ marginTop: 20, textAlign: "center" }}>
          <Link href="/login" style={linkBtnStyle}>
            {t("backToSignIn")}
          </Link>
        </div>
      </div>

      {/* Footer */}
      <p
        style={{
          marginTop: 20,
          textAlign: "center",
          fontSize: 11,
          color: "#8A94A840",
          fontFamily: '"Inter", sans-serif',
        }}
      >
        ShelfGuard v1.0 · B2B SaaS
      </p>
    </div>
  );
}
