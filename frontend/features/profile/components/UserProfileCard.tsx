"use client";

import { useMe } from "@/features/auth/hooks/useAuth";
import { ROLE_LABELS } from "../types";

const labelStyle: React.CSSProperties = {
  display: "block",
  color: "#6B7280",
  fontSize: 11,
  fontWeight: 500,
  marginBottom: 4,
  textTransform: "uppercase",
  letterSpacing: "0.05em",
};

const valueStyle: React.CSSProperties = {
  color: "#E8EDF5",
  fontSize: 13,
  fontWeight: 500,
};

const emptyValueStyle: React.CSSProperties = {
  ...valueStyle,
  color: "#374151",
  fontStyle: "italic",
};

function InfoRow({ label, value }: { label: string; value?: string | null }) {
  return (
    <div style={{ padding: "12px 0", borderBottom: "1px solid #1A2235" }}>
      <span style={labelStyle}>{label}</span>
      {value ? (
        <span style={valueStyle}>{value}</span>
      ) : (
        <span style={emptyValueStyle}>—</span>
      )}
    </div>
  );
}

export function UserProfileCard() {
  const { data: me, isLoading } = useMe();

  if (isLoading) {
    return (
      <div style={{ display: "flex", alignItems: "center", justifyContent: "center", height: 200 }}>
        <div style={{ color: "#374151", fontSize: 13 }}>Завантаження…</div>
      </div>
    );
  }

  const initials = me?.fullName
    ? me.fullName.split(" ").slice(0, 2).map((n: string) => n[0]).join("").toUpperCase()
    : "?";

  return (
    <div>
      {/* Avatar + name block */}
      <div
        style={{
          display: "flex",
          alignItems: "center",
          gap: 16,
          padding: "20px 0 24px",
          borderBottom: "1px solid #1F2937",
          marginBottom: 8,
        }}
      >
        <div
          style={{
            width: 72,
            height: 72,
            borderRadius: "50%",
            background: "linear-gradient(135deg, #3B82F6, #6366F1)",
            display: "flex",
            alignItems: "center",
            justifyContent: "center",
            color: "#fff",
            fontSize: 26,
            fontWeight: 700,
            flexShrink: 0,
            boxShadow: "0 0 0 3px rgba(59,130,246,0.15)",
          }}
        >
          {initials}
        </div>
        <div>
          <div style={{ color: "#E8EDF5", fontSize: 18, fontWeight: 700, lineHeight: 1.2 }}>
            {me?.fullName ?? "—"}
          </div>
          <div
            style={{
              display: "inline-flex",
              alignItems: "center",
              marginTop: 6,
              background: "#1A2235",
              border: "1px solid #1F2937",
              borderRadius: 20,
              padding: "3px 10px",
            }}
          >
            <span style={{ color: "#3B82F6", fontSize: 11, fontWeight: 600 }}>
              {ROLE_LABELS[me?.role ?? ""] ?? me?.role ?? "—"}
            </span>
          </div>
        </div>
      </div>

      {/* Info rows */}
      <div>
        <InfoRow label="Email"   value={me?.email} />
        <InfoRow label="Телефон" value={me?.phone} />
        <InfoRow
          label="Магазин"
          value={me?.storeId ? `Магазин #${me.storeId.slice(0, 8)}` : null}
        />
        <InfoRow
          label="Telegram"
          value={me?.telegramChatId ? "Підключено ✓" : null}
        />
        <div style={{ padding: "12px 0" }}>
          <span style={labelStyle}>ID акаунта</span>
          <span style={{ color: "#374151", fontSize: 11, fontFamily: "monospace" }}>
            {me?.id ?? "—"}
          </span>
        </div>
      </div>

      {/* Footer hint */}
      <div
        style={{
          marginTop: 20,
          padding: "12px 14px",
          background: "#0A1628",
          border: "1px solid #1F2937",
          borderRadius: 8,
        }}
      >
        <p style={{ color: "#4B5563", fontSize: 12, margin: 0, lineHeight: 1.5 }}>
          Для зміни особистих даних, пароля або підключення Telegram перейдіть до{" "}
          <a
            href="/settings-user"
            style={{ color: "#3B82F6", textDecoration: "none" }}
          >
            Налаштування профілю
          </a>
        </p>
      </div>
    </div>
  );
}
