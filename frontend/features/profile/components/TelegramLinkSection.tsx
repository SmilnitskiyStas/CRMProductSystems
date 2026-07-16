"use client";

import { useEffect, useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { Copy, ExternalLink, RefreshCw } from "lucide-react";
import { useMe, ME_KEY } from "@/features/auth/hooks/useAuth";
import { useCreateTelegramLinkCode } from "../hooks/useProfile";
import { Btn } from "@/components/ui/Btn";
import type { TelegramLinkCodeResponse } from "../types";

/** Re-checks /api/auth/me while a code is pending, so linking is detected without a reload. */
const POLL_INTERVAL_MS = 3000;

function copyText(text: string, message: string) {
  navigator.clipboard
    .writeText(text)
    .then(() => toast.success(message))
    .catch(() => toast.error("Не вдалося скопіювати"));
}

export function TelegramLinkSection() {
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
      toast.success("Telegram підключено");
    }
  }, [isLinked, pending]);

  function handleGenerate() {
    setExpired(false);
    createCode.mutate(undefined, {
      onSuccess: (data) => setPending(data),
      onError: () => toast.error("Не вдалося згенерувати код. Спробуйте пізніше."),
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
            Telegram акаунт
          </div>
          <div style={{ color: isLinked ? "#4ADE80" : "#6B7280", fontSize: 12, marginTop: 2 }}>
            {isLinked ? "✓ Підключено" : "Не підключено"}
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
          Telegram успішно прив&apos;язано. Ви отримуватимете сповіщення в боті.
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
              Код прив&apos;язки
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
                onClick={() => copyText(pending.code, "Код скопійовано")}
              >
                Копіювати
              </Btn>
            </div>

            <ol style={{ margin: "0 0 14px", paddingLeft: 18, fontSize: 12, color: "#9CA3AF", lineHeight: 1.7 }}>
              <li>Натисніть «Відкрити в Telegram» — бот отримає код автоматично</li>
              <li>Якщо посилання не відкрилось: знайдіть бота вручну і надішліть <code style={{ color: "#38BDF8" }}>/start {pending.code}</code></li>
              <li>Натисніть Start у боті — акаунт прив&apos;яжеться сам, без дій тут</li>
            </ol>

            <div style={{ display: "flex", gap: 10, flexWrap: "wrap" }}>
              <a href={pending.deepLink} target="_blank" rel="noopener noreferrer">
                <Btn type="button" icon={<ExternalLink size={13} />}>
                  Відкрити в Telegram
                </Btn>
              </a>
              <Btn variant="ghost" icon={<RefreshCw size={13} />} onClick={handleCheckNow}>
                Перевірити зараз
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
            Очікуємо підтвердження в Telegram… Код дійсний до{" "}
            {new Date(pending.expiresAt).toLocaleTimeString("uk-UA", { hour: "2-digit", minute: "2-digit" })}
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
              Як підключити Telegram:
            </div>
            <ol style={{ margin: 0, paddingLeft: 18 }}>
              <li>Натисніть «Згенерувати код» нижче</li>
              <li>Перейдіть за посиланням у Telegram та натисніть Start</li>
              <li>Готово — акаунт прив&apos;яжеться автоматично, підтвердження не потрібне</li>
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
              Код прострочено (діяв 15 хвилин). Згенеруйте новий.
            </div>
          )}

          <Btn onClick={handleGenerate} disabled={createCode.isPending}>
            {createCode.isPending ? "Генерація…" : "Згенерувати код"}
          </Btn>
        </>
      )}
    </div>
  );
}
