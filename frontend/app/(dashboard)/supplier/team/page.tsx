"use client";

import { useTranslations } from "next-intl";
import { CabinetStaffPanel } from "@/features/supplier-cabinet/components/CabinetStaffPanel";
import { RolesTab } from "@/features/supplier-cabinet/components/RolesTab";
import { useMe } from "@/features/auth/hooks/useAuth";
import { SUPPLIER_ONLY, hasRole } from "@/lib/roles";

export default function SupplierTeamPage() {
  const t = useTranslations("Dashboard.supplierCabinet.pages");
  const { data: me } = useMe();

  if (me && !hasRole(me.role, SUPPLIER_ONLY)) {
    return (
      <div style={{ padding: "28px 32px", color: "#F87171", fontSize: 14 }}>
        {t("supplierOnlyAccess")}
      </div>
    );
  }

  // null permissions = full/owner access; a restricted staff role without
  // staff_management should not reach this page directly by URL either.
  if (me?.permissions && !me.permissions.staff_management) {
    return (
      <div style={{ padding: "28px 32px", color: "#F87171", fontSize: 14 }}>
        {t("team.noAccess")}
      </div>
    );
  }

  return (
    <div style={{ padding: "28px 32px" }}>
      <div style={{ marginBottom: 24 }}>
        <h1 style={{ color: "#E8EDF5", fontSize: 22, fontWeight: 700, margin: 0 }}>
          {t("team.title")}
        </h1>
        <p style={{ color: "#4B5563", fontSize: 14, marginTop: 6 }}>
          {t("team.subtitle")}
        </p>
      </div>
      <div
        style={{
          display: "grid",
          gridTemplateColumns: "1fr",
          gap: 24,
        }}
        className="lg:grid-cols-2"
      >
        <CabinetStaffPanel />
        <RolesTab />
      </div>
    </div>
  );
}
