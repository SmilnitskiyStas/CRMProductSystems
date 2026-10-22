"use client";

import { useEffect, useState } from "react";
import { useTranslations } from "next-intl";
import { Search, RotateCcw } from "lucide-react";
import { useUsers } from "@/features/users/hooks/useUsers";
import { useLocations } from "@/features/locations/hooks/useLocations";
import type { NotificationHistoryFilters, NotificationEventType } from "../types";
import { EVENT_TYPE_I18N_KEY, getEventTypeLabel } from "../types";
import { Btn } from "@/components/ui/Btn";
import { DetailDrawer } from "@/components/ui/DetailDrawer";
import { inputStyle, selectStyle, labelStyle } from "@/components/ui/fieldStyles";

interface Props {
  open: boolean;
  onClose: () => void;
  filters: NotificationHistoryFilters;
  onChange: (updater: (prev: NotificationHistoryFilters) => NotificationHistoryFilters) => void;
}

const EVENT_TYPES = Object.keys(EVENT_TYPE_I18N_KEY) as NotificationEventType[];

/**
 * Filter panel rendered in the shared DetailDrawer.
 */
export function NotificationFilterDrawer({ open, onClose, filters, onChange }: Props) {
  const t = useTranslations("Dashboard.notifications.filterDrawer");
  const tEventTypes = useTranslations("Dashboard.notifications.eventTypes");
  // Local draft for the search box only — debounced before it reaches `filters`
  // so we don't refetch on every keystroke. Other fields apply immediately.
  const [searchDraft, setSearchDraft] = useState(filters.search ?? "");

  // Keep the draft in sync when the panel (re)opens or filters are reset externally.
  useEffect(() => {
    if (open) setSearchDraft(filters.search ?? "");
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open]);

  useEffect(() => {
    if (!open) return;
    const handle = setTimeout(() => {
      onChange((prev) => {
        const next = searchDraft.trim() || undefined;
        if (prev.search === next) return prev;
        return { ...prev, search: next, page: 1 };
      });
    }, 300);
    return () => clearTimeout(handle);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [searchDraft, open]);

  const { data: users } = useUsers();
  const { data: locations } = useLocations();

  if (!open) return null;

  function set<K extends keyof NotificationHistoryFilters>(key: K, value: NotificationHistoryFilters[K]) {
    onChange((prev) => ({ ...prev, [key]: value, page: 1 }));
  }

  function handleReset() {
    setSearchDraft("");
    onChange((prev) => ({ page: 1, pageSize: prev.pageSize }));
  }

  const hasActiveFilters = Boolean(
    filters.search || filters.eventType || filters.userId || filters.storeId || filters.dateFrom || filters.dateTo,
  );

  return (
    <DetailDrawer isOpen={open} onClose={onClose} title={t("title")} width={380}>
        <div style={{ display: "flex", flexDirection: "column", gap: 18 }}>
          {/* Search */}
          <div>
            <label style={labelStyle}>{t("searchLabel")}</label>
            <div style={{ position: "relative" }}>
              <Search size={14} style={{ position: "absolute", left: 12, top: "50%", transform: "translateY(-50%)", color: "#4B5563" }} />
              <input
                type="text"
                value={searchDraft}
                onChange={(e) => setSearchDraft(e.target.value)}
                placeholder={t("searchPlaceholder")}
                style={{ ...inputStyle, paddingLeft: 34 }}
              />
            </div>
          </div>

          {/* Event type */}
          <div>
            <label style={labelStyle}>{t("categoryLabel")}</label>
            <select
              value={filters.eventType ?? ""}
              onChange={(e) => set("eventType", (e.target.value as NotificationEventType) || undefined)}
              style={selectStyle}
            >
              <option value="" style={{ background: "#0D1117" }}>{t("allCategoriesOption")}</option>
              {EVENT_TYPES.map((value) => (
                <option key={value} value={value} style={{ background: "#0D1117" }}>
                  {getEventTypeLabel(tEventTypes, value)}
                </option>
              ))}
            </select>
          </div>

          {/* Employee */}
          <div>
            <label style={labelStyle}>{t("employeeLabel")}</label>
            <select
              value={filters.userId ?? ""}
              onChange={(e) => set("userId", e.target.value || undefined)}
              style={selectStyle}
            >
              <option value="" style={{ background: "#0D1117" }}>{t("allEmployeesOption")}</option>
              {users?.map((u) => (
                <option key={u.id} value={u.id} style={{ background: "#0D1117" }}>
                  {u.fullName}
                </option>
              ))}
            </select>
          </div>

          {/* Store */}
          <div>
            <label style={labelStyle}>{t("storeLabel")}</label>
            <select
              value={filters.storeId ?? ""}
              onChange={(e) => set("storeId", e.target.value || undefined)}
              style={selectStyle}
            >
              <option value="" style={{ background: "#0D1117" }}>{t("allStoresOption")}</option>
              {locations?.map((l) => (
                <option key={l.id} value={l.id} style={{ background: "#0D1117" }}>
                  {l.name}
                </option>
              ))}
            </select>
          </div>

          {/* Date range */}
          <div>
            <label style={labelStyle}>{t("periodLabel")}</label>
            <div style={{ display: "flex", gap: 10 }}>
              <div style={{ flex: 1 }}>
                <label style={{ ...labelStyle, fontSize: 11 }}>{t("fromDateLabel")}</label>
                <input
                  type="date"
                  value={filters.dateFrom?.slice(0, 10) ?? ""}
                  max={filters.dateTo?.slice(0, 10)}
                  onChange={(e) => set("dateFrom", e.target.value || undefined)}
                  style={inputStyle}
                />
              </div>
              <div style={{ flex: 1 }}>
                <label style={{ ...labelStyle, fontSize: 11 }}>{t("toDateLabel")}</label>
                <input
                  type="date"
                  value={filters.dateTo?.slice(0, 10) ?? ""}
                  min={filters.dateFrom?.slice(0, 10)}
                  onChange={(e) => set("dateTo", e.target.value ? `${e.target.value}T23:59:59.999` : undefined)}
                  style={inputStyle}
                />
              </div>
            </div>
          </div>
        </div>

        <div style={{ marginTop: 24 }}>
          <Btn
            variant="ghost"
            fullWidth
            icon={<RotateCcw size={13} />}
            onClick={handleReset}
            disabled={!hasActiveFilters && !searchDraft}
          >
            {t("resetButton")}
          </Btn>
        </div>
    </DetailDrawer>
  );
}
