"use client";

import { Clock } from "lucide-react";
import { NotificationHistoryList } from "@/features/notifications/components/NotificationHistoryList";

export default function NotificationsPage() {
  return (
    <div style={{ padding: "28px 32px", maxWidth: 860 }}>
      <div style={{ marginBottom: 28 }}>
        <h1 style={{ color: "#E8EDF5", fontSize: 22, fontWeight: 700, margin: 0 }}>
          Сповіщення
        </h1>
        <p style={{ color: "#4B5563", fontSize: 14, marginTop: 6 }}>
          Історія надісланих сповіщень. Налаштування каналів —{" "}
          <a href="/settings?tab=notifications" style={{ color: "#3B82F6" }}>
            на сторінці Налаштування
          </a>
          .
        </p>
      </div>

      <div style={{ display: "flex", alignItems: "center", gap: 8, marginBottom: 20 }}>
        <Clock size={15} style={{ color: "#4B5563" }} />
        <span style={{ color: "#4B5563", fontSize: 13 }}>Остання активність</span>
      </div>

      <div
        style={{
          background: "#0D1117",
          border: "1px solid #1F2937",
          borderRadius: 12,
          padding: 24,
        }}
      >
        <NotificationHistoryList />
      </div>
    </div>
  );
}
