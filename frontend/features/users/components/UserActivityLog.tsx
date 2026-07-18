"use client";

import { useTranslations, useLocale } from "next-intl";
import { useUserActivity } from "../hooks/useUsers";
import { getActionLabel } from "../types";

function formatDate(iso: string, locale: string) {
  return new Date(iso).toLocaleString(locale, {
    day: "2-digit", month: "2-digit", year: "numeric",
    hour: "2-digit", minute: "2-digit",
  });
}

export function UserActivityLog({ userId }: { userId: string }) {
  const t = useTranslations("Dashboard.users.activityLog");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";
  const { data: logs, isLoading, isError } = useUserActivity(userId, Boolean(userId));

  if (isLoading) {
    return (
      <div style={{ color: "#6B7280", fontSize: 13, padding: "20px 0" }}>
        {t("loading")}
      </div>
    );
  }

  if (!logs?.length) {
    return (
      <div
        style={{
          color: "#4B5563", fontSize: 13, padding: "32px 0",
          textAlign: "center",
        }}
      >
        {t("empty")}
      </div>
    );
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
            borderBottom: "1px solid #1A2235",
          }}
        >
          {/* Icon dot */}
          <div
            style={{
              width: 8, height: 8,
              borderRadius: "50%",
              background: "#3B82F6",
              flexShrink: 0,
              marginTop: 5,
            }}
          />
          <div style={{ flex: 1, minWidth: 0 }}>
            <div style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 500 }}>
              {getActionLabel(t, log.action)}
            </div>
            {log.meta && (
              <div style={{ color: "#4B5563", fontSize: 11, marginTop: 2, fontFamily: "monospace" }}>
                {log.meta}
              </div>
            )}
          </div>
          <div style={{ color: "#374151", fontSize: 11, flexShrink: 0, marginTop: 2 }}>
            {formatDate(log.createdAt, intlLocale)}
          </div>
        </div>
      ))}
    </div>
  );
}
