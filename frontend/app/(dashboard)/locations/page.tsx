"use client";

import { useMemo, useState } from "react";
import Link from "next/link";
import { Plus, Map } from "lucide-react";
import { toast } from "sonner";
import { useTranslations } from "next-intl";
import { Btn } from "@/components/ui/Btn";
import {
  useLocations,
  useCreateLocation,
  useUpdateLocation,
} from "@/features/locations/hooks/useLocations";
import { LocationFormDialog } from "@/features/locations/components/LocationFormDialog";
import type { LocationDto, LocationType, LocationSortKey } from "@/features/locations/types";
import { useMe } from "@/features/auth/hooks/useAuth";
import { useStoreContext, useStoreScopeReady } from "@/lib/useStoreContext";
import { hasRole, AT_LEAST_ENTERPRISE_ADMIN } from "@/lib/roles";
import { Table, type TableColumn } from "@/components/ui/Table";

// ── Page ───────────────────────────────────────────────────────────────────────

export default function LocationsPage() {
  const t = useTranslations("Dashboard.locations.page");
  const tTypes = useTranslations("Dashboard.locations.types");
  const tCommon = useTranslations("Common");
  const { data: locations, isLoading } = useLocations();
  const ready = useStoreScopeReady();
  const showLoading = isLoading || !ready;
  const selectedStoreIds = useStoreContext((s) => s.selectedStoreIds);

  // No pagination on this page at all — it already fetches its full list — so search and sort
  // are both pure client-side stages here, no backend round trip needed (unlike the 4
  // server-paginated sibling pages this same task touches).
  const [searchText, setSearchText] = useState("");
  const [sortBy, setSortBy] = useState<LocationSortKey>("name");
  const [sortDescending, setSortDescending] = useState(false);
  function handleSort(key: LocationSortKey) {
    if (key === sortBy) setSortDescending((d) => !d);
    else {
      setSortBy(key);
      setSortDescending(true);
    }
  }

  // Locations IS the store list — the header selector's ids ARE Location ids — so
  // "filter by selected store(s)" is a pure client-side id filter, no backend involved.
  // Chain: store filter -> text search -> sort. Order doesn't affect correctness here (small
  // list), only which stage does less work first.
  const filteredLocations = useMemo(() => {
    if (!locations) return locations;
    let result = locations;
    if (selectedStoreIds.length > 0) {
      const selected = new Set(selectedStoreIds);
      result = result.filter((loc) => selected.has(loc.id));
    }
    const q = searchText.trim().toLowerCase();
    if (q) {
      result = result.filter(
        (loc) => loc.name.toLowerCase().includes(q) || (loc.address ?? "").toLowerCase().includes(q),
      );
    }
    // Copy before sorting — `result` may still be the exact `locations` reference from the
    // query cache when neither filter above matched anything, and Array.prototype.sort mutates
    // in place, which would corrupt React Query's cached data.
    const dir = sortDescending ? -1 : 1;
    result = [...result].sort((a, b) =>
      sortBy === "name" ? a.name.localeCompare(b.name) * dir : a.locationType.localeCompare(b.locationType) * dir,
    );
    return result;
  }, [locations, selectedStoreIds, searchText, sortBy, sortDescending]);
  const [dialog, setDialog] = useState<"create" | LocationDto | null>(null);
  const { data: me } = useMe();
  // Create/Update are AtLeastEnterpriseAdmin-only on the backend (ADR-020/ADR-022 —
  // Location management is HQ-only "infrastructure", deliberately NOT given the
  // capability-OR escape hatch other modules have). Mirror that gate here so lower
  // roles never see a button that would 403. GET stays open to all CanViewStock roles —
  // no whole-page gate, only the two mutating buttons below.
  const canManageLocations = hasRole(me?.role, AT_LEAST_ENTERPRISE_ADMIN);

  const create = useCreateLocation();
  const updateId = dialog && dialog !== "create" ? dialog.id : "";
  const update = useUpdateLocation(updateId);

  function handleSubmit(values: {
    name: string;
    address: string | null;
    locationType: LocationType;
    isActive: boolean;
    legalEntityId: string | null;
    regionCode: string | null;
  }) {
    if (dialog === "create") {
      create.mutate(
        {
          name: values.name,
          address: values.address,
          locationType: values.locationType,
          legalEntityId: values.legalEntityId,
          regionCode: values.regionCode,
        },
        {
          onSuccess: () => {
            toast.success(t("toastCreated"));
            setDialog(null);
          },
          onError: (e) => toast.error(t("toastError", { message: e.message })),
        }
      );
    } else if (dialog) {
      update.mutate(values, {
        onSuccess: () => {
          toast.success(t("toastUpdated"));
          setDialog(null);
        },
        onError: (e) => toast.error(t("toastError", { message: e.message })),
      });
    }
  }

  const isPending = create.isPending || update.isPending;

  const columns: TableColumn<LocationDto>[] = [
    {
      key: "name",
      header: t("headers.name"),
      sortKey: "name",
      render: (loc) => <span style={{ color: "#E8EDF5", fontWeight: 600, fontSize: 13 }}>{loc.name}</span>,
    },
    {
      key: "type",
      header: t("headers.type"),
      sortKey: "type",
      render: (loc) => (
        <span
          style={{
            background: "#1D2D4A",
            color: "#60A5FA",
            borderRadius: 6,
            padding: "2px 8px",
            fontSize: 11,
            fontWeight: 600,
          }}
        >
          {tTypes(loc.locationType)}
        </span>
      ),
    },
    {
      key: "address",
      header: t("headers.address"),
      cellStyle: { color: "#6B7280", fontSize: 13 },
      render: (loc) => loc.address ?? "—",
    },
    {
      key: "zones",
      header: t("headers.zones"),
      cellStyle: { color: "#9CA3AF", fontSize: 13 },
      render: (loc) => loc.zones.length,
    },
    {
      key: "status",
      header: t("headers.status"),
      render: (loc) => (
        <span style={{ color: loc.isActive ? "#22c55e" : "#6B7280", fontSize: 12, fontWeight: 600 }}>
          {loc.isActive ? t("statusActive") : t("statusInactive")}
        </span>
      ),
    },
    {
      key: "actions",
      header: "",
      render: (loc) => (
        <div style={{ display: "flex", alignItems: "center", gap: 8, justifyContent: "center" }}>
          <Link
            href={`/locations/${loc.id}/floor-plan`}
            style={{
              display: "inline-flex",
              alignItems: "center",
              gap: 4,
              background: "#0D2137",
              color: "#60A5FA",
              border: "1px solid #1D3461",
              borderRadius: 6,
              padding: "5px 10px",
              fontSize: 12,
              textDecoration: "none",
              fontWeight: 500,
            }}
          >
            <Map size={13} />
            {t("planLink")}
          </Link>
          {canManageLocations && (
            <Btn variant="ghost" size="sm" onClick={() => setDialog(loc)}>
              {t("edit")}
            </Btn>
          )}
        </div>
      ),
    },
  ];

  return (
    <div style={{ padding: "28px 32px", display: "flex", flexDirection: "column", gap: 20 }}>
      {/* Header */}
      <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", gap: 16 }}>
        <div>
          <h1 style={{ color: "#E8EDF5", fontSize: 22, fontWeight: 700, margin: 0 }}>{t("title")}</h1>
          <p style={{ color: "#4B5563", fontSize: 13, marginTop: 6, marginBottom: 0 }}>
            {t("subtitle")}
          </p>
        </div>
        {canManageLocations && (
          <Btn icon={<Plus size={15} />} onClick={() => setDialog("create")}>
            {t("newLocation")}
          </Btn>
        )}
      </div>

      {/* Search */}
      <input
        type="text"
        value={searchText}
        onChange={(e) => setSearchText(e.target.value)}
        placeholder={t("searchPlaceholder")}
        style={{
          background: "#111827",
          border: "1px solid #1F2937",
          borderRadius: 8,
          color: "#E8EDF5",
          fontSize: 13,
          padding: "7px 12px",
          outline: "none",
          width: 260,
        }}
      />

      {/* Table — no pagination here on purpose: Locations fetches its full list and both
          search and sort are pure client-side stages (see the comment above `searchText`
          state), so `onPageChange` stays unset and the shared Table renders no footer. */}
      <Table
        columns={columns}
        rows={filteredLocations ?? []}
        rowKey={(loc) => loc.id}
        sortBy={sortBy}
        sortDescending={sortDescending}
        onSort={handleSort}
        isLoading={showLoading}
        emptyMessage={showLoading ? tCommon("loading") : t("empty")}
      />

      {/* Dialog */}
      {dialog !== null && (
        <LocationFormDialog
          location={dialog === "create" ? null : dialog}
          isPending={isPending}
          onClose={() => setDialog(null)}
          onSubmit={handleSubmit}
        />
      )}
    </div>
  );
}
