"use client";

import { useState } from "react";
import { useMe } from "@/features/auth/hooks/useAuth";
import { useLinkTelegram } from "../hooks/useProfile";
import { Btn } from "@/components/ui/Btn";

export function TelegramLinkSection() {
  const { data: me } = useMe();
  const link = useLinkTelegram();

  const [chatId,  setChatId]  = useState("");
  const [success, setSuccess] = useState(false);
  const [error,   setError]   = useState("");

  const isLinked = Boolean(me?.telegramChatId);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    const id = chatId.trim();
    if (!id) { setError("Введіть Telegram Chat ID"); return; }
    setError("");

    try {
      await link.mutateAsync(id);
      setSuccess(true);
      setChatId("");
    } catch (err) {
      setError((err as Error)?.message ?? "Помилка підключення");
    }
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
          Telegram успішно прив'язано. Ви отримуватимете сповіщення в боті.
        </div>
      ) : (
        <>
          {/* Instructions */}
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
              <li>Відкрийте Telegram та знайдіть бот <code style={{ color: "#38BDF8" }}>@userinfobot</code></li>
              <li>Надішліть йому будь-яке повідомлення</li>
              <li>Скопіюйте ваш <strong>Chat ID</strong> з відповіді</li>
              <li>Вставте його у поле нижче</li>
            </ol>
          </div>

          {success && (
            <div
              style={{
                padding: "10px 14px",
                background: "#052e16",
                border: "1px solid #166534",
                borderRadius: 8,
                color: "#4ADE80",
                fontSize: 13,
                marginBottom: 14,
              }}
            >
              ✓ Telegram успішно підключено!
            </div>
          )}

          {/* Form */}
          <form onSubmit={handleSubmit}>
            <div style={{ marginBottom: 12 }}>
              <label
                style={{
                  display: "block",
                  color: "#9CA3AF", fontSize: 12, fontWeight: 500,
                  marginBottom: 6,
                }}
              >
                Telegram Chat ID
              </label>
              <input
                value={chatId}
                onChange={(e) => { setChatId(e.target.value); setError(""); }}
                placeholder="123456789"
                type="text"
                inputMode="numeric"
                style={{
                  width: "100%",
                  background: "#0D1117",
                  border: `1px solid ${error ? "#EF4444" : "#374151"}`,
                  borderRadius: 8,
                  padding: "9px 12px",
                  color: "#E8EDF5",
                  fontSize: 13,
                  outline: "none",
                  boxSizing: "border-box",
                }}
              />
              {error && (
                <p style={{ color: "#EF4444", fontSize: 11, marginTop: 4 }}>{error}</p>
              )}
            </div>

            <Btn type="submit" disabled={link.isPending}>
              {link.isPending ? "Підключення…" : "Підключити"}
            </Btn>
          </form>
        </>
      )}
    </div>
  );
}
