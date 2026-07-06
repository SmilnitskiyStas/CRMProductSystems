"use client";

import { CabinetStaffPanel } from "@/features/supplier-cabinet/components/CabinetStaffPanel";
import { RolesTab } from "@/features/supplier-cabinet/components/RolesTab";
import { useMe } from "@/features/auth/hooks/useAuth";
import { SUPPLIER_ONLY, hasRole } from "@/lib/roles";

export default function SupplierTeamPage() {
  const { data: me } = useMe();

  if (me && !hasRole(me.role, SUPPLIER_ONLY)) {
    return (
      <div style={{ padding: "28px 32px", color: "#F87171", fontSize: 14 }}>
        Доступ лише для адміністраторів постачальника.
      </div>
    );
  }

  // null permissions = full/owner access; a restricted staff role without
  // staff_management should not reach this page directly by URL either.
  if (me?.permissions && !me.permissions.staff_management) {
    return (
      <div style={{ padding: "28px 32px", color: "#F87171", fontSize: 14 }}>
        Немає доступу до управління командою.
      </div>
    );
  }

  return (
    <div style={{ padding: "28px 32px" }}>
      <div style={{ marginBottom: 24 }}>
        <h1 style={{ color: "#E8EDF5", fontSize: 22, fontWeight: 700, margin: 0 }}>
          Команда
        </h1>
        <p style={{ color: "#4B5563", fontSize: 14, marginTop: 6 }}>
          Співробітники кабінету постачальника та ролі доступу
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
