"use client";

import { NotificationSettingsTable } from "@/features/notifications/components/NotificationSettingsTable";

export function NotificationsTab() {
  return (
    <div>
      <div style={{ marginBottom: 24 }}>
        <h2 style={{ color: "#E8EDF5", fontSize: 16, fontWeight: 600, margin: 0 }}>
          Канали сповіщень
        </h2>
        <p style={{ color: "#4B5563", fontSize: 13, marginTop: 6 }}>
          Увімкніть або вимкніть сповіщення для кожної події та каналу зв'язку.
        </p>
      </div>
      <NotificationSettingsTable />
    </div>
  );
}
