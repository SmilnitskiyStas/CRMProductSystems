"use client";

import { useParams } from "next/navigation";
import { useTranslations } from "next-intl";
import { useModules } from "@/features/modules/hooks/useModules";
import { PageContainer } from "@/components/ui/PageContainer";
import { BackLink } from "@/components/ui/BackLink";
import { WorkOrderDetail } from "@/features/auto-service/components/WorkOrderDetail";

export default function WorkOrderDetailPage() {
  const { id } = useParams<{ id: string }>();
  const t = useTranslations("Dashboard.autoService.moduleGate");
  const tPage = useTranslations("Dashboard.autoService.workOrdersPage");
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
          {t("title")}
        </h2>
      </div>
    );
  }

  return (
    <PageContainer>
      <BackLink href="/auto-service">{tPage("backToBoard")}</BackLink>
      <WorkOrderDetail id={id} />
    </PageContainer>
  );
}
