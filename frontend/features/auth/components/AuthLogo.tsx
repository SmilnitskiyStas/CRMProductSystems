import Link from "next/link";

/**
 * Shared logo header for every public auth surface (login, forgot-password,
 * reset-password). Wraps the shield + wordmark in a `<Link href="/">` back to the public
 * marketing landing — previously a plain, unclickable `<span>` in LoginCard.tsx, a dead
 * end for anyone who landed on `/login` directly (TASK-457, Частина A).
 *
 * Inline `style={{}}` on purpose — matches the dark auth-card styling already used by
 * LoginCard.tsx/LoginForm.tsx. Deliberately NOT `features/landing/components/Logo.tsx`
 * (Tailwind-based) — a different design system; importing it here would look out of
 * place against this card.
 */
export function AuthLogo() {
  return (
    <Link
      href="/"
      style={{
        display: "inline-flex",
        alignItems: "center",
        gap: 10,
        textDecoration: "none",
      }}
    >
      {/* Shield icon */}
      <svg width="28" height="28" viewBox="0 0 28 28" fill="none">
        <path
          d="M14 2L4 6.5V13C4 18.5 8.5 23.5 14 25.5C19.5 23.5 24 18.5 24 13V6.5L14 2Z"
          fill="#2D7DD2"
          fillOpacity="0.2"
          stroke="#2D7DD2"
          strokeWidth="1.5"
          strokeLinejoin="round"
        />
        <path
          d="M10 14L12.5 16.5L18 11"
          stroke="#2D7DD2"
          strokeWidth="1.5"
          strokeLinecap="round"
          strokeLinejoin="round"
        />
      </svg>
      <span
        style={{
          fontSize: 20,
          fontWeight: 700,
          color: "#E8EDF5",
          fontFamily: '"Inter", sans-serif',
          letterSpacing: "-0.01em",
        }}
      >
        ShelfGuard
      </span>
    </Link>
  );
}
