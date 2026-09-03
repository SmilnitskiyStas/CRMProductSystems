"use client";

import { useState } from "react";
import { useTranslations } from "next-intl";
import { CalendarDays } from "lucide-react";
import { ModuleGate } from "@/features/modules/components/ModuleGate";
import { SupplierScheduleList } from "@/features/supplier-cabinet/components/schedules/SupplierScheduleList";
import { SupplierWeekGrid } from "@/features/supplier-cabinet/components/schedules/SupplierWeekGrid";
import { SupplierMyShifts } from "@/features/supplier-cabinet/components/schedules/SupplierMyShifts";
import { useSupplierSchedule } from "@/features/supplier-cabinet/hooks/useSupplierSchedules";
import { useMe } from "@/features/auth/hooks/useAuth";
import { SUPPLIER_ONLY, hasRole } from "@/lib/roles";
import type { WorkScheduleDto } from "@/features/schedules/types";

type Tab = "schedules" | "my-shifts";

function WeekGridPanel({ schedule, canManage }: { schedule: WorkScheduleDto; canManage: boolean }) {
  const t = useTranslations("Dashboard.supplierCabinet.schedules");
  const { data: detail, isLoading } = useSupplierSchedule(schedule.id);

  if (isLoading) {
    return <div style={{ padding: "24px", color: "#4B5563", fontSize: 13 }}>{t("loadingShifts")}</div>;
  }
  if (!detail) return null;

  return <SupplierWeekGrid schedule={detail} canManage={canManage} />;
}

export default function SupplierSchedulesPage() {
  const t = useTranslations("Dashboard.supplierCabinet.schedules");
  const tPages = useTranslations("Dashboard.supplierCabinet.pages");
  const { data: me } = useMe();
  const [activeTab, setActiveTab] = useState<Tab>("schedules");
  const [selectedSchedule, setSelected] = useState<WorkScheduleDto | null>(null);

  // null permissions = full/owner access; a restricted staff role manages schedules only
  // with workforce_management. Everyone else still gets "Мій розклад".
  const canManage = !me?.permissions || !!me.permissions.workforce_management;
  const effectiveTab: Tab = canManage ? activeTab : "my-shifts";

  if (me && !hasRole(me.role, SUPPLIER_ONLY)) {
    return (
      <div style={{ padding: "28px 32px", color: "#F87171", fontSize: 14 }}>
        {tPages("supplierOnlyAccess")}
      </div>
    );
  }

  const tabStyle = (tab: Tab): React.CSSProperties => ({
    padding: "8px 18px",
    borderRadius: 8,
    border: "none",
    background: effectiveTab === tab ? "#1D3461" : "transparent",
    color: effectiveTab === tab ? "#93C5FD" : "#6B7280",
    fontSize: 13,
    fontWeight: effectiveTab === tab ? 600 : 400,
    cursor: "pointer",
    transition: "background 0.1s, color 0.1s",
  });

  return (
    <ModuleGate moduleKey="supplier_workforce">
      <div style={{ padding: "28px 32px", minHeight: "100vh" }}>
        <div
          style={{
            display: "flex",
            alignItems: "flex-start",
            justifyContent: "space-between",
            marginBottom: 20,
            gap: 16,
            flexWrap: "wrap",
          }}
        >
          <div>
            <div style={{ display: "flex", alignItems: "center", gap: 10, marginBottom: 4 }}>
              <CalendarDays size={20} style={{ color: "#3B82F6" }} />
              <h1 style={{ color: "#E8EDF5", fontSize: 22, fontWeight: 700, margin: 0 }}>{t("pageTitle")}</h1>
            </div>
            <p style={{ color: "#4B5563", fontSize: 13, margin: 0 }}>{t("pageSubtitle")}</p>
          </div>

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
                {t("schedulesTab")}
              </button>
            )}
            <button style={tabStyle("my-shifts")} onClick={() => setActiveTab("my-shifts")}>
              {t("myShiftsTab")}
            </button>
          </div>
        </div>

        {effectiveTab === "my-shifts" && <SupplierMyShifts />}

        {effectiveTab === "schedules" && canManage && (
          <div
            style={{
              display: "grid",
              gridTemplateColumns: "280px 1fr",
              gap: 20,
              alignItems: "flex-start",
            }}
          >
            <div
              style={{
                background: "#0D1117",
                border: "1px solid #1F2937",
                borderRadius: 12,
                padding: 16,
              }}
            >
              <SupplierScheduleList selectedId={selectedSchedule?.id ?? null} onSelect={setSelected} />
            </div>

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
                  <CalendarDays size={32} style={{ opacity: 0.3 }} />
                  {t("chooseScheduleHint")}
                </div>
              )}
            </div>
          </div>
        )}
      </div>
    </ModuleGate>
  );
}
