"use client";

import { use } from "react";
import { useRouter } from "next/navigation";
import { ArrowLeft } from "lucide-react";
import { useModules } from "@/features/modules/hooks/useModules";
import { WorkOrderDetail } from "@/features/auto-service/components/WorkOrderDetail";

interface Props {
  params: Promise<{ id: string }>;
}

export default function WorkOrderDetailPage({ params }: Props) {
  const { id } = use(params);
  const router = useRouter();
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
      </div>
    );
  }

  return (
    <div>
      {/* Back button */}
      <div style={{ padding: "16px 32px 0", borderBottom: "1px solid #1F2937" }}>
        <button
          onClick={() => router.push("/auto-service")}
          style={{
            display: "flex",
            alignItems: "center",
            gap: 8,
            background: "transparent",
            border: "none",
            color: "#6B7280",
            fontSize: 13,
            cursor: "pointer",
            padding: "6px 0",
          }}
        >
          <ArrowLeft size={14} />
          Назад до дошки
        </button>
      </div>
      <WorkOrderDetail id={id} />
    </div>
  );
}
