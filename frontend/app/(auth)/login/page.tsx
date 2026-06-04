import { LoginForm } from "@/features/auth/components/LoginForm";

export const metadata = { title: "Вхід — ShelfGuard" };

export default function LoginPage() {
  return (
    <div
      style={{
        width: "100%",
        maxWidth: 400,
        padding: "0 16px",
      }}
    >
      {/* Card */}
      <div
        style={{
          background: "#161B27",
          border: "1px solid #2A3347",
          borderRadius: 6,
          padding: "36px 32px",
        }}
      >
        {/* Logo */}
        <div style={{ marginBottom: 28, textAlign: "center" }}>
          <div
            style={{
              display: "inline-flex",
              alignItems: "center",
              gap: 10,
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
          </div>
          <p
            style={{
              marginTop: 6,
              fontSize: 12,
              color: "#8A94A8",
              fontFamily: '"Inter", sans-serif',
            }}
          >
            Система управління залишками
          </p>
        </div>

        {/* Divider */}
        <div style={{ borderTop: "1px solid #2A3347", marginBottom: 24 }} />

        <LoginForm />
      </div>

      {/* Footer */}
      <p
        style={{
          marginTop: 20,
          textAlign: "center",
          fontSize: 11,
          color: "#8A94A840",
          fontFamily: '"Inter", sans-serif',
        }}
      >
        ShelfGuard v1.0 · B2B SaaS
      </p>
    </div>
  );
}
