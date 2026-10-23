"use client";

import { useEffect, useRef, useState } from "react";
import { useTranslations } from "next-intl";
import { registerMfaPrompt } from "../mfaPrompt";
import { useMe } from "../hooks/useAuth";
import { MfaEnrollmentForm } from "./MfaEnrollmentForm";

export function MfaStepUpDialog() {
  const t = useTranslations("Dashboard.auth.matrixMfa");
  const { data: me, refetch } = useMe();
  const resolve = useRef<((code: string | null) => void) | null>(null);
  const [open, setOpen] = useState(false);
  const [code, setCode] = useState("");
  useEffect(() => {
    const unregister = registerMfaPrompt(() => new Promise<string | null>(done => {
      resolve.current?.(null);
      resolve.current = done;
      setCode(""); setOpen(true);
    }));
    return () => { unregister(); resolve.current?.(null); resolve.current = null; };
  }, []);
  function finish(value: string | null) { resolve.current?.(value); resolve.current = null; setOpen(false); setCode(""); }
  if (!open) return null;
  if (me?.twoFactorEnabled === false) return <div style={{ position: "fixed", inset: 0, zIndex: 10000, background: "#0009", display: "grid", placeItems: "center", overflow: "auto" }}>
    <div role="dialog" aria-modal="true" aria-label={t("setup")} style={{ background: "#111827", padding: 24, maxWidth: 420 }}>
      <p style={{ color: "#E8EDF5" }}>{t("retryAfterSetup")}</p>
      <MfaEnrollmentForm onCancel={() => finish(null)} onComplete={() => { void refetch(); finish(null); }} />
    </div>
  </div>;
  return <div style={{ position: "fixed", inset: 0, zIndex: 10000, background: "#0009", display: "grid", placeItems: "center" }}>
    <form role="dialog" aria-modal="true" aria-labelledby="mfa-step-title" onSubmit={e => { e.preventDefault(); if (code.trim()) finish(code.trim()); }} style={{ background: "#111827", color: "#E8EDF5", padding: 24, borderRadius: 12, display: "grid", gap: 16, maxWidth: 420 }}>
      <h2 id="mfa-step-title">{t("stepUp")}</h2>
      <label htmlFor="mfa-step-code">{t("codeOrRecovery")}</label>
      <input id="mfa-step-code" autoFocus autoComplete="one-time-code" value={code} onChange={e => setCode(e.target.value.slice(0, 32))} />
      <button type="submit" disabled={!code.trim()}>{t("confirm")}</button>
      <button type="button" onClick={() => finish(null)}>{t("back")}</button>
    </form>
  </div>;
}
