"use client";

import { useState } from "react";
import Link from "next/link";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useTranslations } from "next-intl";
import { toast } from "sonner";
import { Btn } from "@/components/ui/Btn";
import { usersApi } from "@/features/users/api/users";
import { USERS_KEY } from "@/features/users/hooks/useUsers";
import { isLocationScopedRole, type UserDto } from "@/features/users/types";
import { getRoleLabel } from "@/features/profile/types";
import type { LocationDto } from "../types";

interface Props {
  location: LocationDto;
  /** enterprise_admin+ only — PUT /api/users/:id/locations is AtLeastEnterpriseAdmin on the backend. */
  canEdit: boolean;
  onClose: () => void;
}

/**
 * "Персонал" of one location: the location-side view of `user_locations`. Lists users with
 * a store-scoped role assigned to this location and, for enterprise_admin+, adds/removes them.
 * There is no per-location endpoint — each change is a read-modify-write on the user's own
 * assignment list (GET + PUT /api/users/:id/locations, full replace), so other locations of
 * that user are preserved.
 */
export function LocationStaffDialog({ location, canEdit, onClose }: Props) {
  const t = useTranslations("Dashboard.locations.staff");
  const tRoles = useTranslations("Dashboard.roles");
  const qc = useQueryClient();
  const [pickId, setPickId] = useState("");

  // All tenant users (header store filter is intentionally bypassed) — needed to offer
  // candidates; the assigned subset is derived from each user's own assignment list below.
  const { data: users, isLoading: usersLoading } = useQuery({
    queryKey: [...USERS_KEY, "location-staff", location.id] as const,
    queryFn: () => usersApi.getAll(),
  });
  const { data: assignedUsers, isLoading: assignedLoading } = useQuery({
    queryKey: [...USERS_KEY, "location-staff", location.id, "assigned"] as const,
    queryFn: () => usersApi.getAll([location.id]),
  });

  const assignedIds = new Set(
    (assignedUsers ?? []).filter((u) => isLocationScopedRole(u.role)).map((u) => u.id),
  );
  const assigned = (users ?? []).filter((u) => assignedIds.has(u.id) && u.isActive);
  const candidates = (users ?? []).filter(
    (u) => u.isActive && isLocationScopedRole(u.role) && !assignedIds.has(u.id),
  );

  const change = useMutation({
    mutationFn: async ({ user, add }: { user: UserDto; add: boolean }) => {
      const current = await usersApi.getLocations(user.id);
      const next = add
        ? Array.from(new Set([...current.locationIds, location.id]))
        : current.locationIds.filter((id) => id !== location.id);
      return usersApi.setLocations(user.id, { locationIds: next });
    },
    onSuccess: (_r, { add }) => {
      toast.success(add ? t("toastAdded") : t("toastRemoved"));
      setPickId("");
      qc.invalidateQueries({ queryKey: USERS_KEY });
    },
    onError: (e: Error) => toast.error(t("toastError", { message: e.message })),
  });

  const loading = usersLoading || assignedLoading;

  return (
    <div
      onClick={onClose}
      style={{
        position: "fixed", inset: 0, background: "rgba(0,0,0,0.6)", zIndex: 50,
        display: "flex", alignItems: "center", justifyContent: "center", padding: 16,
      }}
    >
      <div
        onClick={(e) => e.stopPropagation()}
        style={{
          background: "#111827", border: "1px solid #1F2937", borderRadius: 12,
          width: "100%", maxWidth: 520, maxHeight: "85vh", overflowY: "auto", padding: 22,
        }}
      >
        <h2 style={{ color: "#E8EDF5", fontSize: 17, fontWeight: 700, margin: 0 }}>
          {t("title", { name: location.name })}
        </h2>
        <p style={{ color: "#6B7280", fontSize: 12, margin: "6px 0 14px" }}>{t("hint")}</p>

        {loading ? (
          <div style={{ color: "#6B7280", fontSize: 13 }}>{t("loading")}</div>
        ) : assigned.length === 0 ? (
          <div style={{ color: "#6B7280", fontSize: 13, marginBottom: 14 }}>{t("empty")}</div>
        ) : (
          <ul style={{ listStyle: "none", padding: 0, margin: "0 0 14px", display: "flex", flexDirection: "column", gap: 6 }}>
            {assigned.map((u) => (
              <li
                key={u.id}
                style={{
                  display: "flex", alignItems: "center", justifyContent: "space-between", gap: 10,
                  background: "#0D1117", border: "1px solid #1F2937", borderRadius: 8, padding: "8px 12px",
                }}
              >
                <div style={{ minWidth: 0 }}>
                  <div style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 600 }}>{u.fullName}</div>
                  <div style={{ color: "#6B7280", fontSize: 11 }}>{getRoleLabel(tRoles, u.role)}</div>
                </div>
                {canEdit && (
                  <Btn
                    variant="ghost"
                    size="sm"
                    disabled={change.isPending}
                    onClick={() => change.mutate({ user: u, add: false })}
                  >
                    {t("remove")}
                  </Btn>
                )}
              </li>
            ))}
          </ul>
        )}

        {canEdit && !loading && (
          <div style={{ display: "flex", gap: 8, alignItems: "center", marginBottom: 14 }}>
            <select
              value={pickId}
              onChange={(e) => setPickId(e.target.value)}
              disabled={candidates.length === 0 || change.isPending}
              style={{
                flex: 1, background: "#0D1117", border: "1px solid #1F2937", borderRadius: 8,
                color: "#E8EDF5", fontSize: 13, padding: "7px 10px",
              }}
            >
              <option value="">
                {candidates.length === 0 ? t("noCandidates") : t("pickPlaceholder")}
              </option>
              {candidates.map((u) => (
                <option key={u.id} value={u.id}>
                  {u.fullName} — {getRoleLabel(tRoles, u.role)}
                </option>
              ))}
            </select>
            <Btn
              size="sm"
              disabled={!pickId || change.isPending}
              onClick={() => {
                const user = candidates.find((u) => u.id === pickId);
                if (user) change.mutate({ user, add: true });
              }}
            >
              {t("add")}
            </Btn>
          </div>
        )}

        <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
          <Link href="/users" style={{ color: "#60A5FA", fontSize: 12, textDecoration: "none" }}>
            {t("openUsers")}
          </Link>
          <Btn variant="ghost" size="sm" onClick={onClose}>
            {t("close")}
          </Btn>
        </div>
      </div>
    </div>
  );
}
