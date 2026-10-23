"use client";

import { useState } from "react";
import { useMutation } from "@tanstack/react-query";
import { QRCodeSVG } from "qrcode.react";
import { useTranslations } from "next-intl";
import { authApi } from "../api/auth";
import { useCompleteLogin } from "../hooks/useAuth";

export function MfaEnrollmentForm({ token, onCancel, onComplete }: { token?: string; onCancel: () => void; onComplete?: () => void }) {
  const t = useTranslations("Dashboard.auth.matrixMfa");
  const [code, setCode] = useState("");
  const setup = useMutation({ mutationFn: () => token ? authApi.setupEnrollment(token) : authApi.setupTwoFactor() });
  const enable = useMutation({ mutationFn: () => token ? authApi.enableEnrollment(token, code) : authApi.enableTwoFactor(code) });
  const complete = useCompleteLogin();
  return <div style={{ display: "grid", gap: 16, color: "#E8EDF5" }}>
    <p>{t("enrollment")}</p>
    {enable.data ? <>
      <p>{t("saveCodes")}</p>
      <pre style={{ whiteSpace: "pre-wrap", userSelect: "all" }}>{enable.data.recoveryCodes.join("\n")}</pre>
      <button onClick={() => onComplete ? onComplete() : complete(enable.data!)}>{t("saved")}</button>
    </> : <>
      {!setup.data ? <button disabled={setup.isPending} onClick={() => setup.mutate()}>{t("setup")}</button> : <>
        <QRCodeSVG value={setup.data.otpauthUri} size={180} includeMargin />
        <p>{t("manualKey")}</p>
        <code style={{ overflowWrap: "anywhere", userSelect: "all" }}>{setup.data.secret}</code>
        <form onSubmit={e => { e.preventDefault(); if (/^\d{6}$/.test(code)) enable.mutate(); }} style={{ display: "grid", gap: 12 }}>
          <label htmlFor="enrollment-code">{t("code")}</label>
          <input id="enrollment-code" value={code} onChange={e => setCode(e.target.value.replace(/\D/g, "").slice(0, 6))} autoComplete="one-time-code" inputMode="numeric" />
          <button disabled={enable.isPending || code.length !== 6}>{t("confirm")}</button>
        </form>
      </>}
      {(setup.error || enable.error) && <p role="alert">{(setup.error || enable.error)?.message}</p>}
      <button disabled={enable.isPending} onClick={onCancel}>{t("back")}</button>
    </>}
  </div>;
}
