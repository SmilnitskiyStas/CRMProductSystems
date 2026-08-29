"use client";

import { useTranslations } from "next-intl";
import { BarChart2, Clock, MessageSquare, Ticket, CheckCircle2 } from "lucide-react";
import { Table, type TableColumn } from "@/components/ui/Table";
import { useProviderStats } from "../hooks/useProviderStats";
import type { ProviderMemberStatsDto } from "../api/providerStatsApi";

const ROLE_COLORS: Record<string, string> = {
  provider:       "#A78BFA",
  provider_admin: "#60A5FA",
  provider_agent: "#4ADE80",
};

function StatCell({ value, label, icon, color }: { value: string | number; label: string; icon: React.ReactNode; color: string }) {
  return (
    <div>
      <div style={{ color, fontSize: 14, fontWeight: 700, fontFamily: "monospace" }}>{value}</div>
      <div style={{ color: "#4B5563", fontSize: 10, marginTop: 2, display: "flex", alignItems: "center", justifyContent: "center", gap: 3 }}>
        {icon}
        {label}
      </div>
    </div>
  );
}

export function StatsTab() {
  const t = useTranslations("Dashboard.provider.statsTab");
  const tRoleLabels = useTranslations("Dashboard.provider.roleLabels");
  const { data: stats, isLoading } = useProviderStats();

  const columns: TableColumn<ProviderMemberStatsDto>[] = [
    {
      key: "member",
      header: t("headerMember"),
      align: "left",
      render: (m) => {
        const resolveRate = m.ticketsAssigned > 0
          ? Math.round((m.ticketsResolved / m.ticketsAssigned) * 100)
          : null;
        return (
          <div style={{ opacity: m.isActive ? 1 : 0.5 }}>
            <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
              <span style={{ color: "#E8EDF5", fontSize: 14, fontWeight: 500 }}>{m.fullName}</span>
              <span style={{
                padding: "1px 8px", borderRadius: 20, fontSize: 10, fontWeight: 600,
                background: `${ROLE_COLORS[m.role] ?? "#6B7280"}22`,
                border: `1px solid ${ROLE_COLORS[m.role] ?? "#6B7280"}55`,
                color: ROLE_COLORS[m.role] ?? "#6B7280",
              }}>
                {tRoleLabels.has(m.role) ? tRoleLabels(m.role) : m.role}
              </span>
              {!m.isActive && (
                <span style={{ color: "#4B5563", fontSize: 10 }}>{t("inactiveLabel")}</span>
              )}
            </div>
            {resolveRate !== null && (
              <div style={{ marginTop: 4, height: 3, background: "#1F2937", borderRadius: 2, maxWidth: 120 }}>
                <div style={{
                  height: "100%", borderRadius: 2,
                  width: `${resolveRate}%`,
                  background: resolveRate >= 70 ? "#4ADE80" : resolveRate >= 40 ? "#FBBF24" : "#F87171",
                }} />
              </div>
            )}
          </div>
        );
      },
    },
    {
      key: "assigned",
      header: t("headerAssigned"),
      render: (m) => (
        <StatCell value={m.ticketsAssigned} label={t("labelTickets")} icon={<Ticket size={9} />} color="#60A5FA" />
      ),
    },
    {
      key: "resolved",
      header: t("headerResolved"),
      render: (m) => (
        <StatCell value={m.ticketsResolved} label={t("labelResolved")} icon={<CheckCircle2 size={9} />} color="#4ADE80" />
      ),
    },
    {
      key: "created",
      header: t("headerCreated"),
      render: (m) => (
        <StatCell value={m.ticketsCreatedByProvider} label={t("labelCreated")} icon={<Ticket size={9} />} color="#A78BFA" />
      ),
    },
    {
      key: "comments",
      header: t("headerComments"),
      render: (m) => (
        <StatCell value={m.commentsWritten} label={t("labelComments")} icon={<MessageSquare size={9} />} color="#FBBF24" />
      ),
    },
    {
      key: "avgTime",
      header: t("headerAvgTime"),
      render: (m) => (
        <StatCell
          value={m.avgResolutionHours !== null ? t("avgHoursValue", { hours: m.avgResolutionHours }) : "—"}
          label={t("labelAvgTime")}
          icon={<Clock size={9} />}
          color={m.avgResolutionHours !== null && m.avgResolutionHours < 24 ? "#4ADE80" : "#F87171"}
        />
      ),
    },
  ];

  return (
    <div style={{ background: "#0D1117", border: "1px solid #1F2937", borderRadius: 12, padding: "20px 24px" }}>
      <div style={{ display: "flex", alignItems: "center", gap: 8, marginBottom: 20 }}>
        <BarChart2 size={18} color="#60A5FA" />
        <h2 style={{ color: "#E8EDF5", fontSize: 16, fontWeight: 600, margin: 0 }}>
          {t("title")}
        </h2>
      </div>

      <Table
        columns={columns}
        rows={stats ?? []}
        rowKey={(m) => m.memberId}
        isLoading={isLoading}
        emptyMessage={isLoading ? t("loading") : t("noData")}
      />
    </div>
  );
}
