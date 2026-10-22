"use client";

import { useMemo, useState } from "react";
import Link from "next/link";
import { useParams } from "next/navigation";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useLocale, useTranslations } from "next-intl";
import { toast } from "sonner";
import { Btn } from "@/components/ui/Btn";
import { PageContainer } from "@/components/ui/PageContainer";
import { PageHeader } from "@/components/ui/PageHeader";
import { BackLink } from "@/components/ui/BackLink";
import { filterInputStyle } from "@/components/ui/fieldStyles";
import { Table, type TableColumn } from "@/components/ui/Table";
import { useMe } from "@/features/auth/hooks/useAuth";
import { useLocation } from "@/features/locations/hooks/useLocations";
import { usersApi } from "@/features/users/api/users";
import { USERS_KEY } from "@/features/users/hooks/useUsers";
import { LocationsMultiSelectDropdown } from "@/features/users/components/LocationsMultiSelectDropdown";
import { isLocationScopedRole, type UserDto } from "@/features/users/types";
import { getRoleLabel, ROLE_KEYS } from "@/features/profile/types";
import { hasRole, AT_LEAST_ENTERPRISE_ADMIN, AT_LEAST_STORE_MANAGER } from "@/lib/roles";

/**
 * Staff of one location — the location-side view of `user_locations`. No per-location endpoint
 * exists, so every change is a read-modify-write on the user's own list (GET + PUT
 * /api/users/:id/locations, full replace) which keeps the user's other locations intact.
 * Viewing needs GET /api/users (store_manager+); editing is enterprise_admin+ only.
 */
