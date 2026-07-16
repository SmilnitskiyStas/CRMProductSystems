"use client";

import { useEffect } from "react";

// Catches errors thrown by the ROOT layout itself (app/layout.tsx) — the one
// case app/error.tsx cannot cover, since that boundary lives inside the root
// layout and can't catch errors from its own ancestor. Must render its own
// <html>/<body> (it fully replaces the root layout while active) and must not
// depend on anything that could itself be broken — no shared providers, no
// Tailwind class assumptions beyond globals.css, plain inline fallback styles.
export default function GlobalError({
  error,
  reset,
}: {
  error: Error & { digest?: string };
  reset: () => void;
}) {
  useEffect(() => {
    // TODO(KI-020): send to Sentry/error-tracking once a DSN exists — see known-issues.md.
    // eslint-disable-next-line no-console
    console.error("Unhandled root-layout error:", error);
  }, [error]);

  return (
    <html lang="uk">
      <body>
        <div
          style={{
            minHeight: "100vh",
            display: "flex",
            flexDirection: "column",
            alignItems: "center",
            justifyContent: "center",
            gap: "1rem",
            padding: "1rem",
            textAlign: "center",
            fontFamily: "system-ui, -apple-system, sans-serif",
          }}
        >
          <h1 style={{ fontSize: "1.25rem", fontWeight: 600 }}>Критична помилка застосунку</h1>
          <p style={{ maxWidth: 420, fontSize: "0.875rem", color: "#6b7280" }}>
            Сталася неочікувана помилка, і сторінку не вдалося завантажити. Спробуйте
            перезавантажити — якщо проблема повторюється, зверніться до підтримки.
          </p>
          {error.digest && (
            <p style={{ fontSize: "0.75rem", color: "#9ca3af" }}>Код помилки: {error.digest}</p>
          )}
          <div style={{ display: "flex", gap: "0.5rem" }}>
            <button
              onClick={() => reset()}
              style={{
                padding: "0.5rem 1rem",
                borderRadius: "0.375rem",
                background: "#111827",
                color: "#fff",
                fontSize: "0.875rem",
                fontWeight: 500,
                border: "none",
                cursor: "pointer",
              }}
            >
              Спробувати ще раз
            </button>
            <button
              onClick={() => (window.location.href = "/")}
              style={{
                padding: "0.5rem 1rem",
                borderRadius: "0.375rem",
                background: "#fff",
                color: "#111827",
                fontSize: "0.875rem",
                fontWeight: 500,
                border: "1px solid #d1d5db",
                cursor: "pointer",
              }}
            >
              На головну
            </button>
          </div>
        </div>
      </body>
    </html>
  );
}
