"use client";

import { useEffect, useState } from "react";
import { useTranslations } from "next-intl";
import { useLocations } from "@/features/locations/hooks/useLocations";
import { useUserLocations, useSetUserLocations } from "../hooks/useUserLocations";
import { Btn } from "@/components/ui/Btn";
import type { UserDto } from "../types";

interface Props {
  user: UserDto;
}

/**
 * Full-replace multi-location "territory" editor for a network_manager user (TASK-392c,
 * Feature 2 Stage 1). Caller must gate visibility to enterprise_admin+ viewing a
 * network_manager target — this component does not check roles itself, since
 * PUT/GET /api/users/:id/locations is AtLeastEnterpriseAdmin-only with no capability bypass
 * (anti-escalation), same posture as TenantRoleSelector.
 */
export function UserLocationsEditor({ user }: Props) {
  const t = useTranslations("Dashboard.users.locationsEditor");
  const { data: locations, isLoading: locationsLoading } = useLocations();
  const { data: current, isLoading: currentLoading } = useUserLocations(user.id);
  const setLocations = useSetUserLocations();

  const [selected, setSelected] = useState<string[]>([]);
  const [initialized, setInitialized] = useState(false);
  const [saved, setSaved] = useState(false);
  const [err, setErr] = useState("");

  // Seed local selection once the current assignment list has loaded. Separate GET (no such
  // field on UserDto), so — unlike TenantRoleSelector's useState(user.tenantRoleId) — this
  // arrives a tick after mount; the `initialized` guard makes sure we only seed once and
  // never clobber the admin's in-progress edits on a background refetch.
  useEffect(() => {
    if (current && !initialized) {
      setSelected(current.locationIds);
      setInitialized(true);
    }
  }, [current, initialized]);

  const activeLocations = (locations ?? []).filter((l) => l.isActive);
  const original = current?.locationIds ?? [];
  const dirty =
    selected.length !== original.length || selected.some((id) => !original.includes(id));
  const loading = locationsLoading || currentLoading;

  function toggle(id: string) {
    setSaved(false);
    setSelected((prev) => (prev.includes(id) ? prev.filter((x) => x !== id) : [...prev, id]));
  }

  async function handleSave() {
    setErr("");
    try {
      await setLocations.mutateAsync({ userId: user.id, locationIds: selected });
      setSaved(true);
      setTimeout(() => setSaved(false), 3000);
    } catch (e) {
      setErr((e as Error)?.message ?? t("saveErrorFallback"));
    }
  }

  return (
    <div
      style={{
        marginBottom: 20,
        padding: "12px 14px",
        borderRadius: 8,
        background: "#0D1117",
        border: "1px solid #1F2937",
      }}
    >
      <div
        style={{
          color: "#9CA3AF", fontSize: 11, fontWeight: 600,
          textTransform: "uppercase", letterSpacing: "0.05em", marginBottom: 8,
        }}
      >
        {t("label")}
      </div>

      {loading ? (
        <div style={{ color: "#374151", fontSize: 12 }}>{t("loading")}</div>
      ) : activeLocations.length === 0 ? (
        <div style={{ color: "#374151", fontSize: 12 }}>{t("emptyHint")}</div>
      ) : (
        <div
          style={{
            display: "flex", flexDirection: "column", gap: 6,
            marginBottom: 10, maxHeight: 220, overflowY: "auto",
          }}
        >
          {activeLocations.map((loc) => (
            <label
              key={loc.id}
              style={{
                display: "flex", alignItems: "center", gap: 8,
                fontSize: 13, color: "#E8EDF5", cursor: "pointer",
              }}
            >
              <input
                type="checkbox"
                checked={selected.includes(loc.id)}
                onChange={() => toggle(loc.id)}
              />
              {loc.name}
            </label>
          ))}
        </div>
      )}

      <div style={{ display: "flex", gap: 10, alignItems: "center" }}>
        <Btn size="sm" onClick={handleSave} disabled={!dirty || setLocations.isPending || loading}>
          {setLocations.isPending ? t("savingButton") : saved ? t("savedButton") : t("saveButton")}
        </Btn>
        <span style={{ color: "#4B5563", fontSize: 11 }}>
          {t("selectedCount", { count: selected.length })}
        </span>
      </div>

      {err && <div style={{ color: "#F87171", fontSize: 12, marginTop: 6 }}>{err}</div>}
    </div>
  );
}
