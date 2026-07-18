"use client";

import { useTranslations } from "next-intl";
import { NotificationSettingsTable } from "@/features/notifications/components/NotificationSettingsTable";

export function NotificationsTab() {
  const t = useTranslations("Dashboard.settings.notificationsTab");

  return (
    <div>
      <div style={{ marginBottom: 24 }}>
        <h2 style={{ color: "#E8EDF5", fontSize: 16, fontWeight: 600, margin: 0 }}>
          {t("title")}
        </h2>
        <p style={{ color: "#4B5563", fontSize: 13, marginTop: 6 }}>
          {t("subtitle")}
        </p>
      </div>
      <NotificationSettingsTable />
    </div>
  );
}
