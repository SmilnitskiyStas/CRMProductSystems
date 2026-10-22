"use client";

import { useEffect } from "react";
import { useTranslations } from "next-intl";
import { PageContainer } from "@/components/ui/PageContainer";
import { PageHeader } from "@/components/ui/PageHeader";
import { CabinetOrdersTab } from "@/features/supplier-cabinet/components/CabinetOrdersTab";
import { useMarkOrdersSeen } from "@/features/supplier-cabinet/hooks/useCabinetCooperation";
import { useMe } from "@/features/auth/hooks/useAuth";
import { SUPPLIER_ONLY, hasRole } from "@/lib/roles";

export default function SupplierOrdersPage() {
  const t = useTranslations("Dashboard.supplierCabinet.pages");
  const { data: me } = useMe();

  // Opening the orders tab clears the "new order arrived" sidebar badge (Phase 6a).
  const markSeen = useMarkOrdersSeen();
  const markSeenMutate = markSeen.mutate;
  useEffect(() => {
    markSeenMutate();
  }, [markSeenMutate]);

  if (me && !hasRole(me.role, SUPPLIER_ONLY)) {
    return (
      <PageContainer>
        <div style={{ color: "#F87171", fontSize: 14 }}>{t("supplierOnlyAccess")}</div>
      </PageContainer>
    );
  }

  return (
    <PageContainer>
      <PageHeader title={t("orders.title")} subtitle={t("orders.subtitle")} />
      <CabinetOrdersTab />
    </PageContainer>
  );
}
