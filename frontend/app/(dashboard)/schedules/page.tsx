"use client";

import { useState } from "react";
import { Calendar } from "lucide-react";
import { ScheduleList } from "@/features/schedules/components/ScheduleList";
import { WeekGrid } from "@/features/schedules/components/WeekGrid";
import { MyShifts } from "@/features/schedules/components/MyShifts";
import { useSchedule } from "@/features/schedules/hooks/useSchedules";
import { useMe } from "@/features/auth/hooks/useAuth";
import { AT_LEAST_STORE_MANAGER, PROVIDER_TEAM } from "@/lib/roles";
import type { WorkScheduleDto } from "@/features/schedules/types";
import type { AppRole } from "@/lib/roles";
import { ScheduleTab } from "@/features/provider/components/ScheduleTab";

type Tab = "schedules" | "my-shifts";

function WeekGridPanel({
  schedule,
  canManage,
}: {
  schedule: WorkScheduleDto;
  canManage: boolean;
}) {
  const { data: detail, isLoading } = useSchedule(schedule.id);

  if (isLoading) {
    return (
      <div style={{ padding: "24px", color: "#4B5563", fontSize: 13 }}>Завантаження змін…</div>
    );
  }

  if (!detail) return null;

  return <WeekGrid schedule={detail} canManage={canManage} />;
}

export default function SchedulesPage() {
  const [activeTab, setActiveTab]         = useState<Tab>("schedules");
  const [selectedSchedule, setSelected]   = useState<WorkScheduleDto | null>(null);

  const { data: me } = useMe();
  const userRole = (me?.role ?? "") as AppRole;
  const canManage = AT_LEAST_STORE_MANAGER.has(userRole);

  // Provider team → own weekly slot schedule (not tenant-based WorkSchedule)
  if (PROVIDER_TEAM.has(userRole)) {
    return (
      <div style={{ padding: "28px 32px" }}>
        <div style={{ marginBottom: 20 }}>
          <div style={{ display: "flex", alignItems: "center", gap: 10, marginBottom: 4 }}>
            <Calendar size={20} style={{ color: "#3B82F6" }} />
            <h1 style={{ color: "#E8EDF5", fontSize: 22, fontWeight: 700, margin: 0 }}>
              Розклад команди
            </h1>
          </div>
          <p style={{ color: "#4B5563", fontSize: 13, margin: 0 }}>
            Тижнева доступність агентів підтримки
          </p>
        </div>
        <ScheduleTab />
      </div>
    );
  }

  const tabStyle = (tab: Tab): React.CSSProperties => ({
    padding: "8px 18px",
    borderRadius: 8,
    border: "none",
    background: activeTab === tab ? "#1D3461" : "transparent",
    color: activeTab === tab ? "#93C5FD" : "#6B7280",
    fontSize: 13,
    fontWeight: activeTab === tab ? 600 : 400,
    cursor: "pointer",
    transition: "background 0.1s, color 0.1s",
  });

  return (
    <div style={{ padding: "28px 32px", minHeight: "100vh" }}>
      {/* Header */}
      <div
        style={{
          display: "flex",
          alignItems: "flex-start",
          justifyContent: "space-between",
          marginBottom: 20,
        }}
      >
        <div>
          <div style={{ display: "flex", alignItems: "center", gap: 10, marginBottom: 4 }}>
            <Calendar size={20} style={{ color: "#3B82F6" }} />
            <h1 style={{ color: "#E8EDF5", fontSize: 22, fontWeight: 700, margin: 0 }}>
              Розклад змін
            </h1>
          </div>
          <p style={{ color: "#4B5563", fontSize: 13, margin: 0 }}>
            Управління робочими графіками та змінами персоналу
          </p>
        </div>

        {/* Tabs */}
        <div
          style={{
            display: "flex",
            gap: 4,
            background: "#111827",
            border: "1px solid #1F2937",
            borderRadius: 10,
            padding: 4,
          }}
        >
          {canManage && (
            <button style={tabStyle("schedules")} onClick={() => setActiveTab("schedules")}>
              Графіки
            </button>
          )}
          <button style={tabStyle("my-shifts")} onClick={() => setActiveTab("my-shifts")}>
            Мій розклад
          </button>
        </div>
      </div>

      {/* My shifts tab */}
      {activeTab === "my-shifts" && <MyShifts />}

      {/* Schedules tab */}
      {activeTab === "schedules" && canManage && (
        <div
          style={{
            display: "grid",
            gridTemplateColumns: "280px 1fr",
            gap: 20,
            alignItems: "flex-start",
          }}
        >
          {/* Left panel: list */}
          <div
            style={{
              background: "#0D1117",
              border: "1px solid #1F2937",
              borderRadius: 12,
              padding: 16,
            }}
          >
            <ScheduleList
              selectedId={selectedSchedule?.id ?? null}
              onSelect={setSelected}
            />
          </div>

          {/* Right panel: week grid */}
          <div
            style={{
              background: "#0D1117",
              border: "1px solid #1F2937",
              borderRadius: 12,
              padding: 16,
              minHeight: 400,
            }}
          >
            {selectedSchedule ? (
              <>
                <div style={{ marginBottom: 16 }}>
                  <h2 style={{ color: "#E8EDF5", fontSize: 15, fontWeight: 700, margin: 0 }}>
                    {selectedSchedule.name}
                  </h2>
                  <p style={{ color: "#4B5563", fontSize: 12, margin: "4px 0 0" }}>
                    {selectedSchedule.locationName}
                  </p>
                </div>
                <WeekGridPanel schedule={selectedSchedule} canManage={canManage} />
              </>
            ) : (
              <div
                style={{
                  display: "flex",
                  flexDirection: "column",
                  alignItems: "center",
                  justifyContent: "center",
                  height: 300,
                  color: "#4B5563",
                  fontSize: 13,
                  gap: 8,
                }}
              >
                <Calendar size={32} style={{ opacity: 0.3 }} />
                Оберіть графік зі списку
              </div>
            )}
          </div>
        </div>
      )}
    </div>
  );
}
