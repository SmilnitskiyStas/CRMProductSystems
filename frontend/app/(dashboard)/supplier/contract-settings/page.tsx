"use client";

import { ContractSettingsForm } from "@/features/supplier-cabinet/components/ContractSettingsForm";
import { useMe } from "@/features/auth/hooks/useAuth";
import { SUPPLIER_ONLY, hasRole } from "@/lib/roles";

export default function SupplierContractSettingsPage() {
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
          Реквізити договору
        </h1>
        <p style={{ color: "#4B5563", fontSize: 14, marginTop: 6 }}>
          Юридичні реквізити, підпис і печатка для генерації договорів про співпрацю
        </p>
      </div>
      <ContractSettingsForm />
    </div>
  );
}
