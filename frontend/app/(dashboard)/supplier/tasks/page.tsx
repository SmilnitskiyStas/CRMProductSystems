"use client";

import { useTranslations } from "next-intl";
import { PageContainer } from "@/components/ui/PageContainer";
import { PageHeader } from "@/components/ui/PageHeader";
import { TasksBoard } from "@/features/supplier-cabinet/components/TasksBoard";
import { useMe } from "@/features/auth/hooks/useAuth";
import { SUPPLIER_ONLY, hasRole } from "@/lib/roles";

export default function SupplierTasksPage() {
  const t = useTranslations("Dashboard.supplierCabinet.pages");
  const { data: me } = useMe();

  if (me && !hasRole(me.role, SUPPLIER_ONLY)) {
    return (
      <PageContainer>
        <div style={{ color: "#F87171", fontSize: 14 }}>{t("supplierOnlyAccess")}</div>
      </PageContainer>
    );
  }

  // null permissions = full/owner access; a restricted staff role without
  // task_board should not reach this page directly by URL either.
  if (me?.permissions && !me.permissions.task_board) {
    return (
      <PageContainer>
        <div style={{ color: "#F87171", fontSize: 14 }}>{t("tasks.noAccess")}</div>
      </PageContainer>
    );
  }

  return (
    <PageContainer>
      <PageHeader title={t("tasks.title")} subtitle={t("tasks.subtitle")} />
      <TasksBoard />
    </PageContainer>
  );
}
