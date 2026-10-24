"use client";

import Link from "next/link";
import { ShieldCheck } from "lucide-react";
import { useTranslations } from "next-intl";
import { useMe } from "../hooks/useAuth";

/** Non-blocking reminder; setup starts only from the account's security settings. */
export function MfaSetupReminder() {
  const t = useTranslations("Dashboard.auth.mfaReminder");
  const { data: user } = useMe();
  if (!user || user.twoFactorEnabled !== false || user.role === "consumer") return null;

  return (
    <div role="status" style={{ background: "#2D7DD214", borderBottom: "1px solid #2D7DD240", padding: "10px 24px", display: "flex", alignItems: "center", flexWrap: "wrap", gap: 12, color: "#B9D9F7", fontSize: 13 }}>
      <ShieldCheck size={18} aria-hidden="true" style={{ flexShrink: 0 }} />
      <span style={{ flex: "1 1 260px" }}>{t("message")}</span>
      <Link href="/settings-user#security" style={{ color: "#79B8F3", textDecoration: "underline", textUnderlineOffset: 3, fontWeight: 600 }}>{t("action")}</Link>
    </div>
  );
}
