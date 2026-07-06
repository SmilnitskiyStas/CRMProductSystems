"use client";

import { ClientsTab } from "@/features/supplier-cabinet/components/ClientsTab";
import { useMe } from "@/features/auth/hooks/useAuth";
import { SUPPLIER_ONLY, hasRole } from "@/lib/roles";

export default function SupplierClientsPage() {
  const { data: me } = useMe();

  if (me && !hasRole(me.role, SUPPLIER_ONLY)) {
    return (
      <div style={{ padding: "28px 32px", color: "#F87171", fontSize: 14 }}>
        Доступ лише для адміністраторів постачальника.
      </div>
    );
  }

  // null permissions = full/owner access; a restricted staff role without
  // client_management should not reach this page directly by URL either.
  if (me?.permissions && !me.permissions.client_management) {
    return (
      <div style={{ padding: "28px 32px", color: "#F87171", fontSize: 14 }}>
        Немає доступу до клієнтів.
      </div>
    );
  }

  return (
    <div style={{ padding: "28px 32px" }}>
      <div style={{ marginBottom: 24 }}>
        <h1 style={{ color: "#E8EDF5", fontSize: 22, fontWeight: 700, margin: 0 }}>
          Клієнти
        </h1>
        <p style={{ color: "#4B5563", fontSize: 14, marginTop: 6 }}>
          Клієнти кабінету постачальника — відгуки, завдання, переписка
        </p>
      </div>
      <ClientsTab />
    </div>
  );
}
