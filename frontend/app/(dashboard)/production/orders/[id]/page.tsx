"use client";

import { use } from "react";
import { useModules } from "@/features/modules/hooks/useModules";
import { ProductionOrderDetail } from "@/features/production/components/ProductionOrderDetail";

interface Props {
  params: Promise<{ id: string }>;
}

export default function ProductionOrderDetailPage({ params }: Props) {
  const { id } = use(params);
  const { data: modulesData } = useModules();
  const isActive = !modulesData || modulesData.modules.includes("production");

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
          Модуль Виробництво не активний
        </h2>
      </div>
    );
  }

  return <ProductionOrderDetail id={id} />;
}
