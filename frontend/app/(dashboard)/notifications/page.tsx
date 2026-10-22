"use client";

import { useState } from "react";
import { useTranslations } from "next-intl";
import { Clock, Filter } from "lucide-react";
import { useMe } from "@/features/auth/hooks/useAuth";
import { canAccessTenantSettings } from "@/lib/roles";
import { NotificationHistoryList } from "@/features/notifications/components/NotificationHistoryList";
import { NotificationFilterDrawer } from "@/features/notifications/components/NotificationFilterDrawer";
import type { NotificationHistoryFilters } from "@/features/notifications/types";
import { Btn } from "@/components/ui/Btn";
import { PageContainer } from "@/components/ui/PageContainer";
import { PageHeader } from "@/components/ui/PageHeader";

const DEFAULT_PAGE_SIZE = 20;

function countActiveFilters(f: NotificationHistoryFilters): number {
  return [f.search, f.eventType, f.userId, f.storeId, f.dateFrom, f.dateTo].filter(Boolean).length;
}

export default function NotificationsPage() {
  const t = useTranslations("Dashboard.notifications.page");
  const [filters, setFilters] = useState<NotificationHistoryFilters>({
    page: 1,
    pageSize: DEFAULT_PAGE_SIZE,
  });
  const [filtersOpen, setFiltersOpen] = useState(false);
  const { data: me } = useMe();
  // Business tenants have no Settings page — no link into it for them.
  const canOpenSettings = canAccessTenantSettings(me?.role);
  const activeCount = countActiveFilters(filters);

  return (
    <PageContainer style={{ maxWidth: 860 }}>
      <PageHeader
        title={t("title")}
        subtitle={
          canOpenSettings ? (
            <>
              {t("subtitlePrefix")}{" "}
              <a href="/settings?tab=notifications" style={{ color: "#3B82F6" }}>
                {t("settingsLinkText")}
              </a>
              .
            </>
          ) : (
            t("subtitleNoSettings")
          )
        }
        actions={
          <Btn
            variant={activeCount > 0 ? "primary" : "ghost"}
            icon={<Filter size={14} />}
            onClick={() => setFiltersOpen(true)}
          >
            {t("filtersButton")}
            {activeCount > 0 && (
              <span style={{
                display: "flex", alignItems: "center", justifyContent: "center",
                minWidth: 18, height: 18, padding: "0 4px",
                background: "#3B82F6", color: "#fff",
                borderRadius: 9, fontSize: 11, fontWeight: 700,
              }}>
                {activeCount}
              </span>
            )}
          </Btn>
        }
      />

      <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
        <Clock size={15} style={{ color: "#4B5563" }} />
        <span style={{ color: "#4B5563", fontSize: 13 }}>{t("recentActivityLabel")}</span>
      </div>

      <div
        style={{
          background: "#0D1117",
          border: "1px solid #1F2937",
          borderRadius: 12,
          padding: 24,
        }}
      >
        <NotificationHistoryList filters={filters} onFiltersChange={setFilters} />
      </div>

      <NotificationFilterDrawer
        open={filtersOpen}
        onClose={() => setFiltersOpen(false)}
        filters={filters}
        onChange={setFilters}
      />
    </PageContainer>
  );
}
