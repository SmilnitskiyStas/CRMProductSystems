"use client";

import Link from "next/link";
import { useEffect, useRef, useState, type FormEvent } from "react";
import { useTranslations } from "next-intl";
import { authApi } from "../api/auth";
import { readResetGrant, type ResetGrant } from "../resetGrant";

const inputStyle: React.CSSProperties = { width: "100%", boxSizing: "border-box", padding: "10px 14px",
  background: "#0F1117", border: "1px solid #2A3347", borderRadius: 4, color: "#E8EDF5", fontSize: 14 };

export function ResetPasswordForm() {
  const t = useTranslations("Dashboard.auth");
  const read = useRef(false);
  const [loaded, setLoaded] = useState(false);
  const [grant, setGrant] = useState<ResetGrant | null>(null);
  const [password, setPassword] = useState("");
  const [repeat, setRepeat] = useState("");
  const [pending, setPending] = useState(false);
  const [success, setSuccess] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (read.current) return;
    read.current = true;
    setGrant(readResetGrant(window.location.hash));
    // Keep recovery proof in component memory only, not browser history or persistent storage.
    window.history.replaceState(window.history.state, "", window.location.pathname);
    setLoaded(true);
  }, []);

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!grant || pending) return;
    if (password !== repeat) { setError(t("resetPasswordMismatch")); return; }
    if (password.length < 12 || !/[\p{L}]/u.test(password) || !/\d/.test(password)) {
      setError(t("resetPasswordWeak")); return;
    }
    setPending(true);
    setError(null);
    try {
      await authApi.resetPassword({ ...grant, newPassword: password });
      setSuccess(true);
      setGrant(null);
      setPassword("");
      setRepeat("");
    } catch {
      setError(t("resetPasswordFailed"));
    } finally { setPending(false); }
  }

  if (!loaded) return <p role="status" style={{ color: "#8A94A8" }}>{t("resetPasswordLoading")}</p>;
  if (success) return <div role="status" style={{ color: "#22C55E", lineHeight: 1.5 }}>{t("resetPasswordSuccess")}</div>;
  if (!grant) return <div style={{ color: "#F87171", lineHeight: 1.5 }}>
    <p role="alert">{t("resetPasswordInvalid")}</p>
    <Link href="/forgot-password" style={{ color: "#2D7DD2" }}>{t("resetPasswordRequestNew")}</Link>
  </div>;

  return <form onSubmit={submit} style={{ display: "flex", flexDirection: "column", gap: 16 }}>
    <p style={{ margin: 0, color: "#8A94A8", fontSize: 13, lineHeight: 1.5 }}>{t("resetPasswordDescription")}</p>
    <label style={{ color: "#8A94A8", fontSize: 12 }}>
      {t("resetPasswordNewLabel")}
      <input style={{ ...inputStyle, marginTop: 6 }} type="password" autoComplete="new-password"
        required minLength={12} maxLength={256} value={password} onChange={(event) => setPassword(event.target.value)} disabled={pending} />
    </label>
    <label style={{ color: "#8A94A8", fontSize: 12 }}>
      {t("resetPasswordRepeatLabel")}
      <input style={{ ...inputStyle, marginTop: 6 }} type="password" autoComplete="new-password"
        required minLength={12} maxLength={256} value={repeat} onChange={(event) => setRepeat(event.target.value)} disabled={pending} />
    </label>
    {error && <p role="alert" style={{ margin: 0, color: "#F87171", fontSize: 13 }}>{error}</p>}
    <button type="submit" disabled={pending} style={{ background: "#2D7DD2", border: 0, borderRadius: 4,
      color: "white", padding: "11px 0", fontSize: 14, cursor: pending ? "wait" : "pointer" }}>
      {pending ? t("resetPasswordSaving") : t("resetPasswordSubmit")}
    </button>
  </form>;
}
