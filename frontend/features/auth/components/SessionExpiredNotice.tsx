"use client";

import { useSearchParams } from "next/navigation";

// Shown on /login when the API client redirects after a failed token refresh
// (lib/api.ts → /login?reason=session_expired). Amber warning tone — this is
// an expected event, not an error.
export function SessionExpiredNotice() {
  const searchParams = useSearchParams();

  if (searchParams.get("reason") !== "session_expired") return null;

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
      Час сеансу сплив. Будь ласка, увійдіть знову.
    </div>
  );
}
