"use client";

import { useState } from "react";
import { useTranslations } from "next-intl";
import { Calendar } from "lucide-react";
import { ScheduleList } from "@/features/schedules/components/ScheduleList";
import { WeekGrid } from "@/features/schedules/components/WeekGrid";
import { MyShifts } from "@/features/schedules/components/MyShifts";
import { useSchedule, useUpdateSchedule } from "@/features/schedules/hooks/useSchedules";
import { Btn } from "@/components/ui/Btn";
import { PageContainer } from "@/components/ui/PageContainer";
import { PageHeader } from "@/components/ui/PageHeader";
import { Tabs } from "@/components/ui/Tabs";
import { StatusPill } from "@/components/ui/StatusPill";
import { toast } from "sonner";
import { useMe } from "@/features/auth/hooks/useAuth";
import { AT_LEAST_STORE_MANAGER, PROVIDER_TEAM } from "@/lib/roles";
import type { WorkScheduleDto, ScheduleStatus } from "@/features/schedules/types";
import type { AppRole } from "@/lib/roles";
import { ScheduleTab } from "@/features/provider/components/ScheduleTab";
import { useRequireTab } from "@/lib/useRequireTab";

type Tab = "schedules" | "my-shifts";

function WeekGridPanel({
  schedule,
  canManage,
}: {
  schedule: WorkScheduleDto;
  canManage: boolean;
}) {
  const t = useTranslations("Dashboard.schedules.page");
  const tList = useTranslations("Dashboard.schedules.list");
  const { data: detail, isLoading } = useSchedule(schedule.id);
  const update = useUpdateSchedule();

  // The list snapshot passed from the page goes stale after a status change — the detail
  // query is invalidated by the mutation, so it is the source of truth for the status.
  const status = detail?.status ?? schedule.status;

  function setStatus(next: ScheduleStatus) {
    update.mutate(
      { id: schedule.id, data: { name: detail?.name ?? schedule.name, status: next } },
      {
        onSuccess: () => toast.success(t(next === "published" ? "publishedToast" : "unpublishedToast")),
        onError: (err) => toast.error(t("publishError", { message: err.message })),
      },
    );
  }

  if (isLoading) {
    return (
      <div style={{ padding: "24px", color: "#4B5563", fontSize: 13 }}>{t("loadingShifts")}</div>
    );
  }

  if (!detail) return null;

  return (
    <>
      <div style={{ display: "flex", alignItems: "center", gap: 10, marginBottom: 14, flexWrap: "wrap" }}>
        <StatusPill
          bg={status === "published" ? "#052e16" : "#111827"}
          text={status === "published" ? "#4ADE80" : "#9CA3AF"}
          style={{ border: `1px solid ${status === "published" ? "#16A34A" : "#1F2937"}` }}
        >
          {tList(status === "published" ? "statusPublished" : status === "archived" ? "statusArchived" : "statusDraft")}
        </StatusPill>
        {canManage && status !== "published" && (
          <Btn size="sm" disabled={update.isPending} onClick={() => setStatus("published")}>
            {t("publishButton")}
          </Btn>
        )}
        {canManage && status === "published" && (
          <Btn size="sm" variant="ghost" disabled={update.isPending} onClick={() => setStatus("draft")}>
            {t("unpublishButton")}
          </Btn>
        )}
        {status === "draft" && (
          <span style={{ color: "#6B7280", fontSize: 12 }}>{t("draftHint")}</span>
        )}
      </div>
      <WeekGrid schedule={detail} canManage={canManage} />
    </>
  );
}

export default function SchedulesPage() {
  const t = useTranslations("Dashboard.schedules.page");
  const [activeTab, setActiveTab]         = useState<Tab>("schedules");
  const [selectedSchedule, setSelected]   = useState<WorkScheduleDto | null>(null);

  const { data: me } = useMe();
  const userRole = (me?.role ?? "") as AppRole;
  const canManage = AT_LEAST_STORE_MANAGER.has(userRole);

  // Sidebar tab visibility (TASK-391c; authoritative/exclusive as of TASK-397; per-item
  // granularity as of TASK-399): mirrors Sidebar.tsx's /schedules NavItem, which carries no
  // `roles` restriction — every authenticated tenant user (incl. provider-team, who see their
  // own branch below) reaches this page today for their own shifts, gated separately (not by
  // tab/role) inside the page itself. `alreadyAllowed=true` preserves that baseline UNLESS the
  // caller's TenantRole has a non-empty "Видимі розділи" configured that excludes BOTH
  // "/schedules" (item-level) and "workforce" (whole-group, backward compat) — useRequireTab's
  // override semantic now makes the tabs claim authoritative in that case, same as Sidebar
  // hiding the /schedules NavItem (whether by excluding it individually or by hiding the whole
  // "workforce" group it lives inside, alongside /users). In other words: an admin who unchecks
  // "Розклад" (or the whole "Персонал"/workforce group) for a TenantRole also blocks that
  // role's own "Мій розклад" self-service view, not just the team-management links — a real,
  // deliberate consequence of TASK-397's fix (Sidebar and page guard must agree), not a bug.
  useRequireTab("/schedules", "workforce", true);

  // Provider team → own weekly slot schedule (not tenant-based WorkSchedule)
  if (PROVIDER_TEAM.has(userRole)) {
    return (
      <PageContainer>
        <PageHeader
          title={
            <span style={{ display: "inline-flex", alignItems: "center", gap: 10 }}>
              <Calendar size={20} style={{ color: "#3B82F6" }} />
              {t("teamTitle")}
            </span>
          }
          subtitle={t("teamSubtitle")}
        />
        <ScheduleTab />
      </PageContainer>
    );
  }

  return (
    <PageContainer style={{ minHeight: "100vh" }}>
      <PageHeader
        title={
          <span style={{ display: "inline-flex", alignItems: "center", gap: 10 }}>
            <Calendar size={20} style={{ color: "#3B82F6" }} />
            {t("title")}
          </span>
        }
        subtitle={t("subtitle")}
      />

      <Tabs<Tab>
        items={[
          ...(canManage ? [{ key: "schedules" as Tab, label: t("schedulesTab") }] : []),
          { key: "my-shifts" as Tab, label: t("myShiftsTab") },
        ]}
        value={activeTab}
        onChange={setActiveTab}
      />

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
                {t("chooseScheduleHint")}
              </div>
            )}
          </div>
        </div>
      )}
    </PageContainer>
  );
}