export default function LocationStaffPage() {
  const t = useTranslations("Dashboard.locations.staff");
  const tRoles = useTranslations("Dashboard.roles");
  const tCommon = useTranslations("Common");
  const intlLocale = useLocale();
  const { id: locationId } = useParams<{ id: string }>();
  const qc = useQueryClient();
  const { data: me } = useMe();
  const canView = hasRole(me?.role, AT_LEAST_STORE_MANAGER);
  const canEdit = hasRole(me?.role, AT_LEAST_ENTERPRISE_ADMIN);

  const { data: location } = useLocation(locationId);
  // Header store filter is intentionally bypassed: candidates come from the whole tenant.
  const { data: users, isLoading: usersLoading } = useQuery({
    queryKey: [...USERS_KEY, "location-staff", locationId] as const,
    queryFn: () => usersApi.getAll(),
    enabled: canView,
  });
  const { data: assignedUsers, isLoading: assignedLoading } = useQuery({
    queryKey: [...USERS_KEY, "location-staff", locationId, "assigned"] as const,
    queryFn: () => usersApi.getAll([locationId]),
    enabled: canView,
  });

  const [search, setSearch] = useState("");
  const [roleFilter, setRoleFilter] = useState("");
  const [picked, setPicked] = useState<string[]>([]);

  const assignedIds = useMemo(
    () => new Set((assignedUsers ?? []).filter((u) => isLocationScopedRole(u.role)).map((u) => u.id)),
    [assignedUsers],
  );
  const assigned = useMemo(() => (users ?? []).filter((u) => assignedIds.has(u.id)), [users, assignedIds]);
  const candidates = useMemo(
    () => (users ?? []).filter((u) => u.isActive && isLocationScopedRole(u.role) && !assignedIds.has(u.id)),
    [users, assignedIds],
  );
  const rows = useMemo(() => {
    const q = search.trim().toLowerCase();
    return assigned.filter(
      (u) =>
        (!roleFilter || u.role === roleFilter) &&
        (!q || u.fullName.toLowerCase().includes(q) || u.email.toLowerCase().includes(q) || (u.phone ?? "").includes(q)),
    );
  }, [assigned, search, roleFilter]);

  const change = useMutation({
    mutationFn: async ({ targets, add }: { targets: UserDto[]; add: boolean }) => {
      for (const user of targets) {
        const current = await usersApi.getLocations(user.id);
        const next = add
          ? Array.from(new Set([...current.locationIds, locationId]))
          : current.locationIds.filter((x) => x !== locationId);
        await usersApi.setLocations(user.id, { locationIds: next });
      }
    },
    onSuccess: (_r, { add }) => {
      toast.success(add ? t("toastAdded") : t("toastRemoved"));
      setPicked([]);
    },
    onError: (e: Error) => toast.error(t("toastError", { message: e.message })),
    onSettled: () => qc.invalidateQueries({ queryKey: USERS_KEY }),
  });

  const columns: TableColumn<UserDto>[] = [
    {
      key: "name",
      header: t("colName"),
      render: (u) => (
        <div>
          <div style={{ color: "#E8EDF5", fontWeight: 600, fontSize: 13 }}>{u.fullName}</div>
          <div style={{ color: "#6B7280", fontSize: 11 }}>{u.email}</div>
        </div>
      ),
    },
    { key: "role", header: t("colRole"), render: (u) => getRoleLabel(tRoles, u.role), cellStyle: { color: "#9CA3AF", fontSize: 13 } },
    { key: "phone", header: t("colPhone"), render: (u) => u.phone ?? "—", cellStyle: { color: "#9CA3AF", fontSize: 13 } },
    {
      key: "status",
      header: t("colStatus"),
      render: (u) => (
        <span style={{ color: u.isActive ? "#22c55e" : "#6B7280", fontSize: 12, fontWeight: 600 }}>
          {u.isActive ? t("active") : t("inactive")}
        </span>
      ),
    },
    {
      key: "last",
      header: t("colLastActive"),
      cellStyle: { color: "#6B7280", fontSize: 12 },
      render: (u) => (u.lastActiveAt ? new Date(u.lastActiveAt).toLocaleString(intlLocale) : "—"),
    },
    {
      key: "actions",
      header: "",
      render: (u) => (
        <div style={{ display: "flex", gap: 8, justifyContent: "center" }}>
          <Link href="/users" style={{ color: "#60A5FA", fontSize: 12, textDecoration: "none", alignSelf: "center" }}>
            {t("openUsers")}
          </Link>
          {canEdit && (
            <Btn variant="ghost" size="sm" disabled={change.isPending} onClick={() => change.mutate({ targets: [u], add: false })}>
              {t("remove")}
            </Btn>
          )}
        </div>
      ),
    },
  ];

  const loading = usersLoading || assignedLoading;

  return (
    <PageContainer>
      <BackLink href="/locations">{t("back")}</BackLink>
      <PageHeader title={t("title", { name: location?.name ?? "…" })} subtitle={t("hint")} />

      {!canView ? (
        <div style={{ color: "#6B7280", fontSize: 13 }}>{t("noAccess")}</div>
      ) : (
        <>
          <div style={{ display: "flex", gap: 10, flexWrap: "wrap", alignItems: "center" }}>
            <input
              type="text"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder={t("searchPlaceholder")}
              style={{ ...filterInputStyle, width: 260 }}
            />
            <select value={roleFilter} onChange={(e) => setRoleFilter(e.target.value)} style={filterInputStyle}>
              <option value="">{t("allRoles")}</option>
              {ROLE_KEYS.filter(isLocationScopedRole).map((r) => (
                <option key={r} value={r}>{getRoleLabel(tRoles, r)}</option>
              ))}
            </select>
            {canEdit && (
              <div style={{ marginLeft: "auto", display: "flex", gap: 8, alignItems: "center" }}>
                {candidates.length === 0 ? (
                  <span style={{ color: "#6B7280", fontSize: 12 }}>{t("noCandidates")}</span>
                ) : (
                  <LocationsMultiSelectDropdown
                    locations={candidates.map((u) => ({ id: u.id, name: `${u.fullName} — ${getRoleLabel(tRoles, u.role)}` }))}
                    selectedIds={picked}
                    onToggle={(id) => setPicked((p) => (p.includes(id) ? p.filter((x) => x !== id) : [...p, id]))}
                    summaryLabel={t("pickedCount", { count: picked.length })}
                    placeholderLabel={t("pickPlaceholder")}
                    doneLabel={t("done")}
                    disabled={change.isPending}
                  />
                )}
                <Btn
                  size="sm"
                  disabled={picked.length === 0 || change.isPending}
                  onClick={() => change.mutate({ targets: candidates.filter((u) => picked.includes(u.id)), add: true })}
                >
                  {t("add")}
                </Btn>
              </div>
            )}
          </div>

          <Table
            columns={columns}
            rows={rows}
            rowKey={(u) => u.id}
            isLoading={loading}
            emptyMessage={loading ? tCommon("loading") : t("empty")}
          />
        </>
      )}
    </PageContainer>
  );
}
