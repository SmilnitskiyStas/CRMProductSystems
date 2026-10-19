"use client";

import { useState } from "react";
import { useTranslations } from "next-intl";
import { usePrimaryStoreId } from "@/lib/useStoreContext";
import { RegistersTab } from "@/features/registers/components/RegistersTab";
import { PinsTab } from "@/features/registers/components/PinsTab";
import { StatsTab } from "@/features/registers/components/StatsTab";
import { JournalTab } from "@/features/registers/components/JournalTab";

type TabKey = "registers" | "pins" | "stats" | "journal";
const TABS: TabKey[] = ["registers", "pins", "stats", "journal"];

export default function RegistersPage() {
  const t = useTranslations("Dashboard.registers");
  const [tab, setTab] = useState<TabKey>("registers");
  // Global store selector: empty = all stores the user may see (backend scopes by role).
  const storeId = usePrimaryStoreId();

  return (
    <div style={{ padding: "28px 32px" }}>
      <div style={{ marginBottom: 22 }}>
        <h1 style={{ color: "#E8EDF5", fontSize: 22, fontWeight: 700, margin: 0 }}>{t("title")}</h1>
        <p style={{ color: "#4B5563", fontSize: 13, marginTop: 6, marginBottom: 0 }}>{t("subtitle")}</p>
      </div>

      <div style={{ display: "flex", gap: 4, borderBottom: "1px solid #1F2937", marginBottom: 20 }}>
        {TABS.map((k) => (
          <button
            key={k}
            onClick={() => setTab(k)}
            style={{
              padding: "9px 16px",
              fontSize: 13,
              fontWeight: 600,
              cursor: "pointer",
              background: "transparent",
              border: "none",
              borderBottom: `2px solid ${tab === k ? "#3B82F6" : "transparent"}`,
              color: tab === k ? "#93C5FD" : "#6B7280",
            }}
          >
            {t(`tabs.${k}`)}
          </button>
        ))}
      </div>

      {tab === "registers" && <RegistersTab storeId={storeId} />}
      {tab === "pins" && <PinsTab storeId={storeId} />}
      {tab === "stats" && <StatsTab storeId={storeId} />}
      {tab === "journal" && <JournalTab storeId={storeId} />}
    </div>
  );
}
