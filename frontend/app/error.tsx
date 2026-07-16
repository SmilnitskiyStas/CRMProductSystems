"use client";

import { useEffect } from "react";
import { Button } from "@/components/ui/button";

// Next.js App Router error boundary — catches render/runtime errors thrown
// anywhere under this segment (i.e. everywhere except the root layout itself,
// which is covered by global-error.tsx). Must be a Client Component.
export default function ErrorBoundary({
  error,
  reset,
}: {
  error: Error & { digest?: string };
  reset: () => void;
}) {
  useEffect(() => {
    // TODO(KI-020): send to Sentry/error-tracking once a DSN exists — see known-issues.md.
    // eslint-disable-next-line no-console
    console.error("Unhandled application error:", error);
  }, [error]);

  return (
    <div className="flex min-h-[60vh] flex-col items-center justify-center gap-4 px-4 text-center">
      <div className="rounded-full bg-destructive/10 p-4">
        <svg
          xmlns="http://www.w3.org/2000/svg"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          strokeWidth={1.5}
          className="h-10 w-10 text-destructive"
          aria-hidden="true"
        >
          <path
            strokeLinecap="round"
            strokeLinejoin="round"
            d="M12 9v3.75m9-.75a9 9 0 11-18 0 9 9 0 0118 0zm-9 3.75h.008v.008H12v-.008z"
          />
        </svg>
      </div>
      <h1 className="text-xl font-semibold text-foreground">Щось пішло не так</h1>
      <p className="max-w-md text-sm text-muted-foreground">
        Сталася неочікувана помилка. Спробуйте ще раз — якщо проблема повторюється,
        зверніться до підтримки.
      </p>
      {error.digest && (
        <p className="text-xs text-muted-foreground/70">Код помилки: {error.digest}</p>
      )}
      <div className="flex gap-2">
        <Button onClick={() => reset()}>Спробувати ще раз</Button>
        <Button variant="outline" onClick={() => (window.location.href = "/")}>
          На головну
        </Button>
      </div>
    </div>
  );
}
