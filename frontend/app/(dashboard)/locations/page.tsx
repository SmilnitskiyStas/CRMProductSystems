"use client";

import { useState } from "react";
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
import type { LocationDto, LocationType } from "@/features/locations/types";
import { useMe } from "@/features/auth/hooks/useAuth";
import { hasRole, AT_LEAST_ENTERPRISE_ADMIN } from "@/lib/roles";

// ── Page ───────────────────────────────────────────────────────────────────────

export default function LocationsPage() {
  const t = useTranslations("Dashboard.locations.page");
  const tTypes = useTranslations("Dashboard.locations.types");
  const tCommon = useTranslations("Common");
  const { data: locations, isLoading } = useLocations();
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
  }) {
    if (dialog === "create") {
      create.mutate(
        {
          name: values.name,
          address: values.address,
          locationType: values.locationType,
          legalEntityId: values.legalEntityId,
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

      {/* Table */}
      {isLoading ? (
        <div style={{ color: "#4B5563", fontSize: 13, textAlign: "center", padding: "48px 0" }}>
          {tCommon("loading")}
        </div>
      ) : !locations?.length ? (
        <div style={{ color: "#4B5563", fontSize: 13, textAlign: "center", padding: "48px 0" }}>
          {t("empty")}
        </div>
      ) : (
        <div
          style={{
            background: "#161B26",
            border: "1px solid #1F2937",
            borderRadius: 12,
            overflow: "hidden",
          }}
        >
          <table style={{ width: "100%", borderCollapse: "collapse" }}>
            <thead>
              <tr style={{ borderBottom: "1px solid #1F2937" }}>
                {[
                  t("headers.name"),
                  t("headers.type"),
                  t("headers.address"),
                  t("headers.zones"),
                  t("headers.status"),
                  "",
                ].map((h) => (
                  <th
                    key={h}
                    style={{
                      color: "#4B5563",
                      fontSize: 11,
                      fontWeight: 600,
                      textTransform: "uppercase",
                      letterSpacing: "0.04em",
                      padding: "12px 16px",
                      textAlign: "left",
                    }}
                  >
                    {h}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {locations.map((loc) => (
                <tr
                  key={loc.id}
                  style={{ borderBottom: "1px solid #1F2937" }}
                >
                  <td style={tdStyle}>
                    <span style={{ color: "#E8EDF5", fontWeight: 600, fontSize: 13 }}>{loc.name}</span>
                  </td>
                  <td style={tdStyle}>
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
                  </td>
                  <td style={{ ...tdStyle, color: "#6B7280", fontSize: 13 }}>
                    {loc.address ?? "—"}
                  </td>
                  <td style={{ ...tdStyle, color: "#9CA3AF", fontSize: 13 }}>
                    {loc.zones.length}
                  </td>
                  <td style={tdStyle}>
                    <span
                      style={{
                        color: loc.isActive ? "#22c55e" : "#6B7280",
                        fontSize: 12,
                        fontWeight: 600,
                      }}
                    >
                      {loc.isActive ? t("statusActive") : t("statusInactive")}
                    </span>
                  </td>
                  <td style={{ ...tdStyle, textAlign: "right" }}>
                    <div style={{ display: "flex", alignItems: "center", gap: 8, justifyContent: "flex-end" }}>
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
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

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

const tdStyle: React.CSSProperties = {
  padding: "14px 16px",
  verticalAlign: "middle",
};
