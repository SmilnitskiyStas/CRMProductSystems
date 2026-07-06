"use client";

import { CooperationRequestsTab } from "@/features/supplier-cabinet/components/CooperationRequestsTab";
import { useMe } from "@/features/auth/hooks/useAuth";
import { SUPPLIER_ONLY, hasRole } from "@/lib/roles";

export default function SupplierRequestsPage() {
  const { data: me } = useMe();

  if (me && !hasRole(me.role, SUPPLIER_ONLY)) {
    return (
      <div style={{ padding: "28px 32px", color: "#F87171", fontSize: 14 }}>
        Доступ лише для адміністраторів постачальника.
      </div>
    );
  }

  return (
    <div style={{ padding: "28px 32px" }}>
      <div style={{ marginBottom: 24 }}>
        <h1 style={{ color: "#E8EDF5", fontSize: 22, fontWeight: 700, margin: 0 }}>
          Заявки на співпрацю
        </h1>
        <p style={{ color: "#4B5563", fontSize: 14, marginTop: 6 }}>
          Схвалення заявок клієнтів, договори та підписання через Вчасно
        </p>
      </div>
      <CooperationRequestsTab />
    </div>
  );
}
