"use client";

import { useEffect, useState } from "react";
import { useTranslations, useLocale } from "next-intl";
import { useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { Copy, ExternalLink, RefreshCw } from "lucide-react";
import { useMe, ME_KEY } from "@/features/auth/hooks/useAuth";
import { useCreateTelegramLinkCode } from "../hooks/useProfile";
import { Btn } from "@/components/ui/Btn";
import type { TelegramLinkCodeResponse } from "../types";

/** Re-checks /api/auth/me while a code is pending, so linking is detected without a reload. */
const POLL_INTERVAL_MS = 3000;

function copyText(text: string, message: string, failMessage: string) {
  navigator.clipboard
    .writeText(text)
    .then(() => toast.success(message))
    .catch(() => toast.error(failMessage));
}

export function TelegramLinkSection() {
  const t = useTranslations("Dashboard.profile.telegram");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";
  const { data: me } = useMe();
  const qc = useQueryClient();
  const createCode = useCreateTelegramLinkCode();

  const [pending, setPending] = useState<TelegramLinkCodeResponse | null>(null);
  const [expired, setExpired] = useState(false);

  const isLinked = Boolean(me?.telegramChatId);

  // Poll /api/auth/me while a code is outstanding — the worker's /start <code> listener
  // writes TelegramChatId server-side, there is nothing for the frontend to submit.
  useEffect(() => {
    if (!pending || isLinked) return;

    const interval = setInterval(() => {
      if (Date.now() > new Date(pending.expiresAt).getTime()) {
        setExpired(true);
        return;
      }
      qc.invalidateQueries({ queryKey: ME_KEY });
    }, POLL_INTERVAL_MS);

    return () => clearInterval(interval);
  }, [pending, isLinked, qc]);

  // Linked while a code was pending — clear local state, nothing left to show but the status.
  useEffect(() => {
    if (isLinked && pending) {
      setPending(null);
      setExpired(false);
      toast.success(t("connectedToast"));
    }
  }, [isLinked, pending, t]);

  function handleGenerate() {
    setExpired(false);
    createCode.mutate(undefined, {
      onSuccess: (data) => setPending(data),
      onError: () => toast.error(t("generateErrorToast")),
    });
  }

  function handleCheckNow() {
    qc.invalidateQueries({ queryKey: ME_KEY });
  }

  return (
    <div>
      {/* Status */}
      <div style={{ display: "flex", alignItems: "center", gap: 12, marginBottom: 20 }}>
        <div
          style={{
            width: 40, height: 40, borderRadius: "50%",
            background: isLinked ? "#052e16" : "#111827",
            border: `1px solid ${isLinked ? "#166534" : "#1F2937"}`,
            display: "flex", alignItems: "center", justifyContent: "center",
            fontSize: 18,
          }}
        >
          ✈️
        </div>
        <div>
          <div style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 500 }}>
            {t("titleLabel")}
          </div>
          <div style={{ color: isLinked ? "#4ADE80" : "#6B7280", fontSize: 12, marginTop: 2 }}>
            {isLinked ? t("connectedStatus") : t("notConnectedStatus")}
          </div>
        </div>
      </div>

      {isLinked ? (
        <div
          style={{
            padding: "14px 16px",
            background: "#052e16",
            border: "1px solid #166534",
            borderRadius: 9,
            color: "#4ADE80",
            fontSize: 13,
          }}
        >
          {t("linkedBanner")}
        </div>
      ) : pending && !expired ? (
        <>
          {/* Pending code */}
          <div
            style={{
              padding: "14px 16px",
              background: "#0A1020",
              border: "1px solid #1F2937",
              borderRadius: 9,
              marginBottom: 16,
            }}
          >
            <div style={{ color: "#E8EDF5", fontWeight: 600, fontSize: 13, marginBottom: 10 }}>
              {t("codeTitle")}
            </div>
            <div style={{ display: "flex", alignItems: "center", gap: 8, marginBottom: 14 }}>
              <code
                style={{
                  background: "#0D1117",
                  border: "1px solid #374151",
                  borderRadius: 8,
                  padding: "10px 14px",
                  color: "#38BDF8",
                  fontSize: 18,
                  fontFamily: "monospace",
                  letterSpacing: "0.15em",
                  flex: 1,
                  textAlign: "center",
                }}
              >
                {pending.code}
              </code>
              <Btn
                variant="ghost"
                size="sm"
                icon={<Copy size={12} />}
                onClick={() => copyText(pending.code, t("codeCopiedToast"), t("copyFailedToast"))}
              >
                {t("copyButtonLabel")}
              </Btn>
            </div>

            <ol style={{ margin: "0 0 14px", paddingLeft: 18, fontSize: 12, color: "#9CA3AF", lineHeight: 1.7 }}>
              <li>{t("step1")}</li>
              <li>{t("step2Prefix")} <code style={{ color: "#38BDF8" }}>/start {pending.code}</code></li>
              <li>{t("step3")}</li>
            </ol>

            <div style={{ display: "flex", gap: 10, flexWrap: "wrap" }}>
              <a href={pending.deepLink} target="_blank" rel="noopener noreferrer">
                <Btn type="button" icon={<ExternalLink size={13} />}>
                  {t("openInTelegramButton")}
                </Btn>
              </a>
              <Btn variant="ghost" icon={<RefreshCw size={13} />} onClick={handleCheckNow}>
                {t("checkNowButton")}
              </Btn>
            </div>
          </div>

          <div
            style={{
              display: "flex", alignItems: "center", gap: 8,
              color: "#6B7280", fontSize: 12,
            }}
          >
            <span
              style={{
                width: 6, height: 6, borderRadius: "50%",
                background: "#FBBF24",
                animation: "sg-pulse 1.4s ease-in-out infinite",
              }}
            />
            {t("waitingPrefix")}{" "}
            {new Date(pending.expiresAt).toLocaleTimeString(intlLocale, { hour: "2-digit", minute: "2-digit" })}
            <style>{`@keyframes sg-pulse { 0%, 100% { opacity: 0.3; } 50% { opacity: 1; } }`}</style>
          </div>
        </>
      ) : (
        <>
          {/* Idle / expired — instructions + generate button */}
          <div
            style={{
              padding: "12px 14px",
              background: "#0A1020",
              border: "1px solid #1F2937",
              borderRadius: 9,
              marginBottom: 16,
              fontSize: 12,
              color: "#6B7280",
              lineHeight: 1.6,
            }}
          >
            <div style={{ color: "#E8EDF5", fontWeight: 600, marginBottom: 6 }}>
              {t("howToConnectTitle")}
            </div>
            <ol style={{ margin: 0, paddingLeft: 18 }}>
              <li>{t("idleStep1")}</li>
              <li>{t("idleStep2")}</li>
              <li>{t("idleStep3")}</li>
            </ol>
          </div>

          {expired && (
            <div
              style={{
                padding: "10px 14px",
                background: "#2d0a0a",
                border: "1px solid #7F1D1D",
                borderRadius: 8,
                color: "#F87171",
                fontSize: 13,
                marginBottom: 14,
              }}
            >
              {t("expiredMessage")}
            </div>
          )}

          <Btn onClick={handleGenerate} disabled={createCode.isPending}>
            {createCode.isPending ? t("generatingButton") : t("generateButton")}
          </Btn>
        </>
      )}
    </div>
  );
}
