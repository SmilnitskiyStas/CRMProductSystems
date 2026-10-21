"use client";

import { useSearchParams } from "next/navigation";
import { useTranslations } from "next-intl";

// Shown on /login when the API client redirects after a failed token refresh
// (lib/api.ts → /login?reason=session_expired) or after the idle-timeout logout
// (useIdleLogout → /login?reason=idle_timeout). Amber warning tone — both are
// expected events, not errors.
export function SessionExpiredNotice() {
  const t = useTranslations("Dashboard.auth");
  const searchParams = useSearchParams();
  const reason = searchParams.get("reason");

  if (reason !== "session_expired" && reason !== "idle_timeout") return null;

  return (
    <div
      role="status"
      style={{
        background: "#F59E0B1A",
        border: "1px solid #F59E0B40",
        borderRadius: 4,
        padding: "10px 14px",
        marginBottom: 20,
        color: "#F59E0B",
        fontSize: 13,
        fontFamily: '"Inter", sans-serif',
      }}
    >
      {t(reason === "idle_timeout" ? "idleTimeout" : "sessionExpired")}
    </div>
  );
}
