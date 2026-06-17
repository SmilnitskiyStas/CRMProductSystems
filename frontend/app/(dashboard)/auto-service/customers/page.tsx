"use client";

import { useModules } from "@/features/modules/hooks/useModules";
import { CustomerTable } from "@/features/auto-service/components/CustomerTable";

export default function AutoServiceCustomersPage() {
  const { data: modulesData } = useModules();
  const isActive = !modulesData || modulesData.modules.includes("auto_service");

  if (!isActive) {
    return (
      <div
        style={{
          padding: "80px 32px",
          textAlign: "center",
          display: "flex",
          flexDirection: "column",
          alignItems: "center",
          gap: 16,
        }}
      >
        <div style={{ fontSize: 40 }}>🔒</div>
        <h2 style={{ color: "#E8EDF5", fontSize: 20, fontWeight: 700, margin: 0 }}>
          Модуль Auto Service не активний
        </h2>
        <p style={{ color: "#4B5563", fontSize: 14, maxWidth: 440 }}>
          Зверніться до адміністратора платформи, щоб увімкнути модуль «Автосервіс».
        </p>
      </div>
    );
  }

  return (
    <div style={{ padding: "28px 32px", maxWidth: 1100 }}>
      <CustomerTable />
    </div>
  );
}
