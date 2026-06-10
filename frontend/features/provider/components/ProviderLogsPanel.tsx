"use client";

import { useProviderLogs } from "../hooks/useProvider";

function formatDate(iso: string) {
  return new Date(iso).toLocaleString("uk-UA", {
    day: "2-digit", month: "2-digit", year: "numeric",
    hour: "2-digit", minute: "2-digit",
  });
}

const ACTION_COLORS: Record<string, string> = {
  "provider.impersonate": "#C4B5FD",
  "user.invited":         "#93C5FD",
  "user.deactivated":     "#F87171",
  "user.updated":         "#6EE7B7",
};

export function ProviderLogsPanel() {
  const { data: logs, isLoading } = useProviderLogs(50);

  if (isLoading) {
    return <div style={{ color: "#4B5563", fontSize: 13, padding: "16px 0" }}>Завантаження…</div>;
  }

  if (!logs?.length) {
    return <div style={{ color: "#4B5563", fontSize: 13, padding: "16px 0" }}>Логів немає</div>;
  }

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 1 }}>
      {logs.map((log) => (
        <div
          key={log.id}
          style={{
            display: "flex",
            alignItems: "flex-start",
            gap: 12,
            padding: "10px 0",
            borderBottom: "1px solid #111827",
          }}
        >
          <div
            style={{
              width: 8, height: 8,
              borderRadius: "50%",
              background: ACTION_COLORS[log.action] ?? "#374151",
              flexShrink: 0,
              marginTop: 5,
            }}
          />
          <div style={{ flex: 1, minWidth: 0 }}>
            <div style={{ color: "#E8EDF5", fontSize: 12, fontWeight: 500 }}>
              {log.action}
            </div>
            {log.meta && (
              <div style={{ color: "#4B5563", fontSize: 11, marginTop: 2, wordBreak: "break-word" }}>
                {log.meta}
              </div>
            )}
          </div>
          <div style={{ color: "#374151", fontSize: 11, flexShrink: 0, marginTop: 2, textAlign: "right" }}>
            {formatDate(log.createdAt)}
          </div>
        </div>
      ))}
    </div>
  );
}
