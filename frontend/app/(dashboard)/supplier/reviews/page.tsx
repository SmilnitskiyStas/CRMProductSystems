"use client";

import { CabinetReviews } from "@/features/supplier-cabinet/components/CabinetReviews";
import { useMe } from "@/features/auth/hooks/useAuth";
import { SUPPLIER_ONLY, hasRole } from "@/lib/roles";

export default function SupplierReviewsPage() {
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
          Відгуки та рейтинг
        </h1>
        <p style={{ color: "#4B5563", fontSize: 14, marginTop: 6 }}>
          Відгуки клієнтів про вашу компанію (лише для перегляду)
        </p>
      </div>
      <CabinetReviews />
    </div>
  );
}
